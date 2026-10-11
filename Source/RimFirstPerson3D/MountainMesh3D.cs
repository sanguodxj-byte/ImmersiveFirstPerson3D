// AI生成
using System;
using System.Collections.Generic;

namespace RimFirstPerson3D
{
    public struct MountainPoint3D
    {
        public float x;
        public float y;
        public float z;

        public MountainPoint3D(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString()
        {
            return $"({x:F3}, {y:F3}, {z:F3})";
        }
    }

    public struct MountainTriangle3D
    {
        public MountainPoint3D a;
        public MountainPoint3D b;
        public MountainPoint3D c;
        public MountainPoint3D na;
        public MountainPoint3D nb;
        public MountainPoint3D nc;
        public bool surface;

        public MountainTriangle3D(
            MountainPoint3D a, MountainPoint3D b, MountainPoint3D c,
            MountainPoint3D na, MountainPoint3D nb, MountainPoint3D nc,
            bool surface)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.na = na;
            this.nb = nb;
            this.nc = nc;
            this.surface = surface;
        }
    }

    public static class MountainMesh3D
    {
        private const float MountainCaveCeiling = Metrics3D.MountainCaveCeiling;

        public static void BuildCell(
            MountainShape3D shape,
            int x,
            int z,
            byte[] cells,
            float[] ceilings,
            List<MountainTriangle3D> output)
        {
            if (shape == null || cells == null || output == null) return;
            if (x < 0 || x >= shape.Width || z < 0 || z >= shape.Depth) return;

            int cellIdx = z * shape.Width + x;
            byte cellType = cells[cellIdx];
            if (cellType == 0) return; // 0: absent (dug out / no rock)

            float cap = float.MaxValue;
            if (ceilings != null && cellIdx < ceilings.Length)
            {
                cap = ceilings[cellIdx];
            }

            float bottomY = (cellType == 2) ? MountainCaveCeiling : 0f;

            // 1. Top sloped surface (or clipped top surface)
            BuildTopSurfaces(shape, x, z, bottomY, cap, output);

            // 2. Cave ceiling horizontal face (facing down at MountainCaveCeiling) if cellType == 2
            if (cellType == 2)
            {
                BuildCaveCeiling(shape, x, z, cap, output);
            }

            // 3. Side vertical cliffs
            BuildSides(shape, x, z, cells, ceilings, bottomY, cap, output);
        }

        private static void BuildTopSurfaces(
            MountainShape3D shape,
            int x,
            int z,
            float bottomY,
            float cap,
            List<MountainTriangle3D> output)
        {
            int sx0 = x * 2;
            int sz0 = z * 2;

            for (int dsz = 0; dsz < 2; dsz++)
            {
                for (int dsx = 0; dsx < 2; dsx++)
                {
                    int sx = sx0 + dsx;
                    int sz = sz0 + dsz;

                    float x0 = sx * 0.5f;
                    float x1 = (sx + 1) * 0.5f;
                    float z0 = sz * 0.5f;
                    float z1 = (sz + 1) * 0.5f;

                    float h00 = shape.SampleHeight(sx, sz);
                    float h10 = shape.SampleHeight(sx + 1, sz);
                    float h01 = shape.SampleHeight(sx, sz + 1);
                    float h11 = shape.SampleHeight(sx + 1, sz + 1);

                    var p00 = new MountainPoint3D(x0, h00, z0);
                    var p10 = new MountainPoint3D(x1, h10, z0);
                    var p01 = new MountainPoint3D(x0, h01, z1);
                    var p11 = new MountainPoint3D(x1, h11, z1);

                    var n00 = ComputeVertexNormal(shape, sx, sz);
                    var n10 = ComputeVertexNormal(shape, sx + 1, sz);
                    var n01 = ComputeVertexNormal(shape, sx, sz + 1);
                    var n11 = ComputeVertexNormal(shape, sx + 1, sz + 1);

                    // Unity left-handed coordinates: clockwise when viewed from above (+Y) gives outward +Y normal:
                    // Cross(b - a, c - a).y > 0.
                    if (shape.Diagonal00To11(sx, sz))
                    {
                        ClipAndEmitTopTriangle(p00, p11, p10, n00, n11, n10, bottomY, cap, output);
                        ClipAndEmitTopTriangle(p00, p01, p11, n00, n01, n11, bottomY, cap, output);
                    }
                    else
                    {
                        ClipAndEmitTopTriangle(p00, p01, p10, n00, n01, n10, bottomY, cap, output);
                        ClipAndEmitTopTriangle(p10, p01, p11, n10, n01, n11, bottomY, cap, output);
                    }
                }
            }
        }

        private static readonly List<VertexWithNormal> clipBufA = new List<VertexWithNormal>(8);
        private static readonly List<VertexWithNormal> clipBufB = new List<VertexWithNormal>(8);
        private static readonly List<VertexWithNormal> clipBufC = new List<VertexWithNormal>(8);
        private static readonly int[] SideDx = { 0, 0, -1, 1 };
        private static readonly int[] SideDz = { 1, -1, 0, 0 };

        private static void ClipAndEmitTopTriangle(
            MountainPoint3D a, MountainPoint3D b, MountainPoint3D c,
            MountainPoint3D na, MountainPoint3D nb, MountainPoint3D nc,
            float bottomY, float cap,
            List<MountainTriangle3D> output)
        {
            clipBufA.Clear();
            clipBufA.Add(new VertexWithNormal(a, na));
            clipBufA.Add(new VertexWithNormal(b, nb));
            clipBufA.Add(new VertexWithNormal(c, nc));

            // Clip against y >= bottomY
            ClipPolyAbove(clipBufA, bottomY, clipBufB);
            if (clipBufB.Count < 3) return;

            // Clip against y <= cap
            if (cap < float.MaxValue * 0.5f)
            {
                // 1. Sloped surface portion below cap (y <= cap)
                ClipPolyBelow(clipBufB, cap, clipBufC);
                if (clipBufC.Count >= 3)
                {
                    EmitPoly(clipBufC, true, output);
                }

                // 2. Horizontal flat cap portion where original surface exceeds cap (y >= cap), projected to y = cap
                ClipPolyAbove(clipBufB, cap, clipBufC);
                if (clipBufC.Count >= 3)
                {
                    var capNorm = new MountainPoint3D(0f, 1f, 0f);
                    for (int i = 1; i < clipBufC.Count - 1; i++)
                    {
                        var ca = clipBufC[0].p;
                        var cb = clipBufC[i].p;
                        var cc = clipBufC[i + 1].p;
                        ca.y = cap; cb.y = cap; cc.y = cap;

                        var cross = Cross(Sub(cb, ca), Sub(cc, ca));
                        if (cross.y < -1e-7f)
                        {
                            var temp = cb; cb = cc; cc = temp;
                        }
                        if (Math.Abs(cross.y) > 1e-7f)
                        {
                            output.Add(new MountainTriangle3D(ca, cb, cc, capNorm, capNorm, capNorm, false));
                        }
                    }
                }
            }
            else
            {
                EmitPoly(clipBufB, true, output);
            }
        }

        private struct VertexWithNormal
        {
            public MountainPoint3D p;
            public MountainPoint3D n;

            public VertexWithNormal(MountainPoint3D p, MountainPoint3D n)
            {
                this.p = p;
                this.n = n;
            }
        }

        private static void ClipPolyAbove(List<VertexWithNormal> poly, float yFloor, List<VertexWithNormal> result)
        {
            result.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var next = poly[(i + 1) % poly.Count];
                bool curIn = cur.p.y >= yFloor - 1e-5f;
                bool nextIn = next.p.y >= yFloor - 1e-5f;

                if (curIn)
                {
                    var clamped = cur;
                    if (clamped.p.y < yFloor) clamped.p.y = yFloor;
                    result.Add(clamped);
                    if (!nextIn)
                    {
                        float t = (yFloor - cur.p.y) / (next.p.y - cur.p.y);
                        var inter = LerpVertex(cur, next, t);
                        inter.p.y = yFloor;
                        result.Add(inter);
                    }
                }
                else if (nextIn)
                {
                    float t = (yFloor - cur.p.y) / (next.p.y - cur.p.y);
                    var inter = LerpVertex(cur, next, t);
                    inter.p.y = yFloor;
                    result.Add(inter);
                }
            }
        }

        private static void ClipPolyBelow(
            List<VertexWithNormal> poly, float yCeil, List<VertexWithNormal> result)
        {
            result.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var next = poly[(i + 1) % poly.Count];
                bool curIn = cur.p.y <= yCeil + 1e-5f;
                bool nextIn = next.p.y <= yCeil + 1e-5f;

                if (curIn)
                {
                    var clamped = cur;
                    if (clamped.p.y > yCeil) clamped.p.y = yCeil;
                    result.Add(clamped);
                    if (!nextIn)
                    {
                        float t = (yCeil - cur.p.y) / (next.p.y - cur.p.y);
                        var inter = LerpVertex(cur, next, t);
                        inter.p.y = yCeil;
                        result.Add(inter);
                    }
                }
                else if (nextIn)
                {
                    float t = (yCeil - cur.p.y) / (next.p.y - cur.p.y);
                    var inter = LerpVertex(cur, next, t);
                    inter.p.y = yCeil;
                    result.Add(inter);
                }
            }
        }

        private static VertexWithNormal LerpVertex(VertexWithNormal v1, VertexWithNormal v2, float t)
        {
            float x = v1.p.x + (v2.p.x - v1.p.x) * t;
            float y = v1.p.y + (v2.p.y - v1.p.y) * t;
            float z = v1.p.z + (v2.p.z - v1.p.z) * t;
            float nx = v1.n.x + (v2.n.x - v1.n.x) * t;
            float ny = v1.n.y + (v2.n.y - v1.n.y) * t;
            float nz = v1.n.z + (v2.n.z - v1.n.z) * t;
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; }
            return new VertexWithNormal(
                new MountainPoint3D(x, y, z),
                new MountainPoint3D(nx, ny, nz));
        }

        private static void EmitPoly(List<VertexWithNormal> poly, bool surface, List<MountainTriangle3D> output)
        {
            if (poly.Count < 3) return;
            for (int i = 1; i < poly.Count - 1; i++)
            {
                var a = poly[0].p;
                var b = poly[i].p;
                var c = poly[i + 1].p;
                var cross = Cross(Sub(b, a), Sub(c, a));
                float areaSq = cross.x * cross.x + cross.y * cross.y + cross.z * cross.z;
                if (areaSq < 1e-12f) continue;

                if (cross.y < 0f)
                {
                    output.Add(new MountainTriangle3D(
                        a, c, b,
                        poly[0].n, poly[i + 1].n, poly[i].n,
                        surface));
                }
                else
                {
                    output.Add(new MountainTriangle3D(
                        a, b, c,
                        poly[0].n, poly[i].n, poly[i + 1].n,
                        surface));
                }
            }
        }

        private static void BuildCaveCeiling(
            MountainShape3D shape, int x, int z, float cap, List<MountainTriangle3D> output)
        {
            float yCave = MountainCaveCeiling;
            if (cap < yCave) return;

            int sx0 = x * 2;
            int sz0 = z * 2;

            var down = new MountainPoint3D(0f, -1f, 0f);

            for (int dsz = 0; dsz < 2; dsz++)
            {
                for (int dsx = 0; dsx < 2; dsx++)
                {
                    int sx = sx0 + dsx;
                    int sz = sz0 + dsz;

                    float x0 = sx * 0.5f;
                    float x1 = (sx + 1) * 0.5f;
                    float z0 = sz * 0.5f;
                    float z1 = (sz + 1) * 0.5f;

                    float h00 = shape.SampleHeight(sx, sz);
                    float h10 = shape.SampleHeight(sx + 1, sz);
                    float h01 = shape.SampleHeight(sx, sz + 1);
                    float h11 = shape.SampleHeight(sx + 1, sz + 1);

                    var p00 = new MountainPoint3D(x0, h00, z0);
                    var p10 = new MountainPoint3D(x1, h10, z0);
                    var p01 = new MountainPoint3D(x0, h01, z1);
                    var p11 = new MountainPoint3D(x1, h11, z1);

                    if (shape.Diagonal00To11(sx, sz))
                    {
                        ClipCaveCeilingSubTriangle(p00, p10, p11, yCave, down, output);
                        ClipCaveCeilingSubTriangle(p00, p11, p01, yCave, down, output);
                    }
                    else
                    {
                        ClipCaveCeilingSubTriangle(p00, p10, p01, yCave, down, output);
                        ClipCaveCeilingSubTriangle(p10, p11, p01, yCave, down, output);
                    }
                }
            }
        }

        private static void ClipCaveCeilingSubTriangle(
            MountainPoint3D a, MountainPoint3D b, MountainPoint3D c,
            float yCave, MountainPoint3D down, List<MountainTriangle3D> output)
        {
            clipBufA.Clear();
            clipBufA.Add(new VertexWithNormal(a, down));
            clipBufA.Add(new VertexWithNormal(b, down));
            clipBufA.Add(new VertexWithNormal(c, down));

            ClipPolyAbove(clipBufA, yCave, clipBufB);
            if (clipBufB.Count < 3) return;

            // Project all vertices to yCave and ensure downward winding (Cross.y < 0)
            for (int i = 1; i < clipBufB.Count - 1; i++)
            {
                var pa = clipBufB[0].p; pa.y = yCave;
                var pb = clipBufB[i].p; pb.y = yCave;
                var pc = clipBufB[i + 1].p; pc.y = yCave;

                var cross = Cross(Sub(pb, pa), Sub(pc, pa));
                if (Math.Abs(cross.y) < 1e-7f) continue;
                if (cross.y > 0f)
                {
                    var temp = pb; pb = pc; pc = temp;
                }
                output.Add(new MountainTriangle3D(pa, pb, pc, down, down, down, false));
            }
        }

        private static void BuildSides(
            MountainShape3D shape,
            int x,
            int z,
            byte[] cells,
            float[] ceilings,
            float selfBottom,
            float selfCap,
            List<MountainTriangle3D> output)
        {
            // 4 neighbors: North(0, 1), South(0, -1), West(-1, 0), East(1, 0)
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + SideDx[dir];
                int nz = z + SideDz[dir];

                byte nType = 0;
                float nCap = float.MaxValue;
                if (nx >= 0 && nx < shape.Width && nz >= 0 && nz < shape.Depth)
                {
                    int nIdx = nz * shape.Width + nx;
                    nType = cells[nIdx];
                    if (ceilings != null && nIdx < ceilings.Length) nCap = ceilings[nIdx];
                }

                float nBottom = (nType == 2) ? MountainCaveCeiling : 0f;

                BuildSideEdge(shape, x, z, dir, selfBottom, selfCap, nType, nBottom, nCap, output);
            }
        }

        private static void BuildSideEdge(
            MountainShape3D shape,
            int x,
            int z,
            int dir,
            float selfBottom,
            float selfCap,
            byte nType,
            float nBottom,
            float nCap,
            List<MountainTriangle3D> output)
        {
            // Edge endpoints in (sx, sz) lattice
            // dir 0: North, z edge at z+1, from x to x+1
            // dir 1: South, z edge at z, from x+1 to x
            // dir 2: West, x edge at x, from z to z+1
            // dir 3: East, x edge at x+1, from z+1 to z

            int sxA, szA, sxB, szB;
            MountainPoint3D faceNorm;

            if (dir == 0) // North: (x, z+1) -> (x+1, z+1), normal is (0, 0, 1)
            {
                sxA = x * 2; szA = (z + 1) * 2;
                sxB = (x + 1) * 2; szB = (z + 1) * 2;
                faceNorm = new MountainPoint3D(0f, 0f, 1f);
            }
            else if (dir == 1) // South: (x+1, z) -> (x, z), normal is (0, 0, -1)
            {
                sxA = (x + 1) * 2; szA = z * 2;
                sxB = x * 2; szB = z * 2;
                faceNorm = new MountainPoint3D(0f, 0f, -1f);
            }
            else if (dir == 2) // West: (x, z) -> (x, z+1), normal is (-1, 0, 0)
            {
                sxA = x * 2; szA = z * 2;
                sxB = x * 2; szB = (z + 1) * 2;
                faceNorm = new MountainPoint3D(-1f, 0f, 0f);
            }
            else // East: (x+1, z+1) -> (x+1, z), normal is (1, 0, 0)
            {
                sxA = (x + 1) * 2; szA = (z + 1) * 2;
                sxB = (x + 1) * 2; szB = z * 2;
                faceNorm = new MountainPoint3D(1f, 0f, 0f);
            }

            int sxMid = (sxA + sxB) / 2;
            int szMid = (szA + szB) / 2;

            // Segment 1: A -> Mid
            BuildSideSubSegment(shape, sxA, szA, sxMid, szMid, faceNorm, selfBottom, selfCap, nType, nBottom, nCap, output);
            // Segment 2: Mid -> B
            BuildSideSubSegment(shape, sxMid, szMid, sxB, szB, faceNorm, selfBottom, selfCap, nType, nBottom, nCap, output);
        }

        private static readonly float[] splitTs = new float[6];

        private static void BuildSideSubSegment(
            MountainShape3D shape,
            int sxA, int szA,
            int sxB, int szB,
            MountainPoint3D faceNorm,
            float selfBottom,
            float selfCap,
            byte nType,
            float nBottom,
            float nCap,
            List<MountainTriangle3D> output)
        {
            float xA = sxA * 0.5f;
            float zA = szA * 0.5f;
            float xB = sxB * 0.5f;
            float zB = szB * 0.5f;

            float hA = shape.SampleHeight(sxA, szA);
            float hB = shape.SampleHeight(sxB, szB);

            // Split the segment at any point where h(t) crosses a horizontal plane (selfBottom, selfCap, nBottom, nCap)
            // so piecewise-linear Min/Max intervals never cut corners or leave triangular gaps at cave entrances.
            int count = 0;
            splitTs[count++] = 0f;
            AddSplitT(hA, hB, selfBottom, ref count);
            if (selfCap < float.MaxValue * 0.5f) AddSplitT(hA, hB, selfCap, ref count);
            if (nType != 0)
            {
                AddSplitT(hA, hB, nBottom, ref count);
                if (nCap < float.MaxValue * 0.5f) AddSplitT(hA, hB, nCap, ref count);
            }
            splitTs[count++] = 1f;

            // Insertion sort on small array
            for (int i = 1; i < count; i++)
            {
                float v = splitTs[i];
                int j = i - 1;
                while (j >= 0 && splitTs[j] > v)
                {
                    splitTs[j + 1] = splitTs[j];
                    j--;
                }
                splitTs[j + 1] = v;
            }

            for (int i = 0; i < count - 1; i++)
            {
                float t0 = splitTs[i];
                float t1 = splitTs[i + 1];
                if (t1 - t0 <= 1e-4f) continue;

                float x0 = xA + (xB - xA) * t0;
                float z0 = zA + (zB - zA) * t0;
                float h0 = hA + (hB - hA) * t0;

                float x1 = xA + (xB - xA) * t1;
                float z1 = zA + (zB - zA) * t1;
                float h1 = hA + (hB - hA) * t1;

                EmitSidePiece(x0, z0, h0, x1, z1, h1, faceNorm, selfBottom, selfCap, nType, nBottom, nCap, output);
            }
        }

        private static void AddSplitT(float hA, float hB, float level, ref int count)
        {
            if ((hA < level - 1e-5f && hB > level + 1e-5f) || (hA > level + 1e-5f && hB < level - 1e-5f))
            {
                float t = (level - hA) / (hB - hA);
                if (t > 1e-4f && t < 1f - 1e-4f)
                {
                    splitTs[count++] = t;
                }
            }
        }

        private static void EmitSidePiece(
            float xA, float zA, float hA,
            float xB, float zB, float hB,
            MountainPoint3D faceNorm,
            float selfBottom,
            float selfCap,
            byte nType,
            float nBottom,
            float nCap,
            List<MountainTriangle3D> output)
        {
            float sTopA = Math.Min(hA, selfCap);
            float sTopB = Math.Min(hB, selfCap);
            float sBotA = selfBottom;
            float sBotB = selfBottom;

            if (sTopA <= sBotA + 1e-5f && sTopB <= sBotB + 1e-5f) return;

            if (nType == 0)
            {
                EmitClippedVerticalQuad(xA, zA, xB, zB, sBotA, sTopA, sBotB, sTopB, faceNorm, output);
            }
            else
            {
                // Clamp neighbor top to >= nBottom so when neighbor roof interval [nBottom, min(h, nCap)] is empty,
                // upper [Max(sBot, nTop), sTop] and lower [sBot, Min(sTop, nBottom)] intervals partition cleanly without overlap.
                float nTopA = Math.Max(nBottom, Math.Min(hA, nCap));
                float nTopB = Math.Max(nBottom, Math.Min(hB, nCap));

                // 1. Upper difference: where self is higher than neighbor's top
                float upBotA = Math.Max(sBotA, nTopA);
                float upBotB = Math.Max(sBotB, nTopB);
                if (sTopA > upBotA + 1e-4f || sTopB > upBotB + 1e-4f)
                {
                    EmitClippedVerticalQuad(xA, zA, xB, zB, upBotA, sTopA, upBotB, sTopB, faceNorm, output);
                }

                // 2. Lower difference: where self has solid below neighbor's cave ceiling (nBottom)
                if (nBottom > sBotA + 1e-4f || nBottom > sBotB + 1e-4f)
                {
                    float lowTopA = Math.Min(sTopA, nBottom);
                    float lowTopB = Math.Min(sTopB, nBottom);
                    if (lowTopA > sBotA + 1e-4f || lowTopB > sBotB + 1e-4f)
                    {
                        EmitClippedVerticalQuad(xA, zA, xB, zB, sBotA, lowTopA, sBotB, lowTopB, faceNorm, output);
                    }
                }
            }
        }

        private static void EmitClippedVerticalQuad(
            float xA, float zA, float xB, float zB,
            float yBotA, float yTopA,
            float yBotB, float yTopB,
            MountainPoint3D norm,
            List<MountainTriangle3D> output)
        {
            float dHA = yTopA - yBotA;
            float dHB = yTopB - yBotB;

            if (dHA <= 1e-4f && dHB <= 1e-4f) return;

            if (dHA > 1e-4f && dHB > 1e-4f)
            {
                EmitVerticalQuad(xA, zA, xB, zB, yBotA, yTopA, yBotB, yTopB, norm, output);
                return;
            }

            // Crossing triangle (wedge): clip at exact point where yTop crosses yBot
            if (dHA > 1e-4f)
            {
                float t = dHB < 0f ? dHA / (dHA - dHB) : 1f;
                float xC = xA + (xB - xA) * t;
                float zC = zA + (zB - zA) * t;
                float yC = yBotA + (yBotB - yBotA) * t;
                var pBotA = new MountainPoint3D(xA, yBotA, zA);
                var pTopA = new MountainPoint3D(xA, yTopA, zA);
                var pC = new MountainPoint3D(xC, yC, zC);
                output.Add(new MountainTriangle3D(pBotA, pC, pTopA, norm, norm, norm, false));
            }
            else
            {
                float t = dHA < 0f ? -dHA / (dHB - dHA) : 0f;
                float xC = xA + (xB - xA) * t;
                float zC = zA + (zB - zA) * t;
                float yC = yBotA + (yBotB - yBotA) * t;
                var pC = new MountainPoint3D(xC, yC, zC);
                var pBotB = new MountainPoint3D(xB, yBotB, zB);
                var pTopB = new MountainPoint3D(xB, yTopB, zB);
                output.Add(new MountainTriangle3D(pC, pBotB, pTopB, norm, norm, norm, false));
            }
        }

        private static void EmitVerticalQuad(
            float xA, float zA, float xB, float zB,
            float yBotA, float yTopA,
            float yBotB, float yTopB,
            MountainPoint3D norm,
            List<MountainTriangle3D> output)
        {
            var pBotA = new MountainPoint3D(xA, yBotA, zA);
            var pTopA = new MountainPoint3D(xA, yTopA, zA);
            var pBotB = new MountainPoint3D(xB, yBotB, zB);
            var pTopB = new MountainPoint3D(xB, yTopB, zB);

            // Unity clockwise winding when viewed from outside:
            // Cross(pBotB - pBotA, pTopB - pBotA) points in the direction of +norm.
            output.Add(new MountainTriangle3D(pBotA, pBotB, pTopB, norm, norm, norm, false));
            output.Add(new MountainTriangle3D(pBotA, pTopB, pTopA, norm, norm, norm, false));
        }

        private static MountainPoint3D ComputeVertexNormal(MountainShape3D shape, int sx, int sz)
        {
            // 3x3 Sobel-weighted central difference for smooth isotropic normals without grid-axis crease artifacts
            float h00 = shape.SampleHeight(sx - 1, sz - 1);
            float h10 = shape.SampleHeight(sx, sz - 1);
            float h20 = shape.SampleHeight(sx + 1, sz - 1);
            float h01 = shape.SampleHeight(sx - 1, sz);
            float h21 = shape.SampleHeight(sx + 1, sz);
            float h02 = shape.SampleHeight(sx - 1, sz + 1);
            float h12 = shape.SampleHeight(sx, sz + 1);
            float h22 = shape.SampleHeight(sx + 1, sz + 1);

            // Each sample step is 0.5m, so 2 steps = 1.0m. Sobel weights (1, 2, 1) sum to 4 over a 1.0m span => divide by 4.0m.
            float dhdx = ((h20 - h00) + 2f * (h21 - h01) + (h22 - h02)) * 0.25f;
            float dhdz = ((h02 - h00) + 2f * (h12 - h10) + (h22 - h20)) * 0.25f;

            // Surface: y = h(x, z) => outward normal proportional to (-dh/dx, 1, -dh/dz)
            float nx = -dhdx;
            float ny = 1.0f;
            float nz = -dhdz;

            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-6f)
            {
                nx /= len;
                ny /= len;
                nz /= len;
            }
            return new MountainPoint3D(nx, ny, nz);
        }

        private static MountainPoint3D Sub(MountainPoint3D p1, MountainPoint3D p2)
        {
            return new MountainPoint3D(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        private static MountainPoint3D Cross(MountainPoint3D u, MountainPoint3D v)
        {
            return new MountainPoint3D(
                u.y * v.z - u.z * v.y,
                u.z * v.x - u.x * v.z,
                u.x * v.y - u.y * v.x);
        }
    }
}
