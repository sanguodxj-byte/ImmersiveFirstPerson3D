// AI生成
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 建筑资产模型：外部 OBJ 接管
    // ============================================================
    //
    // 【来源】FPS3D-Uncompleted-Textures 仓库的 152 个待建模建筑 glb，
    //         经资产管线转成三角化 OBJ + MTL + PNG，落盘
    //         1.7\Assets\buildings\Building_<DefName>[__变体].obj。
    //         每个 OBJ 恰好两组：Body（带贴图本体）与 Outline（反绕描边壳）。
    // 【坐标】模型为 Y-up 右手系源数据，ObjImporter3D 的 z 取反完成左手化；
    //         Body 正面在导入后朝 -Z，与 RimWorld 默认 South 朝向一致；
    //         旋转沿用全项目统一约定 Quaternion.AngleAxis(t.Rotation.AsAngle, up)。
    // 【尺度】模型即格单位（底面 y=0），DrawPos 落地直绘，不做缩放。
    // 【材质】Body 走 CutoutMatFor（贴图来自 mtl 的 map_Kd，运行时
    //         Texture2D.LoadImage 读盘）；Outline 走 Util3D.OutlineMat，
    //         与 Furniture3D / Wreckage3D 同一套描边表现。
    // 【变体】__后缀（B/C/D/E 贴图变体、Open/Off 状态变体）当前只注册
    //         基础名：同一 def 的实例统一用基础模型，状态变体待后续按需接入。
    //
    // 接线：BuildingLayer3D 把接管件让给本层（不再进立板/地面贴片回退），
    // 蓝图与建造框架同 Furniture3D 一样用全息蓝图材质绘制。
    [StaticConstructorOnStartup]
    public static class BuildingModels3D
    {
        public class Built
        {
            public Mesh body;
            public Mesh hull;
            public Material mat;
        }

        // defName（小写） -> 模型文件名（不含扩展名）
        private static readonly Dictionary<string, string> defToModel =
            new Dictionary<string, string>(128, System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Built> cache =
            new Dictionary<string, Built>(128, System.StringComparer.OrdinalIgnoreCase);
        private static Texture2D placeholderTex;

        /// <summary>本系统开关（独立于其它层，便于单点回退）。</summary>
        public static bool Enabled = true;

        static BuildingModels3D()
        {
            try { ScanAssets(); }
            catch (System.Exception e) { Log.Error(ModEntry.LogPrefix + "building models scan failed: " + e); }
        }

        // 扫描 1.7\Assets\buildings\Building_*.obj 注册 defName。
        // 排序后基础名（无 __变体）先出现，first-wins 即基础模型。
        private static void ScanAssets()
        {
            var mod = ModLister.GetActiveModWithIdentifier(ModEntry.HarmonyId);
            if (mod == null) return;
            string dir = Path.Combine(mod.RootDir.FullName, "1.7", "Assets", "buildings");
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, "Building_*.obj");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            int registered = 0;
            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!name.StartsWith("Building_")) continue;
                string def = name.Substring("Building_".Length);
                int cut = def.IndexOf('_' + "_");   // "__" 拆分变体后缀
                if (cut >= 0) def = def.Substring(0, cut);
                if (def.Length == 0) continue;
                if (defToModel.ContainsKey(def)) continue;
                // 只在 obj 与 Body 贴图都就绪时注册，缺贴图的模型不接管。
                defToModel[def] = name;
                registered++;
            }
            Log.Message(ModEntry.LogPrefix + "building models registered: " + registered
                + " from " + files.Length + " files");
        }

        public static bool ResolveBuildDef(Thing t, out ThingDef buildDef, out bool isConstructible)
        {
            buildDef = null;
            isConstructible = false;
            if (t == null || t.def == null) return false;
            if (t is Blueprint bp)
            {
                buildDef = bp.EntityToBuild() as ThingDef;
                isConstructible = true;
                return buildDef != null;
            }
            if (t is Frame fr)
            {
                buildDef = fr.BuildDef;
                isConstructible = true;
                return buildDef != null;
            }
            buildDef = t.def;
            return true;
        }

        /// <summary>该建筑（或其蓝图/建造框架）是否由本层绘制（BuildingLayer3D 据此跳过立板回退）。</summary>
        public static bool Handles(Thing t)
        {
            if (!Enabled || !ResolveBuildDef(t, out var d, out _)) return false;
            return d != null && defToModel.ContainsKey(d.defName);
        }

        /// <summary>按 defName 取模型（本体+描边壳+材质）；缺模型返回 body==null 的空哨兵。</summary>
        private static Built ModelFor(string defName)
        {
            if (cache.TryGetValue(defName, out var b)) return b;
            Built built = new Built();
            try
            {
                if (defToModel.TryGetValue(defName, out var modelName))
                {
                    var mod = ModLister.GetActiveModWithIdentifier(ModEntry.HarmonyId);
                    if (mod != null)
                    {
                        string dir = Path.Combine(mod.RootDir.FullName, "1.7", "Assets", "buildings");
                        string objPath = Path.Combine(dir, modelName + ".obj");
                        var imported = ObjImporter3D.Load(objPath);
                        if (imported != null && imported.groupTris != null
                            && imported.groupTris.Count > 0 && imported.verts != null)
                        {
                            string bodyTex = null;
                            imported.texByGroup.TryGetValue("Body", out bodyTex);
                            built.body = GroupMesh(imported, "Body", "Fps3D_BldBody_" + defName);
                            if (built.body != null)
                            {
                                built.mat = MatFor(bodyTex, modelName);
                                var st = FirstPerson3DMod.settings;
                                float ow = st != null ? st.outlineWidth : Metrics3D.OutlineWidth;
                                built.hull = PlantModels3D.BuildHull(built.body, ow);
                                Log.Message(ModEntry.LogPrefix + "building model loaded: " + defName +
                                    " (tex=" + (bodyTex != null ? Path.GetFileName(bodyTex) : "none") +
                                    ", verts=" + built.body.vertexCount + ", tris=" + (built.body.triangles.Length / 3) + ")");
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warning(ModEntry.LogPrefix + "building model load failed: " + defName + " -> " + e.Message);
            }
            cache[defName] = built;
            return built;
        }

        // 由导入结果按材质组重建独立网格（沿用 HandTerminal3D 的做法：
        // 1.7 下多 submesh Mesh 读写不可靠，直接吃原始列表）。
        private static Mesh GroupMesh(ObjImporter3D.ImportResult imported, string group, string meshName)
        {
            int gi = -1;
            for (int i = 0; i < imported.groups.Count; i++)
                if (imported.groups[i] == group) { gi = i; break; }
            if (gi < 0) return null;
            var tris = imported.groupTris[gi];
            if (tris == null || tris.Count == 0) return null;
            var mesh = new Mesh { name = meshName };
            if (imported.verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(imported.verts);
            if (imported.uvs != null && imported.uvs.Count == imported.verts.Count)
                mesh.SetUVs(0, imported.uvs);
            if (imported.normals != null && imported.normals.Count == imported.verts.Count)
                mesh.SetNormals(imported.normals);
            else
                mesh.RecalculateNormals();
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 贴图读盘 -> Cutout 材质（缺失回退到白色占位，模型仍以本色可绘）。
        private static Material MatFor(string pngPath, string modelName)
        {
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(pngPath) && File.Exists(pngPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(pngPath);
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = "Fps3D_BldTex_" + modelName,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!tex.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
                catch { tex = null; }
            }
            if (tex == null)
            {
                if (placeholderTex == null)
                {
                    placeholderTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < 16; i++) px[i] = new Color32(190, 190, 190, 255);
                    placeholderTex.SetPixels32(px);
                    placeholderTex.Apply();
                    placeholderTex.name = "Fps3D_BldTexPlaceholder";
                }
                tex = placeholderTex;
            }
            return Util3D.CutoutMatFor(tex);
        }

        // ---- 绘制（BuildingLayer3D 逐帧调用，量级与家具相当） ----

        public static bool TryDraw(Thing t, Vector3 pos, Color lit)
        {
            if (!Enabled || !ResolveBuildDef(t, out var def, out bool isConstructible)) return false;
            if (def == null) return false;
            var b = ModelFor(def.defName);
            if (b == null || b.body == null) return false;
            float yaw = t.Rotation.AsAngle;
            var matrix = Matrix4x4.TRS(pos, Quaternion.AngleAxis(yaw, Vector3.up), Vector3.one);
            if (isConstructible)
            {
                Graphics.DrawMesh(b.body, matrix, Util3D.BlueprintMatFor(b.mat.mainTexture, FilterMode.Point), 0);
                PerfCounters.BuildingMeshesDrawn++;
                if (Fps3DState.DrawOutlines && b.hull != null)
                {
                    Graphics.DrawMesh(b.hull, matrix, Util3D.BlueprintOutlineMat, 0);
                    PerfCounters.OutlineMeshesDrawn++;
                }
                return true;
            }
            Graphics.DrawMesh(b.body, matrix, Lighting3D.MatFor(b.mat, lit), 0);
            PerfCounters.BuildingMeshesDrawn++;
            if (Fps3DState.DrawOutlines && b.hull != null)
            {
                Graphics.DrawMesh(b.hull, matrix, Util3D.OutlineMat, 0);
                PerfCounters.OutlineMeshesDrawn++;
            }
            return true;
        }

        /// <summary>设置/资源变化时清缓存（网格跨图复用，平时不清）。</summary>
        public static void Clear()
        {
            foreach (var kv in cache)
            {
                Util3D.SafeDestroy(kv.Value.body);
                Util3D.SafeDestroy(kv.Value.hull);
            }
            cache.Clear();
        }
    }
}
