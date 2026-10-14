// AI生成
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimFirstPerson3D
{
    // 第一人称右上角雷达迷你地图。
    // 支持就地多级缩放（近距战术 22 格 / 标称 36 格 / 广域 58 格 / 全图总览），
    // 渲染地表、墙体、门扉、战争迷雾，并在正中以实心黄点与朝向箭头标出玩家位置与朝向。
    // 所有标记严格遵循全项目实心填充铁律（Solid Filled）。
    [StaticConstructorOnStartup]
    public static class MinimapHud3D
    {
        public const float Size = 160f;
        public const float Margin = 12f;

        public enum ZoomMode
        {
            Close = 0,   // 22 格
            Normal = 1,  // 36 格
            Wide = 2,    // 58 格
            Full = 3     // 全图
        }

        public static ZoomMode CurrentZoom = ZoomMode.Normal;

        private static Texture2D radarTex;
        private static Color32[] pixels = new Color32[128 * 128];
        private static Texture2D solid;
        private static float lastUpdateTime;
        private static Vector2 lastCenterPos = new Vector2(-9999f, -9999f);
        private static ZoomMode lastZoom = ZoomMode.Normal;
        private static Map lastMap;

        public static Rect Bounds => new Rect(UI.screenWidth - Size - Margin, Margin, Size, Size);

        private static void EnsureTextures()
        {
            if (solid == null)
            {
                solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solid.SetPixel(0, 0, Color.white);
                solid.Apply();
                solid.name = "Minimap3D_Solid";
            }
            if (radarTex == null)
            {
                radarTex = new Texture2D(128, 128, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "Minimap3D_RadarTex"
                };
            }
        }

        private static void Fill(Rect r, Color c)
        {
            EnsureTextures();
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, solid);
            GUI.color = prev;
        }

        // 实心玩家指示器：正中心实心黄点 + 向前朝向实心三角指针
        private static void DrawPlayerMarker(float cx, float cy, float size, Color c)
        {
            EnsureTextures();
            Color prev = GUI.color;
            GUI.color = c;

            // 1. 正中心实心醒目黄点 (6x6 矩阵)
            Fill(new Rect(cx - 3f, cy - 3f, 6f, 6f), c);

            // 2. 向前指向的前方实心三角箭头 (在局部未旋转空间中指向上方 -Y)
            Fill(new Rect(cx - 0.75f, cy - size * 0.5f, 1.5f, 1.5f), c);
            Fill(new Rect(cx - 1.75f, cy - size * 0.5f + 1.5f, 3.5f, 1.5f), c);
            Fill(new Rect(cx - 2.75f, cy - size * 0.5f + 3.0f, 5.5f, 1.5f), c);
            Fill(new Rect(cx - 3.75f, cy - size * 0.5f + 4.5f, 7.5f, 1.5f), c);
            Fill(new Rect(cx - 1.5f, cy - size * 0.5f + 6.0f, 3f, 2.0f), c);

            GUI.color = prev;
        }

        public static void Draw(Pawn possessed)
        {
            EnsureTextures();
            Rect rect = Bounds;

            // 点击小地图切换缩放倍率
            HandleInput(rect);

            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            Map map = possessed != null ? possessed.MapHeld : Find.CurrentMap;
            if (map == null) return;

            Vector3 playerWorldPos = possessed != null ? possessed.DrawPos : CameraRig3D.LastCamPos;
            Vector2 playerPos2D = new Vector2(playerWorldPos.x, playerWorldPos.z);

            // 1. 更新雷达底图像素
            UpdateRadarTexture(map, playerPos2D);

            // 2. 绘制小地图底图与边框
            Fill(rect, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            GUI.DrawTexture(rect, radarTex);

            // 外边框线
            Color borderColor = new Color(0.55f, 0.52f, 0.45f, 0.85f);
            Fill(new Rect(rect.x, rect.y, rect.width, 1.5f), borderColor);
            Fill(new Rect(rect.x, rect.yMax - 1.5f, rect.width, 1.5f), borderColor);
            Fill(new Rect(rect.x, rect.y, 1.5f, rect.height), borderColor);
            Fill(new Rect(rect.xMax - 1.5f, rect.y, 1.5f, rect.height), borderColor);

            // 3. 正北指示标记（顶部中心实心小标签）
            float northW = 16f, northH = 12f;
            Rect northRect = new Rect(rect.center.x - northW * 0.5f, rect.y - 1f, northW, northH);
            Fill(northRect, new Color(0.08f, 0.09f, 0.12f, 0.95f));
            Fill(new Rect(northRect.x, northRect.y, northRect.width, 1f), borderColor);
            Fill(new Rect(northRect.x, northRect.yMax - 1f, northRect.width, 1f), borderColor);
            Fill(new Rect(northRect.x, northRect.y, 1f, northRect.height), borderColor);
            Fill(new Rect(northRect.xMax - 1f, northRect.y, 1f, northRect.height), borderColor);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.95f, 0.92f, 0.80f, 0.95f);
            Widgets.Label(northRect, "N");

            // 4. 左下角缩放档位提示（简洁结果陈述，无教学文字）
            string zoomLabel = CurrentZoom == ZoomMode.Close ? "22m"
                             : CurrentZoom == ZoomMode.Normal ? "36m"
                             : CurrentZoom == ZoomMode.Wide ? "58m" : "ALL";
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = new Color(0.75f, 0.72f, 0.65f, 0.75f);
            Widgets.Label(new Rect(rect.x + 4f, rect.yMax - 16f, 32f, 14f), zoomLabel);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // 5. 动态实体绘制（周围 Pawn 实心色块与玩家自身黄点）
            DrawEntities(rect, map, possessed, playerPos2D);
        }

        private static void HandleInput(Rect rect)
        {
            if (Event.current == null) return;
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                CurrentZoom = (ZoomMode)(((int)CurrentZoom + 1) % 4);
                lastUpdateTime = 0f;
                SoundDefOf.Tick_High?.PlayOneShotOnCamera();
                Event.current.Use();
            }
        }

        private static float GetRadius(Map map)
        {
            switch (CurrentZoom)
            {
                case ZoomMode.Close: return 22f;
                case ZoomMode.Normal: return 36f;
                case ZoomMode.Wide: return 58f;
                case ZoomMode.Full: return Mathf.Max(map.Size.x, map.Size.z) * 0.5f;
                default: return 36f;
            }
        }

        private static void UpdateRadarTexture(Map map, Vector2 centerPos)
        {
            float now = Time.realtimeSinceStartup;
            float radius = GetRadius(map);

            bool needUpdate = radarTex == null
                || lastMap != map
                || lastZoom != CurrentZoom
                || (now - lastUpdateTime > 0.10f && ((centerPos - lastCenterPos).sqrMagnitude > 0.08f || now - lastUpdateTime > 0.35f));

            if (!needUpdate) return;

            lastUpdateTime = now;
            lastCenterPos = centerPos;
            lastZoom = CurrentZoom;
            lastMap = map;

            var fogGrid = map.fogGrid;
            var edificeGrid = map.edificeGrid;
            var terrainGrid = map.terrainGrid;
            var snowGrid = map.snowGrid;

            bool isFull = CurrentZoom == ZoomMode.Full;
            float mapSizeX = map.Size.x;
            float mapSizeZ = map.Size.z;

            // 128x128 采样
            for (int py = 0; py < 128; py++)
            {
                float v = py / 127f;
                int rowOffset = py * 128;

                for (int px = 0; px < 128; px++)
                {
                    float u = px / 127f;
                    int cx, cz;

                    if (isFull)
                    {
                        cx = Mathf.FloorToInt(u * mapSizeX);
                        cz = Mathf.FloorToInt(v * mapSizeZ);
                    }
                    else
                    {
                        cx = Mathf.FloorToInt(centerPos.x + (u - 0.5f) * (radius * 2f));
                        cz = Mathf.FloorToInt(centerPos.y + (v - 0.5f) * (radius * 2f));
                    }

                    if (cx < 0 || cz < 0 || cx >= mapSizeX || cz >= mapSizeZ)
                    {
                        pixels[rowOffset + px] = new Color32(10, 11, 14, 255);
                        continue;
                    }

                    IntVec3 c = new IntVec3(cx, 0, cz);

                    if (fogGrid != null && fogGrid.IsFogged(c))
                    {
                        pixels[rowOffset + px] = new Color32(20, 21, 25, 255);
                        continue;
                    }

                    Building edifice = edificeGrid != null ? edificeGrid[c] : null;
                    if (edifice != null)
                    {
                        if (edifice.def.IsDoor)
                        {
                            pixels[rowOffset + px] = new Color32(185, 155, 95, 255);
                        }
                        else if (edifice.def.building != null && edifice.def.building.isNaturalRock)
                        {
                            pixels[rowOffset + px] = new Color32(72, 70, 66, 255);
                        }
                        else
                        {
                            pixels[rowOffset + px] = new Color32(145, 140, 135, 255);
                        }
                        continue;
                    }

                    if (snowGrid != null && snowGrid.GetDepth(c) > 0.18f)
                    {
                        pixels[rowOffset + px] = new Color32(215, 225, 235, 255);
                        continue;
                    }

                    TerrainDef t = terrainGrid != null ? terrainGrid.TerrainAt(c) : null;
                    if (t == null)
                    {
                        pixels[rowOffset + px] = new Color32(60, 60, 60, 255);
                        continue;
                    }

                    if (t.IsWater)
                    {
                        if (t.defName.Contains("Deep"))
                            pixels[rowOffset + px] = new Color32(26, 52, 92, 255);
                        else
                            pixels[rowOffset + px] = new Color32(42, 82, 132, 255);
                    }
                    else if (t.defName.Contains("Sand"))
                    {
                        pixels[rowOffset + px] = new Color32(175, 155, 110, 255);
                    }
                    else if (t.defName.Contains("Marsh") || t.defName.Contains("Mud"))
                    {
                        pixels[rowOffset + px] = new Color32(65, 60, 45, 255);
                    }
                    else if (t.defName.Contains("Soil") || t.defName.Contains("Dirt"))
                    {
                        pixels[rowOffset + px] = new Color32(85, 72, 55, 255);
                    }
                    else if (t.defName.Contains("Concrete") || t.defName.Contains("Tile")
                             || t.defName.Contains("Paved") || t.defName.Contains("Metal"))
                    {
                        pixels[rowOffset + px] = new Color32(125, 125, 130, 255);
                    }
                    else
                    {
                        pixels[rowOffset + px] = new Color32(68, 95, 52, 255);
                    }
                }
            }

            radarTex.SetPixels32(pixels);
            radarTex.Apply(false);
        }

        private static void DrawEntities(Rect rect, Map map, Pawn possessed, Vector2 playerPos2D)
        {
            if (map.mapPawns == null) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            bool isFull = CurrentZoom == ZoomMode.Full;
            float radius = GetRadius(map);

            // 1. 周围其他 Pawn 标记
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p == possessed || p.Dead) continue;

                float sx, sy;
                if (isFull)
                {
                    sx = rect.x + (p.DrawPos.x / map.Size.x) * rect.width;
                    sy = rect.yMax - (p.DrawPos.z / map.Size.z) * rect.height;
                }
                else
                {
                    float dx = p.DrawPos.x - playerPos2D.x;
                    float dz = p.DrawPos.z - playerPos2D.y;
                    if (Mathf.Abs(dx) > radius || Mathf.Abs(dz) > radius) continue;

                    sx = rect.center.x + (dx / (radius * 2f)) * rect.width;
                    sy = rect.center.y - (dz / (radius * 2f)) * rect.height;
                }

                if (sx < rect.x + 3f || sx > rect.xMax - 3f || sy < rect.y + 3f || sy > rect.yMax - 3f)
                    continue;

                if (p.HostileTo(Faction.OfPlayer))
                {
                    // 敌对单位：实心红色块 (5x5)
                    Fill(new Rect(sx - 2.5f, sy - 2.5f, 5f, 5f), new Color(0.96f, 0.20f, 0.15f, 0.95f));
                }
                else if (p.IsColonist)
                {
                    if (p.Downed)
                    {
                        // 倒地殖民者：实心琥珀橙色块 (6x6)
                        Fill(new Rect(sx - 3f, sy - 3f, 6f, 6f), new Color(1.0f, 0.65f, 0.15f, 0.95f));
                    }
                    else
                    {
                        // 友方殖民者：实心青蓝色块 (5x5)
                        Fill(new Rect(sx - 2.5f, sy - 2.5f, 5f, 5f), new Color(0.25f, 0.85f, 0.95f, 0.95f));
                    }
                }
                else if (p.Faction == Faction.OfPlayer)
                {
                    // 殖民地动物：实心草绿色块 (4x4)
                    Fill(new Rect(sx - 2f, sy - 2f, 4f, 4f), new Color(0.35f, 0.85f, 0.40f, 0.85f));
                }
                else
                {
                    // 中立人/野生动物：实心浅黄灰色块 (4x4)
                    Fill(new Rect(sx - 2f, sy - 2f, 4f, 4f), new Color(0.80f, 0.80f, 0.50f, 0.70f));
                }
            }

            // 2. 玩家自身指示（金色实心圆点 + 指向视线偏航角的实心尖端）
            float playerSx, playerSy;
            if (isFull)
            {
                playerSx = rect.x + (playerPos2D.x / map.Size.x) * rect.width;
                playerSy = rect.yMax - (playerPos2D.y / map.Size.z) * rect.height;
            }
            else
            {
                // 在跟随缩放模式下，玩家自身严格且永久锚定在小地图正中央
                playerSx = rect.center.x;
                playerSy = rect.center.y;
            }

            float yaw = CameraRig3D.CamYaw;
            Matrix4x4 prevMatrix = GUI.matrix;
            // 采用局部矩阵右乘复合，确保在任意 UIScale 下以 (playerSx, playerSy) 为原点精确原地旋转，
            // 彻底解决 GUIUtility.RotateAroundPivot 因全局 UIScale 缩放失配导致玩家黄点随视角旋转甩飞到小地图外的严重 bug。
            Matrix4x4 localTransform = Matrix4x4.TRS(new Vector3(playerSx, playerSy, 0f), Quaternion.Euler(0f, 0f, yaw), Vector3.one)
                                     * Matrix4x4.TRS(new Vector3(-playerSx, -playerSy, 0f), Quaternion.identity, Vector3.one);
            GUI.matrix = prevMatrix * localTransform;

            DrawPlayerMarker(playerSx, playerSy, 12f, new Color(1.0f, 0.90f, 0.20f, 1f));

            GUI.matrix = prevMatrix;
        }
    }
}
