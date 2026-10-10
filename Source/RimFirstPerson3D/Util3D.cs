// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 面向本模组的网格与材质工具。
    public static class Util3D
    {
        public static bool VerboseLog = false;

        // ---- 共享网格 ----

        private static Mesh billboardQuad;
        // 立式四边形：宽 1（x -0.5..0.5），高 1（y 0..1），底边对齐 y=0，面向 -Z。
        public static Mesh BillboardQuad
        {
            get
            {
                if (billboardQuad != null) return billboardQuad;
                billboardQuad = BuildQuad(new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                                          new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f));
                billboardQuad.name = "Fps3D_Billboard";
                return billboardQuad;
            }
        }

        private static Mesh crossMesh;

        private static Mesh groundQuad;
        // 平铺地面四边形：中心在原点，1x1，法线向上（v 向北递增）。
        public static Mesh GroundQuad
        {
            get
            {
                if (groundQuad != null) return groundQuad;
                groundQuad = BuildQuad(
                    new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, -0.5f));
                groundQuad.name = "Fps3D_GroundQuad";
                return groundQuad;
            }
        }

        // 十字交叉面片：两张对角立式四边形，单位尺寸；每面正反双份（背面剔除下两侧都可见）。
        public static Mesh CrossMesh
        {
            get
            {
                if (crossMesh != null) return crossMesh;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                var a = new Vector3(1f, 0f, 1f).normalized;
                var b = new Vector3(1f, 0f, -1f).normalized;
                AppendQuad(verts, uvs, tris, -a * 0.5f, a * 0.5f, a * 0.5f + Vector3.up, -a * 0.5f + Vector3.up);
                AppendQuad(verts, uvs, tris, a * 0.5f, -a * 0.5f, -a * 0.5f + Vector3.up, a * 0.5f + Vector3.up);
                AppendQuad(verts, uvs, tris, -b * 0.5f, b * 0.5f, b * 0.5f + Vector3.up, -b * 0.5f + Vector3.up);
                AppendQuad(verts, uvs, tris, b * 0.5f, -b * 0.5f, -b * 0.5f + Vector3.up, b * 0.5f + Vector3.up);
                crossMesh = new Mesh();
                crossMesh.SetVertices(verts);
                crossMesh.SetUVs(0, uvs);
                crossMesh.SetTriangles(tris, 0);
                crossMesh.RecalculateNormals();
                crossMesh.RecalculateBounds();
                crossMesh.name = "Fps3D_Cross";
                return crossMesh;
            }
        }

        // ---- 材质缓存 ----

        private static Texture2D whiteTex;
        public static Texture2D WhiteTex
        {
            get
            {
                if (whiteTex != null) return whiteTex;
                whiteTex = new Texture2D(2, 2);
                var px = new Color32[] { Color.white, Color.white, Color.white, Color.white };
                whiteTex.SetPixels32(px);
                whiteTex.Apply();
                return whiteTex;
            }
        }

        private static Material outlineMat;
        public static Material OutlineMat
        {
            get
            {
                if (outlineMat != null) return outlineMat;
                var m = new Material(ShaderDatabase.Cutout);
                m.mainTexture = WhiteTex;
                m.color = new Color(0.04f, 0.03f, 0.05f);
                outlineMat = m;
                return outlineMat;
            }
        }

        // 实例化绘制专用材质缓存：克隆原版材质并开启 instancing（原版材质默认未开）。
        private static readonly Dictionary<Material, Material> instancedMats = new Dictionary<Material, Material>();
        public static Material InstancedMatFor(Material src)
        {
            if (src == null) return null;
            if (instancedMats.TryGetValue(src, out var m)) return m;
            m = new Material(src) { enableInstancing = true };
            instancedMats[src] = m;
            return m;
        }

        // ---- 原版"植物随风摇摆"着色器 ----
        // 摇摆是 GPU 顶点动画（RimWorld 自带的 materials/map/plantwindsway 着色器，
        // 由地图 WindManager 每帧驱动），与提交方式无关——用 Graphics.DrawMesh 画同样会摇。
        // 因此植被只要用这个着色器就自动摇摆，零 CPU 开销，且与合并网格方案兼容。
        private static bool swayShaderResolved;
        private static Shader swayShader;
        public static Shader PlantSwayShader
        {
            get
            {
                if (swayShaderResolved) return swayShader;
                swayShaderResolved = true;
                try { swayShader = Shader.Find("materials/map/plantwindsway"); } catch { }
                if (swayShader == null) { try { swayShader = ShaderDatabase.CutoutPlant; } catch { } }
                if (swayShader == null) { try { swayShader = ShaderDatabase.Cutout; } catch { } }
                return swayShader;
            }
        }

        // 用摇摆着色器派生植物材质：保留源材质的贴图与色调；源为空时返回白底摇摆材质。
        public static Material NewPlantMat(Material src)
        {
            var sh = PlantSwayShader;
            Material m = sh != null ? new Material(sh) : (src != null ? new Material(src) : null);
            if (m == null) return null;
            if (src != null)
            {
                if (src.mainTexture != null) m.mainTexture = src.mainTexture;
                m.mainTextureScale = src.mainTextureScale;
                m.mainTextureOffset = src.mainTextureOffset;
                m.color = src.color;
                m.renderQueue = src.renderQueue;
            }
            return m;
        }

        private static Mesh starMesh;
        // 三片 60° 交叉立式面片（0°/60°/120°），单位尺寸，每片正反双份：体积感优于十字片，且与视角无关。
        public static Mesh StarMesh
        {
            get
            {
                if (starMesh != null) return starMesh;
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 60f * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
                    AppendQuad(verts, uvs, tris, -d, d, d + Vector3.up, -d + Vector3.up);
                    AppendQuad(verts, uvs, tris, d, -d, -d + Vector3.up, d + Vector3.up);
                }
                starMesh = new Mesh { name = "Fps3D_Star" };
                starMesh.SetVertices(verts);
                starMesh.SetUVs(0, uvs);
                starMesh.SetTriangles(tris, 0);
                starMesh.RecalculateNormals();
                starMesh.RecalculateBounds();
                return starMesh;
            }
        }

        // ---- 软件实例化：把源网格按一组矩阵逐顶点烘焙成单个合并网格 ----
        // 为何不用 Graphics.DrawMeshInstanced：RimWorld 的原版着色器不含 instancing
        // 变体，实例化绘制会**静默**不产生任何像素（不报错、不报警），因而植物/草/物品
        // 全部不可见。这里改为把实例烘焙成普通网格，走与地形/建筑相同的
        // Graphics.DrawMesh 路径（已证明可正常出图）。
        private static readonly Dictionary<Mesh, Vector3[]> srcVerts = new Dictionary<Mesh, Vector3[]>();
        private static readonly Dictionary<Mesh, Vector2[]> srcUVs = new Dictionary<Mesh, Vector2[]>();
        private static readonly Dictionary<Mesh, int[]> srcTris = new Dictionary<Mesh, int[]>();

        public static Mesh BuildInstancedMesh(Mesh src, List<Matrix4x4> mats)
        {
            if (src == null || mats == null || mats.Count == 0) return null;
            if (!srcVerts.TryGetValue(src, out var sv))
            {
                sv = src.vertices; srcVerts[src] = sv;
                srcUVs[src] = src.uv; srcTris[src] = src.triangles;
            }
            var su = srcUVs[src];
            var st = srcTris[src];
            int vn = sv.Length, tn = st.Length, n = mats.Count;
            var verts = new Vector3[vn * n];
            var uvs = new Vector2[vn * n];
            var tris = new int[tn * n];
            int vi = 0, ti = 0;
            for (int k = 0; k < n; k++)
            {
                var m = mats[k];
                int b = vi;
                for (int i = 0; i < vn; i++) { verts[vi] = m.MultiplyPoint3x4(sv[i]); uvs[vi] = su[i]; vi++; }
                for (int i = 0; i < tn; i++) tris[ti++] = b + st[i];
            }
            var mesh = new Mesh { name = src.name + "_merged" };
            // >65535 顶点必须用 32 位索引，否则顶点被截断。
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static readonly Dictionary<Texture, Material> cutoutMats = new Dictionary<Texture, Material>();

        // 由任意贴图生成 Cutout 立牌材质（无色调，白底），带缓存。
        public static Material CutoutMatFor(Texture tex)
        {
            if (tex == null) return null;
            if (cutoutMats.TryGetValue(tex, out var mat)) return mat;
            mat = new Material(ShaderDatabase.Cutout);
            mat.mainTexture = tex;
            mat.color = Color.white;
            cutoutMats[tex] = mat;
            return mat;
        }

        // ---- 贴图可视内容竖直范围（立片底对齐用）----
        // 原版植物贴图是俯视(XZ)方片，内容常四周留透明边。3D 化把它立起来当高度后，
        // 底部留白就表现为"离地悬空"。这里按**材质子图 uv 区域**（scale/offset，兼容原版
        // atlas 打包）扫描 alpha，返回不透明像素在该子区域内的归一化 v 范围（0=底、1=顶），
        // 供材质 UV 重映射做底对齐。贴图不可读时返回满幅（即不改动，保持原样）。
        private struct VisibleKey
        {
            public int id, ox, oy, sx, sy;
            public override int GetHashCode() { return id * 31 + ox * 7 + oy * 13 + sx * 17 + sy * 19; }
            public override bool Equals(object obj)
            {
                var k = (VisibleKey)obj;
                return id == k.id && ox == k.ox && oy == k.oy && sx == k.sx && sy == k.sy;
            }
        }

        private static readonly Dictionary<VisibleKey, Vector2> visibleVCache = new Dictionary<VisibleKey, Vector2>();

        public static Vector2 VisibleVRange(Texture2D tex, Vector2 scale, Vector2 offset)
        {
            if (tex == null) return new Vector2(0f, 1f);
            if (scale.x <= 0.0001f) scale.x = 1f;
            if (scale.y <= 0.0001f) scale.y = 1f;
            var key = new VisibleKey
            {
                id = tex.GetInstanceID(),
                ox = Mathf.RoundToInt(offset.x * 4096f),
                oy = Mathf.RoundToInt(offset.y * 4096f),
                sx = Mathf.RoundToInt(scale.x * 4096f),
                sy = Mathf.RoundToInt(scale.y * 4096f)
            };
            if (visibleVCache.TryGetValue(key, out var cached)) return cached;
            var res = new Vector2(0f, 1f);
            try
            {
                if (tex.isReadable)
                {
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(offset.x * tex.width), 0, tex.width - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(offset.y * tex.height), 0, tex.height - 1);
                    int w = Mathf.Clamp(Mathf.CeilToInt(scale.x * tex.width), 1, tex.width - x0);
                    int h = Mathf.Clamp(Mathf.CeilToInt(scale.y * tex.height), 1, tex.height - y0);
                    var px = tex.GetPixels(x0, y0, w, h);   // 行 0 = 子图底部（v 从底到顶）
                    int lo = -1, hi = -1;
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * w;
                        bool solid = false;
                        for (int x = 0; x < w; x++) { if (px[row + x].a > 24) { solid = true; break; } }
                        if (solid) { if (lo < 0) lo = y; hi = y; }
                    }
                    if (lo >= 0 && hi >= lo) res = new Vector2((lo + 0.5f) / h, (hi + 0.5f) / h);
                }
            }
            catch { res = new Vector2(0f, 1f); }
            visibleVCache[key] = res;
            return res;
        }

        // 克隆原版材质（保留贴图、atlas 子图 UV 与色调），用于建筑盒体贴图平铺。
        private static readonly Dictionary<Material, Material> clonedMats = new Dictionary<Material, Material>();
        public static Material CloneForBox(Material src)
        {
            if (src == null) return null;
            if (clonedMats.TryGetValue(src, out var m)) return m;
            m = new Material(src);
            // Graphic_Linked 的 MatAt 返回 atlas 子材质。不要清掉其 scale/offset，
            // 否则墙体会采样整张连接图而不是当前连接形状。
            m.mainTextureScale = src.mainTextureScale;
            m.mainTextureOffset = src.mainTextureOffset;
            clonedMats[src] = m;
            return m;
        }

        // ---- 网格构建 ----

        public static Mesh BuildQuad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            AppendQuad(verts, uvs, tris, bl, br, tr, tl);
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void AppendQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl)
        {
            int baseIdx = verts.Count;
            verts.Add(bl); verts.Add(br); verts.Add(tr); verts.Add(tl);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
            // 绕序：Unity 正面为顺时针（从观察侧）。原 (0,1,2)(0,2,3) 朝向反，
            // 曾导致立牌/十字面片/地面贴片全部被背面剔除（r14 修复）。
            tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
            tris.Add(baseIdx); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
        }

        // 盒体网格：底面中心在原点，尺寸 (w, h, d)。侧面 UV 按格宽平铺，顶面按 (w, d)。
        public static Mesh BuildBox(float w, float h, float d, float vScale)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float hw = w * 0.5f, hd = d * 0.5f;

            // 顶面（法线向上）
            AddQuad(v => v,
                new Vector3(-hw, h, hd), new Vector3(hw, h, hd), new Vector3(hw, h, -hd), new Vector3(-hw, h, -hd),
                new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, d), new Vector2(0, d));

            // 四个侧面：u = 沿面宽（格），v = 0..h*vScale
            float v0 = 0f, v1 = h * vScale;
            // 北面 (-Z)
            AddQuad(v => v,
                new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd), new Vector3(-hw, h, -hd), new Vector3(hw, h, -hd),
                new Vector2(0, v0), new Vector2(w, v0), new Vector2(w, v1), new Vector2(0, v1));
            // 南面 (+Z)
            AddQuad(v => v,
                new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd), new Vector3(hw, h, hd), new Vector3(-hw, h, hd),
                new Vector2(0, v0), new Vector2(w, v0), new Vector2(w, v1), new Vector2(0, v1));
            // 西面 (-X)
            AddQuad(v => v,
                new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(-hw, h, hd), new Vector3(-hw, h, -hd),
                new Vector2(0, v0), new Vector2(d, v0), new Vector2(d, v1), new Vector2(0, v1));
            // 东面 (+X)
            AddQuad(v => v,
                new Vector3(hw, 0, hd), new Vector3(hw, 0, -hd), new Vector3(hw, h, -hd), new Vector3(hw, h, hd),
                new Vector2(0, v0), new Vector2(d, v0), new Vector2(d, v1), new Vector2(0, v1));

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            void AddQuad(System.Func<Vector3, Vector3> _, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
                Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL)
            {
                int baseIdx = verts.Count;
                verts.Add(bl); verts.Add(br); verts.Add(tr); verts.Add(tl);
                uvs.Add(uvBL); uvs.Add(uvBR); uvs.Add(uvTR); uvs.Add(uvTL);
                tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
            }
        }

        // 反绕盒体（描边外壳）：法线翻转的膨胀盒，配合普通 Cutout 材质呈现剪影描边。
        public static Mesh BuildBoxHullInverted(float w, float h, float d, float inflate)
        {
            var mesh = BuildBox(w + inflate * 2f, h + inflate, d + inflate * 2f, 1f);
            var tris = mesh.GetTriangles(0);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int t = tris[i];
                tris[i] = tris[i + 2];
                tris[i + 2] = t;
            }
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void SafeDestroy(Mesh m)
        {
            if (m != null) Object.Destroy(m);
        }

        public static int HashCell(IntVec3 c)
        {
            return c.x * 73856093 ^ c.z * 19349663;
        }
    }

    // 全局绘制计数，性能面板直接读取。
    public static class PerfCounters
    {
        public static int TerrainMeshesDrawn;
        public static int BuildingMeshesDrawn;
        public static int TerrainQuads;
        public static int BuildingQuads;
        public static int PlantInstances;
        public static int PlantBatches;
        public static int ItemInstances;
        public static int ItemBatches;
        public static int PawnQuads;
        public static int OutlineMeshesDrawn;
        public static int WaterQuads;
        public static int GrassInstances;
        public static int AnimalMeshes;
        public static float LastTerrainBuildMs;
        public static float LastBuildingBuildMs;
        public static float LastPlantBuildMs;

        public static void ResetFrame()
        {
            TerrainMeshesDrawn = 0;
            BuildingMeshesDrawn = 0;
            TerrainQuads = 0;
            BuildingQuads = 0;
            PlantInstances = 0;
            PlantBatches = 0;
            ItemInstances = 0;
            ItemBatches = 0;
            PawnQuads = 0;
            OutlineMeshesDrawn = 0;
            WaterQuads = 0;
            GrassInstances = 0;
            AnimalMeshes = 0;
        }
    }
}
