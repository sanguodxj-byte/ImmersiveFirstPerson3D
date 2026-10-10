// AI生成
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 轻量 OBJ 导入器：解析 Blender 导出的三角化 OBJ（v/vt/vn/f/usemtl），
    // 按材质组拆 submesh。顶点按 (位置,uv,法线) 三元组唯一化，
    // 法线沿用文件值（保留倒角平滑），顶点色留给调用方烘焙明暗。
    public static class ObjImporter3D
    {
        public class ImportResult
        {
            public Mesh mesh;
            public List<string> groups = new List<string>(); // 与 submesh 一一对应的 usemtl 名
        }

        public static ImportResult Load(string path)
        {
            if (!File.Exists(path)) return null;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var positions = new List<Vector3>(1400);
            var uvs = new List<Vector2>(1400);
            var normals = new List<Vector3>(1400);

            var verts = new List<Vector3>();
            var vertUvs = new List<Vector2>();
            var vertNormals = new List<Vector3>();
            var indexMap = new Dictionary<long, int>(8192);
            var groupTris = new List<KeyValuePair<string, List<int>>>();
            List<int> cur = null;
            bool skipObj = false;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                var key = line.Substring(0, sp);
                var body = line.Substring(sp + 1).Trim();

                if (key == "v")
                {
                    if (TryVec3(body, out var p)) positions.Add(p);
                }
                else if (key == "vt")
                {
                    if (TryVec2(body, out var uv)) uvs.Add(uv);
                }
                else if (key == "vn")
                {
                    if (TryVec3(body, out var n)) normals.Add(n.normalized);
                }
                else if (key == "o")
                {
                    // 左右握把是模型底部两块黑色手柄，指挥面板不画。
                    skipObj = body == "grip_l" || body == "grip_r";
                    if (skipObj) cur = null;
                }
                else if (key == "f")
                {
                    if (skipObj) continue;
                    if (cur == null)
                    {
                        cur = new List<int>(512);
                        groupTris.Add(new KeyValuePair<string, List<int>>("", cur));
                    }
                    // 三角扇：首顶点 + 相邻两顶点。绕序反转：OBJ 右手系 CCW 为正面，
                    // Unity 左手系需 CW，直接沿用会整模被背面剔除（实机踩坑：终端完全不可见）。
                    int fanA = -1, fanB = -1;
                    int seg = 0;
                    foreach (var tok in body.Split(' '))
                    {
                        int vi = VertexIndex(tok, positions.Count, uvs.Count, normals.Count,
                            positions, uvs, normals, verts, vertUvs, vertNormals, indexMap);
                        if (vi < 0) continue;
                        if (seg == 0) { fanA = vi; }
                        else if (seg == 1) { fanB = vi; }
                        else
                        {
                            cur.Add(fanA);
                            cur.Add(vi);
                            cur.Add(fanB);
                            fanB = vi;
                        }
                        seg++;
                    }
                }
                else if (key == "usemtl")
                {
                    if (skipObj) { cur = null; continue; }
                    cur = new List<int>(512);
                    groupTris.Add(new KeyValuePair<string, List<int>>(body, cur));
                }
            }

            if (verts.Count == 0 || groupTris.Count == 0) return null;

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, vertUvs);
            mesh.SetNormals(vertNormals);
            mesh.subMeshCount = groupTris.Count;
            for (int i = 0; i < groupTris.Count; i++)
                mesh.SetTriangles(groupTris[i].Value, i);
            mesh.RecalculateBounds();

            var res = new ImportResult { mesh = mesh };
            foreach (var g in groupTris) res.groups.Add(g.Key);
            Log.Message(ModEntry.LogPrefix + "obj imported: " + verts.Count + "v/"
                + groupTris.Count + "grp " + watch.ElapsedMilliseconds + "ms (" + path + ")");
            return res;
        }

        // f 令牌解析并按需登记合并顶点；负索引为相对末尾（OBJ 规范）。
        private static int VertexIndex(string tok, int posCount, int uvCount, int normCount,
            List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals,
            List<Vector3> verts, List<Vector2> vertUvs, List<Vector3> vertNormals,
            Dictionary<long, int> indexMap)
        {
            int a = tok.IndexOf('/');
            int b = tok.IndexOf('/', a + 1);
            int vi, ti = 0, ni = 0;
            if (a < 0)
            {
                if (!int.TryParse(tok, out vi)) return -1;
            }
            else
            {
                if (!int.TryParse(tok.Substring(0, a), out vi)) return -1;
                string mid = b > a + 1 ? tok.Substring(a + 1, b - a - 1) : "";
                if (mid.Length > 0 && !int.TryParse(mid, out ti)) ti = 0;
                if (b >= 0)
                {
                    string tail = tok.Substring(b + 1);
                    if (tail.Length > 0 && !int.TryParse(tail, out ni)) ni = 0;
                }
            }
            if (vi < 0) vi = posCount + vi + 1; else vi -= 1;
            if (vi < 0 || vi >= posCount) return -1;
            if (ti < 0) ti = uvCount + ti + 1; else ti -= 1;
            if (ti < -1 || ti >= uvCount) ti = -1;
            if (ni < 0) ni = normCount + ni + 1; else ni -= 1;
            if (ni < -1 || ni >= normCount) ni = -1;

            long key = ((long)vi << 42) | ((long)(ti + 1) << 21) | (long)(ni + 1);
            if (indexMap.TryGetValue(key, out int cached)) return cached;

            int idx = verts.Count;
            verts.Add(positions[vi]);
            vertUvs.Add(ti >= 0 ? uvs[ti] : Vector2.zero);
            vertNormals.Add(ni >= 0 ? normals[ni] : Vector3.up);
            indexMap[key] = idx;
            return idx;
        }

        private static bool TryVec3(string s, out Vector3 v)
        {
            v = Vector3.zero;
            var p = s.Split(' ');
            if (p.Length < 3) return false;
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        private static bool TryVec2(string s, out Vector2 v)
        {
            v = Vector2.zero;
            var p = s.Split(' ');
            if (p.Length < 2) return false;
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) return false;
            v = new Vector2(x, y);
            return true;
        }
    }
}
