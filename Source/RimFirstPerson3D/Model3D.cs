// AI生成
using System.Collections.Generic;
using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // 程序化 3D 建模内核
    // ============================================================
    //
    // 本模组不引入外部模型文件（RimWorld 无法在不打 AssetBundle 的前提下加载
    // 外部网格），所有物件都由基本体在运行时拼装：盒子 / 圆柱 / 圆锥 / 楔形 /
    // 球。每个 Part 描述一个基本体在模型空间的位置与尺寸，ModelBuilder 负责
    // 合并顶点、按面类型贴图，并同步产出**描边外壳**。
    //
    // 【描边外壳】
    // 采用「平滑法线外扩壳 + 反绕序」：把每个 Part 的顶点按位置焊接、求平均
    // 法线，再沿法线外扩 outline 宽度，三角形反绕。反绕后正面朝内，背面剔除
    // 会剔掉朝向相机的那一半，只剩物体轮廓之外的部分可见 —— 即描边。
    // 之所以逐 Part 生成（而不是整模型一个壳），是为了让部件交界处也出现
    // 内部描边线，得到赛璐璐风格的拼装感。
    //
    // 【为什么不用自定义 shader】
    // Unity 的自定义 shader 必须编译进 AssetBundle 才能被 mod 加载，而本机
    // 没有 Unity Editor。因此描边在**网格层**完成（法线外扩），视觉效果等价，
    // 且不依赖任何外部资源。

    /// <summary>基本体种类。</summary>
    public enum Prim
    {
        Box,        // 长方体：size = (宽, 高, 深)
        Cylinder,   // 圆柱：size = (直径, 高, 直径)
        Cone,       // 圆锥：size = (底直径, 高, 底直径)
        Wedge,      // 楔形（斜坡）：size = (宽, 高, 深)，高沿 +Z 方向线性升到满高
        Sphere      // 球：size = (直径, 直径, 直径)
    }

    /// <summary>面类型：决定用哪一段 UV 区取样。</summary>
    internal enum FaceKind { Top, Side, Bottom }

    /// <summary>
    /// 贴图取样规格。所有值都在**贴图自身的 0..1 空间**里；
    /// 若材质来自 atlas 子图（mainTextureScale != 1），调用方应设 wholeCell=true，
    /// 此时每个面直接铺满 [0,1]，由材质的 scale/offset 落到 atlas 格内。
    /// </summary>
    public struct UvSpec
    {
        /// <summary>顶面取样区 (u0, v0, u1, v1)。</summary>
        public Vector4 top;
        /// <summary>侧面取样区 (u0, v0, u1, v1)。</summary>
        public Vector4 side;
        /// <summary>侧面纵向一次完整重复对应的世界高度（米）；&lt;=0 表示整段拉伸不重复。</summary>
        public float sideTileH;
        /// <summary>侧面横向一次完整重复对应的世界长度（米）；&lt;=0 表示整段拉伸不重复。</summary>
        public float sideTileW;
        /// <summary>模型整体 XZ 尺寸，用于顶面按包围盒比例取样。</summary>
        public Vector2 modelXZ;

        /// <summary>
        /// 四个方向各自的立面取样区：[0]=北(-Z) [1]=南(+Z) [2]=东(+X) [3]=西(-X)。
        /// 任一元素退化（w&lt;=y）时该面回退用 side —— 家具按"三视图"给每一面配
        /// 各自那张朝向图的立面段，缺该朝向图时回退。
        /// </summary>
        public Vector4[] side4;

        /// <summary>取某方向的立面区（dir: 0北 1南 2东 3西）。</summary>
        public Vector4 SideFor(int dir)
        {
            if (side4 != null && dir >= 0 && dir < 4)
            {
                var r = side4[dir];
                if (r.w > r.y + 1e-6f && r.z > r.x + 1e-6f) return r;
            }
            return side;
        }

        /// <summary>整格 UV（atlas 子图）：每个面铺满 [0,1]。</summary>
        public static UvSpec WholeCell => new UvSpec
        {
            top = new Vector4(0f, 0f, 1f, 1f),
            side = new Vector4(0f, 0f, 1f, 1f),
            sideTileH = 0f,
            sideTileW = 0f,
            modelXZ = Vector2.one
        };

        /// <summary>整张贴图当顶面、中段横带当侧面的通用回退。</summary>
        public static UvSpec Generic(Vector2 modelXZ) => new UvSpec
        {
            top = new Vector4(0f, 0f, 1f, 0.55f),
            side = new Vector4(0f, 0.45f, 1f, 0.62f),
            sideTileH = 0f,
            sideTileW = 0f,
            modelXZ = modelXZ
        };
    }

    /// <summary>模型空间中的一个基本体。位置为模型局部坐标，XZ 以模型中心为原点，Y 以底面为 0。</summary>
    public struct Part
    {
        public Prim kind;
        public Vector3 pos;      // 中心点（Y 为**底面**高度）
        public Vector3 size;     // 见 Prim 注释
        public float yaw;        // 绕 Y 轴旋转（度）
        public float pitch;      // 绕 X 轴旋转（度），用于斜面
        public bool noOutline;   // 参与渲染但不产生描边（如贴地薄片）
        public bool noTop;       // 顶面不贴图（如埋在别的部件下面）
        /// <summary>取样规格下标（ModelBuilder 的 UvSpec 列表）。</summary>
        public int uv;
    }

    /// <summary>一个物件的程序化模型。</summary>
    public class Model3D
    {
        public readonly List<Part> parts = new List<Part>();
        /// <summary>模型整体尺寸（宽, 高, 深），用于顶面 UV 与描边归一化。</summary>
        public Vector3 bounds;

        public Model3D Add(Prim kind, Vector3 pos, Vector3 size,
            float yaw = 0f, float pitch = 0f, bool noOutline = false, int uv = 0)
        {
            parts.Add(new Part
            {
                kind = kind,
                pos = pos,
                size = size,
                yaw = yaw,
                pitch = pitch,
                noOutline = noOutline,
                uv = uv
            });
            return this;
        }

        /// <summary>按已加入的部件推算包围盒（XZ 居中，Y 从 0 起）。</summary>
        public void RecalcBounds()
        {
            if (parts.Count == 0) { bounds = Vector3.one; return; }
            float maxX = 0f, maxZ = 0f, maxY = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                float hw = p.size.x * 0.5f, hd = p.size.z * 0.5f;
                maxX = Mathf.Max(maxX, Mathf.Abs(p.pos.x) + hw);
                maxZ = Mathf.Max(maxZ, Mathf.Abs(p.pos.z) + hd);
                maxY = Mathf.Max(maxY, p.pos.y + p.size.y);
            }
            bounds = new Vector3(maxX * 2f, maxY, maxZ * 2f);
        }
    }

    // ============================================================
    // 网格构建器
    // ============================================================

    /// <summary>
    /// 把若干 Part 合并成一个网格，并同步产出描边外壳网格。
    /// 顶点顺序沿用项目内已验证的约定（顶面从 -x+z 起顺时针），
    /// 保证法线朝外、不被背面剔除。
    /// </summary>
    public class ModelBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> tris = new List<int>();

        // 描边外壳：按 Part 分组累积，保证部件之间不互相焊接。
        private readonly List<Vector3> hullVerts = new List<Vector3>();
        private readonly List<int> hullTris = new List<int>();

        private readonly List<UvSpec> uvList;
        // 当前部件的取样规格；每 Add 一个部件按 part.uv 切换
        private UvSpec uv;
        private readonly bool wholeCell;

        public ModelBuilder(UvSpec uv, bool wholeCellUv)
        {
            this.uvList = new List<UvSpec>(1) { uv };
            this.uv = uv;
            this.wholeCell = wholeCellUv;
        }

        /// <summary>多规格：部件按 Part.uv 下标选用不同的取样区。</summary>
        public ModelBuilder(List<UvSpec> uvs, bool wholeCellUv)
        {
            this.uvList = (uvs != null && uvs.Count > 0) ? uvs : new List<UvSpec> { UvSpec.WholeCell };
            this.uv = this.uvList[0];
            this.wholeCell = wholeCellUv;
        }

        public int TriangleCount => tris.Count / 3;
        public bool IsEmpty => tris.Count == 0;

        public void Add(Part p) => Add(p, Matrix4x4.identity);

        /// <summary>
        /// 把部件加到网格。pre 是「模型空间 -> 分块/世界空间」的变换；
        /// 顶面 UV 仍按**模型空间**归一化，否则旋转/平移后取样区会跑偏。
        /// </summary>
        public void Add(Part p, Matrix4x4 pre)
        {
            uv = uvList[p.uv >= 0 && p.uv < uvList.Count ? p.uv : 0];
            var rot = Quaternion.Euler(p.pitch, p.yaw, 0f);
            var local = Matrix4x4.TRS(p.pos, rot, Vector3.one);
            var matrix = pre * local;
            int hullStart = hullVerts.Count;

            switch (p.kind)
            {
                case Prim.Box: AddBox(matrix, local, p.size, p); break;
                case Prim.Cylinder: AddCylinder(matrix, local, p.size, p, false); break;
                case Prim.Cone: AddCylinder(matrix, local, p.size, p, true); break;
                case Prim.Wedge: AddWedge(matrix, p.size, p); break;
                case Prim.Sphere: AddSphere(matrix, p.size, p); break;
            }

            if (!p.noOutline) EmitHull(hullStart);
            else hullVerts.RemoveRange(hullStart, hullVerts.Count - hullStart);
        }

        public void AddAll(Model3D model)
        {
            if (model == null) return;
            for (int i = 0; i < model.parts.Count; i++) Add(model.parts[i]);
        }

        // ---- 顶点写入 ----

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Matrix4x4 m)
        {
            int i = verts.Count;
            verts.Add(m.MultiplyPoint3x4(a));
            verts.Add(m.MultiplyPoint3x4(b));
            verts.Add(m.MultiplyPoint3x4(c));
            verts.Add(m.MultiplyPoint3x4(d));
            var n = Vector3.Cross(m.MultiplyPoint3x4(b) - m.MultiplyPoint3x4(a),
                                  m.MultiplyPoint3x4(c) - m.MultiplyPoint3x4(a)).normalized;
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            hullVerts.Add(m.MultiplyPoint3x4(a));
            hullVerts.Add(m.MultiplyPoint3x4(b));
            hullVerts.Add(m.MultiplyPoint3x4(c));
            hullVerts.Add(m.MultiplyPoint3x4(d));
        }

        /// <summary>写入一个带平滑法线的顶点（曲面用）。</summary>
        private void Vert(Vector3 v, Vector3 n, Vector2 uv0, Matrix4x4 m)
        {
            verts.Add(m.MultiplyPoint3x4(v));
            normals.Add(m.MultiplyVector(n).normalized);
            uvs.Add(uv0);
        }

        private void Tri(int a, int b, int c)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        // ---- UV 求解 ----

        /// <summary>
        /// 侧面 UV：按世界尺寸在 sideRect 内重复。为了避免依赖贴图的 Repeat
        /// 环绕（atlas/Clamp 贴图环绕会采到邻格），重复通过**细分面**实现：
        /// 沿高/宽各切若干段，每段独立铺满 sideRect。
        /// </summary>
        private void SideQuads(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Matrix4x4 m, int dir = -1)
        {
            var rect = dir >= 0 ? uv.SideFor(dir) : uv.side;
            if (wholeCell)
            {
                Quad(bl, br, tr, tl, new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(0, 1), m);
                return;
            }
            float h = (tr - br).magnitude;
            float w = (br - bl).magnitude;
            int ny = uv.sideTileH > 0.001f ? Mathf.Clamp(Mathf.RoundToInt(h / uv.sideTileH), 1, 64) : 1;
            int nx = uv.sideTileW > 0.001f ? Mathf.Clamp(Mathf.RoundToInt(w / uv.sideTileW), 1, 64) : 1;
            var r = rect;
            for (int iy = 0; iy < ny; iy++)
            {
                float t0 = iy / (float)ny, t1 = (iy + 1) / (float)ny;
                for (int ix = 0; ix < nx; ix++)
                {
                    float s0 = ix / (float)nx, s1 = (ix + 1) / (float)nx;
                    var p0 = Vector3.Lerp(bl, br, s0) + (tl - bl) * t0;
                    var p1 = Vector3.Lerp(bl, br, s1) + (tl - bl) * t0;
                    var p2 = Vector3.Lerp(bl, br, s1) + (tl - bl) * t1;
                    var p3 = Vector3.Lerp(bl, br, s0) + (tl - bl) * t1;
                    Quad(p0, p1, p2, p3,
                        new Vector2(r.x, r.y), new Vector2(r.z, r.y),
                        new Vector2(r.z, r.w), new Vector2(r.x, r.w), m);
                }
            }
        }

        /// <summary>
        /// 顶/底面 UV：把该面在**模型空间** XZ 包围盒中的占比映射进 topRect。
        /// 顶点先过 m 变换到模型空间，否则每个部件都按自身中心归一化，
        /// 拼装后各部件会重复显示同一块贴图区域。
        /// </summary>
        private Vector4 TopRectFor(Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (wholeCell) return new Vector4(0f, 0f, 1f, 1f);
            var r = uv.top;
            float mx = Mathf.Max(0.0001f, uv.modelXZ.x), mz = Mathf.Max(0.0001f, uv.modelXZ.y);
            a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b);
            c = m.MultiplyPoint3x4(c); d = m.MultiplyPoint3x4(d);
            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float minZ = Mathf.Min(Mathf.Min(a.z, b.z), Mathf.Min(c.z, d.z));
            float maxZ = Mathf.Max(Mathf.Max(a.z, b.z), Mathf.Max(c.z, d.z));
            // 模型空间 XZ 归一化到 0..1（模型 XZ 以原点为中心）
            float s0 = Mathf.Clamp01((minX + mx * 0.5f) / mx);
            float s1 = Mathf.Clamp01((maxX + mx * 0.5f) / mx);
            float t0 = Mathf.Clamp01((minZ + mz * 0.5f) / mz);
            float t1 = Mathf.Clamp01((maxZ + mz * 0.5f) / mz);
            return new Vector4(
                Mathf.Lerp(r.x, r.z, s0), Mathf.Lerp(r.y, r.w, t0),
                Mathf.Lerp(r.x, r.z, s1), Mathf.Lerp(r.y, r.w, t1));
        }

        // ---- 基本体 ----

        private void AddBox(Matrix4x4 m, Matrix4x4 local, Vector3 s, Part p)
        {
            float hw = s.x * 0.5f, hd = s.z * 0.5f, h = s.y;
            // 顶面：(-x,+z) -> (+x,+z) -> (+x,-z) -> (-x,-z)（项目内已验证的绕序）
            var ta = new Vector3(-hw, h, hd);
            var tb = new Vector3(hw, h, hd);
            var tc = new Vector3(hw, h, -hd);
            var td = new Vector3(-hw, h, -hd);
            var r = TopRectFor(local, ta, tb, tc, td);
            Quad(ta, tb, tc, td,
                new Vector2(r.x, r.w), new Vector2(r.z, r.w),
                new Vector2(r.z, r.y), new Vector2(r.x, r.y), m);
            // 四个侧面（绕序沿用 BuildingLayer3D.MeshBuilderX 的既有写法）
            // dir: 0北 1南 2东 3西 —— 与 UvSpec.side4 的下标一致
            SideQuads(new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd),
                new Vector3(-hw, h, -hd), new Vector3(hw, h, -hd), m, 0);         // 北 -Z
            SideQuads(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd),
                new Vector3(hw, h, hd), new Vector3(-hw, h, hd), m, 1);           // 南 +Z
            SideQuads(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd),
                new Vector3(-hw, h, hd), new Vector3(-hw, h, -hd), m, 3);         // 西 -X
            SideQuads(new Vector3(hw, 0, hd), new Vector3(hw, 0, -hd),
                new Vector3(hw, h, -hd), new Vector3(hw, h, hd), m, 2);           // 东 +X
        }

        private void AddWedge(Matrix4x4 m, Vector3 s, Part p)
        {
            float hw = s.x * 0.5f, hd = s.z * 0.5f, h = s.y;
            // 斜面：从 -z 端高度 0 升到 +z 端高度 h
            var a = new Vector3(-hw, 0f, -hd);
            var b = new Vector3(hw, 0f, -hd);
            var c = new Vector3(hw, h, hd);
            var d = new Vector3(-hw, h, hd);
            SideQuads(a, b, c, d, m);                                   // 斜面
            SideQuads(new Vector3(hw, 0, hd), new Vector3(-hw, 0, hd),
                new Vector3(-hw, h, hd), new Vector3(hw, h, hd), m);     // 南立面
            // 两侧三角
            Tri3(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(-hw, h, hd), m);
            Tri3(new Vector3(hw, 0, hd), new Vector3(hw, 0, -hd), new Vector3(hw, h, hd), m);
            // 底面
            Quad(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd),
                 new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd),
                 Vector2.zero, Vector2.right, Vector2.one, Vector2.up, m);
        }

        private void Tri3(Vector3 a, Vector3 b, Vector3 c, Matrix4x4 m)
        {
            var va = m.MultiplyPoint3x4(a);
            var vb = m.MultiplyPoint3x4(b);
            var vc = m.MultiplyPoint3x4(c);
            int i = verts.Count;
            verts.Add(va); verts.Add(vb); verts.Add(vc);
            var n = Vector3.Cross(vb - va, vc - va).normalized;
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0.5f, 1));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            hullVerts.Add(va); hullVerts.Add(vb); hullVerts.Add(vc);
        }

        private void AddCylinder(Matrix4x4 m, Matrix4x4 local, Vector3 s, Part p, bool cone)
        {
            const int seg = 12;
            float r = s.x * 0.5f, h = s.y, rz = s.z * 0.5f;
            float topR = cone ? 0.02f * r : r;
            var rSide = wholeCell ? new Vector4(0, 0, 1, 1) : uv.side;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                float u0 = i / (float)seg, u1 = (i + 1) / (float)seg;
                var b0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * rz);
                var b1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * rz);
                var t1 = new Vector3(Mathf.Cos(a1) * topR, h, Mathf.Sin(a1) * rz * (topR / Mathf.Max(0.0001f, r)));
                var t0 = new Vector3(Mathf.Cos(a0) * topR, h, Mathf.Sin(a0) * rz * (topR / Mathf.Max(0.0001f, r)));
                Quad(b0, b1, t1, t0,
                    new Vector2(Mathf.Lerp(rSide.x, rSide.z, u0), rSide.y),
                    new Vector2(Mathf.Lerp(rSide.x, rSide.z, u1), rSide.y),
                    new Vector2(Mathf.Lerp(rSide.x, rSide.z, u1), rSide.w),
                    new Vector2(Mathf.Lerp(rSide.x, rSide.z, u0), rSide.w), m);
            }
            // 顶盖（圆锥时退化为尖点，跳过）
            if (!cone)
            {
                var tr = TopRectFor(local, new Vector3(-r, h, rz), new Vector3(r, h, rz),
                    new Vector3(r, h, -rz), new Vector3(-r, h, -rz));
                int center = verts.Count;
                Vert(new Vector3(0f, h, 0f), Vector3.up, new Vector2((tr.x + tr.z) * 0.5f, (tr.y + tr.w) * 0.5f), m);
                // 顶盖 UV 必须按**该点的 XZ 在取样区里的位置**铺（和盒体顶面同一口径）。
                // 之前沿 i/seg 在对角线上插值 -> 圆面被摊成扇形黑白相间。
                float mx = Mathf.Max(0.0001f, uv.modelXZ.x), mz = Mathf.Max(0.0001f, uv.modelXZ.y);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg * Mathf.PI * 2f;
                    float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                    Vert(new Vector3(Mathf.Cos(a0) * r, h, Mathf.Sin(a0) * rz), Vector3.up, CapUV(a0, r, rz, tr, mx, mz), m);
                    Vert(new Vector3(Mathf.Cos(a1) * r, h, Mathf.Sin(a1) * rz), Vector3.up, CapUV(a1, r, rz, tr, mx, mz), m);
                    Tri(center, center + 1 + i * 2, center + 2 + i * 2);
                    hullVerts.Add(m.MultiplyPoint3x4(new Vector3(0f, h, 0f)));
                    hullVerts.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * r, h, Mathf.Sin(a0) * rz)));
                    hullVerts.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * r, h, Mathf.Sin(a1) * rz)));
                }
            }
        }

        /// <summary>圆柱顶盖某角度的 UV：把 rim 点按模型 XZ 包围盒映射进取样区。</summary>
        private static Vector2 CapUV(float a, float r, float rz, Vector4 tr, float mx, float mz)
        {
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * rz;
            return new Vector2(
                Mathf.Lerp(tr.x, tr.z, Mathf.Clamp01((x + mx * 0.5f) / mx)),
                Mathf.Lerp(tr.y, tr.w, Mathf.Clamp01((z + mz * 0.5f) / mz)));
        }

        private void AddSphere(Matrix4x4 m, Vector3 s, Part p)
        {
            const int lon = 12, lat = 8;
            float rx = s.x * 0.5f, ry = s.y * 0.5f, rz = s.z * 0.5f;
            var rTop = wholeCell ? new Vector4(0, 0, 1, 1) : uv.top;
            int start = verts.Count;
            for (int y = 0; y <= lat; y++)
            {
                float phi = y / (float)lat * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float theta = x / (float)lon * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    var pos = new Vector3(n.x * rx, ry + n.y * ry, n.z * rz);
                    Vert(pos, n, new Vector2(
                        Mathf.Lerp(rTop.x, rTop.z, x / (float)lon),
                        Mathf.Lerp(rTop.y, rTop.w, y / (float)lat)), m);
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = start + y * (lon + 1) + x, b = a + lon + 1;
                    Tri(a, a + 1, b);
                    Tri(a + 1, b + 1, b);
                    hullVerts.Add(verts[a]); hullVerts.Add(verts[a + 1]); hullVerts.Add(verts[b]);
                    hullVerts.Add(verts[a + 1]); hullVerts.Add(verts[b + 1]); hullVerts.Add(verts[b]);
                }
        }

        // ---- 描边外壳 ----

        /// <summary>
        /// 把 [start, end) 区间的外壳顶点按位置焊接、求平均法线，外扩成描边壳。
        /// 焊接是为了让盒体的 8 个角得到**平均法线**（向外斜 45°），
        /// 外扩后才是一个均匀放大的圆角盒，而不是沿三轴各自平移（那会让薄件变厚）。
        /// </summary>
        private void EmitHull(int start)
        {
            int count = hullVerts.Count - start;
            if (count < 3) return;

            var map = new Dictionary<long, int>(count);
            var uniq = new List<Vector3>(count);
            var uniqKeys = new List<long>(count);
            var localOf = new List<int>(count);   // 每个外壳顶点 -> 其唯一顶点下标
            for (int i = start; i < hullVerts.Count; i++)
            {
                long k = Key(hullVerts[i]);
                if (!map.TryGetValue(k, out int idx))
                {
                    idx = uniq.Count;
                    map[k] = idx;
                    uniq.Add(hullVerts[i]);
                    uniqKeys.Add(k);
                }
                localOf.Add(idx);
            }

            // 面积加权平均法线：叉积长度正比于三角形面积，累加即得加权和。
            var acc = new Vector3[uniq.Count];
            for (int t = 0; t + 2 < hullVerts.Count; t += 3)
            {
                var a = hullVerts[t]; var b = hullVerts[t + 1]; var c = hullVerts[t + 2];
                var n = Vector3.Cross(b - a, c - a);
                acc[localOf[t - start]] += n;
                acc[localOf[t - start + 1]] += n;
                acc[localOf[t - start + 2]] += n;
            }

            int baseIdx = hullUnique.Count;
            for (int i = 0; i < uniq.Count; i++)
            {
                var n = acc[i];
                hullUnique.Add(uniq[i]);
                hullUniqueNormals.Add(n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up);
            }
            for (int t = 0; t + 2 < hullVerts.Count; t += 3)
            {
                // 反绕序：正面朝内，背面剔除后只剩轮廓外圈可见 —— 即描边。
                hullTris.Add(baseIdx + localOf[t - start + 2]);
                hullTris.Add(baseIdx + localOf[t - start + 1]);
                hullTris.Add(baseIdx + localOf[t - start]);
            }
        }

        private readonly List<Vector3> hullUnique = new List<Vector3>();
        private readonly List<Vector3> hullUniqueNormals = new List<Vector3>();

        private static long Key(Vector3 v)
        {
            // 1/2048 格（约 0.5mm）量化，足以合并同一部件的重复顶点。
            long x = Mathf.RoundToInt(v.x * 2048f) + 1048576L;
            long y = Mathf.RoundToInt(v.y * 2048f) + 1048576L;
            long z = Mathf.RoundToInt(v.z * 2048f) + 1048576L;
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        public Mesh ToMesh()
        {
            if (tris.Count == 0) return null;
            var mesh = new Mesh { name = "Fps3D_Model" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>产出描边外壳网格：唯一顶点沿平均法线外扩 width。</summary>
        public Mesh ToHull(float width)
        {
            if (hullTris.Count == 0) return null;
            var v = new List<Vector3>(hullUnique.Count);
            for (int i = 0; i < hullUnique.Count; i++)
                v.Add(hullUnique[i] + hullUniqueNormals[i] * width);
            var mesh = new Mesh { name = "Fps3D_Outline" };
            mesh.SetVertices(v);
            mesh.SetTriangles(hullTris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
