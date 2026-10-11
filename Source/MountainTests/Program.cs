// AI生成
using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimFirstPerson3D;

namespace MountainTests
{
    class Program
    {
        static int passed = 0;
        static int failed = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("Running Mountain 3D Core Tests...");

            TestDeterministicGeneration();
            TestEmptyAllIsolatedMultipleFootprints();
            TestBoundaryExactlyZero();
            TestNonIntegerVariedHeights();
            TestCapped24AndGradientLimit();
            TestHeightAtExactPiecewiseTriangleInterpolation();
            TestSharedSeamGeometryAndNormals();
            TestStableFootprintDuringOccupancyExcavation();
            TestRoofCavityTopContinuityAndNoBottomBelowCeiling();
            TestSlopeClippingTunnelMouth();
            TestAdjacentSolidFacesEliminated();
            TestDegeneracyWindingAndManifoldForSolidAndExcavated();
            TestAspectNormalsAndOutwardCrossProducts();
            TestShallowRoofNeighborNoOverlapAndSplitWallArea();
            TestUpperCapClipping();
            TestRepeatedBuilds();

            Microbench250x250();

            Console.WriteLine($"\nTest Results: Passed={passed}, Failed={failed}");
            if (failed > 0)
            {
                Environment.Exit(1);
            }
        }

        static void Assert(bool condition, string testName, string message = "")
        {
            if (condition)
            {
                passed++;
                Console.WriteLine($"  [PASS] {testName}");
            }
            else
            {
                failed++;
                Console.WriteLine($"  [FAIL] {testName}: {message}");
            }
        }

        static void TestDeterministicGeneration()
        {
            int w = 20, d = 20, seed = 12345;
            var shape1 = new MountainShape3D(w, d, seed);
            var shape2 = new MountainShape3D(w, d, seed);

            bool[] footprint = new bool[w * d];
            for (int z = 4; z < 16; z++)
                for (int x = 4; x < 16; x++)
                    footprint[z * w + x] = true;

            shape1.Build(footprint);
            shape2.Build(footprint);

            bool equal = true;
            for (int i = 0; i < shape1.Heights.Length; i++)
            {
                if (shape1.Heights[i] != shape2.Heights[i])
                {
                    equal = false;
                    break;
                }
            }
            Assert(equal, "TestDeterministicGeneration", "Heights differ for identical seeds and inputs");
        }

        static void TestEmptyAllIsolatedMultipleFootprints()
        {
            int w = 10, d = 10;

            // Empty
            var shapeEmpty = new MountainShape3D(w, d, 42);
            shapeEmpty.Build(new bool[w * d]);
            bool allZero = true;
            foreach (var h in shapeEmpty.Heights) if (h != 0f) allZero = false;
            Assert(allZero, "Empty footprint produces all 0 heights");

            // All true (map boundary samples must still be 0 because adjacent cell outside map is absent)
            var shapeAll = new MountainShape3D(w, d, 42);
            bool[] allFp = new bool[w * d];
            for (int i = 0; i < allFp.Length; i++) allFp[i] = true;
            shapeAll.Build(allFp);

            bool boundaryZero = true;
            for (int sx = 0; sx < shapeAll.SampleWidth; sx++)
            {
                if (shapeAll.SampleHeight(sx, 0) != 0f) boundaryZero = false;
                if (shapeAll.SampleHeight(sx, shapeAll.SampleDepth - 1) != 0f) boundaryZero = false;
            }
            for (int sz = 0; sz < shapeAll.SampleDepth; sz++)
            {
                if (shapeAll.SampleHeight(0, sz) != 0f) boundaryZero = false;
                if (shapeAll.SampleHeight(shapeAll.SampleWidth - 1, sz) != 0f) boundaryZero = false;
            }
            Assert(boundaryZero, "All footprint has 0 heights on map perimeter samples");
            Assert(shapeAll.SampleHeight(w, d) > 0f, "All footprint interior has positive height");

            // Isolated 1x1 rock
            var shapeIso = new MountainShape3D(w, d, 42);
            bool[] isoFp = new bool[w * d];
            isoFp[5 * w + 5] = true;
            shapeIso.Build(isoFp);

            // Center sample of cell (5,5) is sx=11, sz=11
            float centerH = shapeIso.SampleHeight(11, 11);
            Assert(centerH > 0f, "Isolated 1x1 rock has nonzero mound at center sample", $"centerH={centerH}");

            // Samples on the outer boundary of cell (5,5) touching absent cells must be 0
            Assert(shapeIso.SampleHeight(10, 10) == 0f && shapeIso.SampleHeight(12, 12) == 0f,
                "Isolated 1x1 rock outer boundary samples are zero");

            // Multiple disjoint footprints
            bool[] multiFp = new bool[w * d];
            multiFp[2 * w + 2] = true;
            multiFp[7 * w + 7] = true;
            var shapeMulti = new MountainShape3D(w, d, 42);
            shapeMulti.Build(multiFp);
            Assert(shapeMulti.SampleHeight(5, 5) > 0f && shapeMulti.SampleHeight(15, 15) > 0f,
                "Multiple disjoint footprints generate individual mounds");
            Assert(shapeMulti.SampleHeight(10, 10) == 0f, "Gap between disjoint footprints is zero");
        }

        static void TestBoundaryExactlyZero()
        {
            int w = 20, d = 20;
            var shape = new MountainShape3D(w, d, 42);
            bool[] fp = new bool[w * d];
            for (int z = 5; z <= 15; z++)
                for (int x = 5; x <= 15; x++)
                    fp[z * w + x] = true;

            shape.Build(fp);

            bool ok = true;
            // Samples strictly outside or on the boundary of the footprint
            for (int sz = 0; sz < shape.SampleDepth; sz++)
            {
                for (int sx = 0; sx < shape.SampleWidth; sx++)
                {
                    // If this sample is adjacent to any absent cell, it must be exactly 0
                    int minCx = (sx - 1) / 2;
                    int maxCx = sx / 2;
                    int minCz = (sz - 1) / 2;
                    int maxCz = sz / 2;
                    bool interior = true;
                    for (int cz = minCz; cz <= maxCz; cz++)
                    {
                        for (int cx = minCx; cx <= maxCx; cx++)
                        {
                            if (cx < 0 || cx >= w || cz < 0 || cz >= d || !fp[cz * w + cx])
                            {
                                interior = false;
                                break;
                            }
                        }
                    }

                    float h = shape.SampleHeight(sx, sz);
                    if (!interior && h != 0f)
                    {
                        ok = false;
                        break;
                    }
                }
            }
            Assert(ok, "Boundary sample == 0 if ANY adjacent cell absent or outside-map");
        }

        static void TestNonIntegerVariedHeights()
        {
            int w = 30, d = 30;
            var shape = new MountainShape3D(w, d, 999);
            bool[] fp = new bool[w * d];
            for (int z = 2; z < 28; z++)
                for (int x = 2; x < 28; x++)
                    fp[z * w + x] = true;

            shape.Build(fp);

            bool hasFractional = false;
            float minH = float.MaxValue, maxH = float.MinValue;
            foreach (var h in shape.Heights)
            {
                if (h > 0f)
                {
                    if (Math.Abs(h - Math.Round(h)) > 0.01f) hasFractional = true;
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            }

            Assert(hasFractional, "Heights are smooth continuous non-integer values");
            Assert(maxH > minH + 5f, "Heights have wide natural variation", $"min={minH}, max={maxH}");
        }

        static void TestCapped24AndGradientLimit()
        {
            int w = 80, d = 80;
            var shape = new MountainShape3D(w, d, 777);
            bool[] fp = new bool[w * d];
            for (int z = 5; z < 75; z++)
                for (int x = 5; x < 75; x++)
                    fp[z * w + x] = true;

            shape.Build(fp);

            float maxObserved = 0f;
            foreach (var h in shape.Heights)
            {
                if (h > maxObserved) maxObserved = h;
            }
            Assert(maxObserved <= 24.0001f, "Height capped at 24m", $"maxObserved={maxObserved}");

            // Gradient test <= tan(55 deg) with small numerical tolerance
            float tan55 = (float)Math.Tan(55.0 * Math.PI / 180.0);
            float maxGrad = 0f;

            for (int sz = 0; sz < shape.SampleDepth; sz++)
            {
                for (int sx = 0; sx < shape.SampleWidth; sx++)
                {
                    float h = shape.SampleHeight(sx, sz);
                    if (sx + 1 < shape.SampleWidth)
                    {
                        float hR = shape.SampleHeight(sx + 1, sz);
                        float g = Math.Abs(hR - h) / 0.5f;
                        if (g > maxGrad) maxGrad = g;
                    }
                    if (sz + 1 < shape.SampleDepth)
                    {
                        float hU = shape.SampleHeight(sx, sz + 1);
                        float g = Math.Abs(hU - h) / 0.5f;
                        if (g > maxGrad) maxGrad = g;
                    }
                    if (sx + 1 < shape.SampleWidth && sz + 1 < shape.SampleDepth)
                    {
                        float hDiag = shape.SampleHeight(sx + 1, sz + 1);
                        float g = Math.Abs(hDiag - h) / (0.5f * 1.41421356f);
                        if (g > maxGrad) maxGrad = g;
                    }
                }
            }

            // tan55 is ~1.428. Allow slight numerical margin 0.05
            Assert(maxGrad <= tan55 + 0.05f, "Gradient <= tan(55 deg)", $"maxGrad={maxGrad}, tan55={tan55}");
        }

        static void TestHeightAtExactPiecewiseTriangleInterpolation()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 123);
            bool[] fp = new bool[w * d];
            for (int i = 0; i < fp.Length; i++) fp[i] = true;
            shape.Build(fp);

            // Test exact sample point match
            bool matchSamples = true;
            for (int sz = 0; sz < shape.SampleDepth; sz++)
            {
                for (int sx = 0; sx < shape.SampleWidth; sx++)
                {
                    float x = sx * 0.5f;
                    float z = sz * 0.5f;
                    float hInterp = shape.HeightAt(x, z);
                    float hSample = shape.SampleHeight(sx, sz);
                    if (Math.Abs(hInterp - hSample) > 1e-4f)
                    {
                        matchSamples = false;
                        break;
                    }
                }
            }
            Assert(matchSamples, "HeightAt matches exact lattice sample heights");

            // Test subquad midpoint interpolation matches plane equation of diagonal
            int testSx = 4, testSz = 4;
            float h00 = shape.SampleHeight(testSx, testSz);
            float h10 = shape.SampleHeight(testSx + 1, testSz);
            float h01 = shape.SampleHeight(testSx, testSz + 1);
            float h11 = shape.SampleHeight(testSx + 1, testSz + 1);
            bool diag0011 = shape.Diagonal00To11(testSx, testSz);

            float midX = (testSx + 0.5f) * 0.5f;
            float midZ = (testSz + 0.5f) * 0.5f;
            float hMid = shape.HeightAt(midX, midZ);

            float expectedMid = diag0011 ? 0.5f * (h00 + h11) : 0.5f * (h10 + h01);
            Assert(Math.Abs(hMid - expectedMid) < 1e-4f, "HeightAt subquad diagonal midpoint matches exact diagonal");
        }

        static void TestSharedSeamGeometryAndNormals()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 321);
            bool[] fp = new bool[w * d];
            for (int z = 2; z < 8; z++)
                for (int x = 2; x < 8; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int z = 2; z < 8; z++)
                for (int x = 2; x < 8; x++)
                    cells[z * w + x] = 1;

            var trisCellA = new List<MountainTriangle3D>();
            var trisCellB = new List<MountainTriangle3D>();

            // Adjacent cells (4, 4) and (5, 4) sharing boundary at x = 5.0
            MountainMesh3D.BuildCell(shape, 4, 4, cells, null, trisCellA);
            MountainMesh3D.BuildCell(shape, 5, 4, cells, null, trisCellB);

            // Collect top surface vertices at seam x = 5.0
            var seamVertsA = new List<MountainPoint3D>();
            var seamNormsA = new List<MountainPoint3D>();
            foreach (var t in trisCellA)
            {
                if (!t.surface) continue;
                if (Math.Abs(t.a.x - 5.0f) < 1e-4f) { seamVertsA.Add(t.a); seamNormsA.Add(t.na); }
                if (Math.Abs(t.b.x - 5.0f) < 1e-4f) { seamVertsA.Add(t.b); seamNormsA.Add(t.nb); }
                if (Math.Abs(t.c.x - 5.0f) < 1e-4f) { seamVertsA.Add(t.c); seamNormsA.Add(t.nc); }
            }

            var seamVertsB = new List<MountainPoint3D>();
            var seamNormsB = new List<MountainPoint3D>();
            foreach (var t in trisCellB)
            {
                if (!t.surface) continue;
                if (Math.Abs(t.a.x - 5.0f) < 1e-4f) { seamVertsB.Add(t.a); seamNormsB.Add(t.na); }
                if (Math.Abs(t.b.x - 5.0f) < 1e-4f) { seamVertsB.Add(t.b); seamNormsB.Add(t.nb); }
                if (Math.Abs(t.c.x - 5.0f) < 1e-4f) { seamVertsB.Add(t.c); seamNormsB.Add(t.nc); }
            }

            Assert(seamVertsA.Count > 0 && seamVertsB.Count > 0, "Seam has vertices on both adjacent cells");

            // For every vertex in A on seam, there should be an identical vertex in B with identical normal
            bool matchedAll = true;
            for (int i = 0; i < seamVertsA.Count; i++)
            {
                var va = seamVertsA[i];
                var na = seamNormsA[i];
                bool found = false;
                for (int j = 0; j < seamVertsB.Count; j++)
                {
                    var vb = seamVertsB[j];
                    var nb = seamNormsB[j];
                    if (Math.Abs(va.z - vb.z) < 1e-4f && Math.Abs(va.y - vb.y) < 1e-4f)
                    {
                        if (Math.Abs(na.x - nb.x) < 1e-3f && Math.Abs(na.y - nb.y) < 1e-3f && Math.Abs(na.z - nb.z) < 1e-3f)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found) { matchedAll = false; break; }
            }
            Assert(matchedAll, "Shared seam geometry and normals match between adjacent cells");
        }

        static void TestStableFootprintDuringOccupancyExcavation()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 555);
            bool[] fp = new bool[w * d];
            for (int z = 2; z < 8; z++)
                for (int x = 2; x < 8; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            float preH = shape.HeightAt(4.5f, 4.5f);

            // Mining out cell (4, 4): cells[4,4] = 0, but footprint remains in shape
            byte[] cells = new byte[w * d];
            for (int z = 2; z < 8; z++)
                for (int x = 2; x < 8; x++)
                    cells[z * w + x] = 1;
            cells[4 * w + 4] = 0;

            float postH = shape.HeightAt(4.5f, 4.5f);
            Assert(preH == postH, "Shape heightfield is stable and not recalculated upon excavation");

            // When building cell (4, 4) which is 0, zero triangles emitted
            var tris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 4, 4, cells, null, tris);
            Assert(tris.Count == 0, "Dug-out cell (cells[idx]=0) emits no triangles");

            // Neighbor cell (5, 4) should emit cliff facing west into (4, 4)
            var trisNeighbor = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 5, 4, cells, null, trisNeighbor);
            bool hasWestWall = false;
            foreach (var t in trisNeighbor)
            {
                if (!t.surface && t.na.x < -0.9f) hasWestWall = true;
            }
            Assert(hasWestWall, "Neighbor cell exposes vertical cliff toward excavated cell");
        }

        static void TestRoofCavityTopContinuityAndNoBottomBelowCeiling()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 888);
            bool[] fp = new bool[w * d];
            for (int z = 1; z < 9; z++)
                for (int x = 1; x < 9; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int i = 0; i < cells.Length; i++) cells[i] = 1;
            // Make cell (5, 5) roof-only (type 2)
            cells[5 * w + 5] = 2;

            var trisRoof = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 5, 5, cells, null, trisRoof);

            bool hasTopSurface = false;
            bool hasCaveCeiling = false;
            bool anyBelowCeiling = false;

            foreach (var t in trisRoof)
            {
                if (t.surface) hasTopSurface = true;
                if (!t.surface && t.na.y < -0.9f)
                {
                    hasCaveCeiling = true;
                    if (Math.Abs(t.a.y - 2.70f) > 1e-3f || Math.Abs(t.b.y - 2.70f) > 1e-3f || Math.Abs(t.c.y - 2.70f) > 1e-3f)
                    {
                        Assert(false, "Cave ceiling triangle not at exactly 2.70m");
                    }
                }
                if (t.a.y < 2.69f || t.b.y < 2.69f || t.c.y < 2.69f)
                {
                    anyBelowCeiling = true;
                }
            }

            Assert(hasTopSurface, "Roof cell retains top continuous sloped surface", $"tris={trisRoof.Count}, min={shape.CellMin(5,5)}, max={shape.CellMax(5,5)}");
            Assert(hasCaveCeiling, "Roof cell generates downward-facing cave ceiling at 2.70m", $"tris={trisRoof.Count}");
            Assert(!anyBelowCeiling, "Roof cell has no geometry below 2.70m");
        }

        static void TestSlopeClippingTunnelMouth()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 444);
            bool[] fp = new bool[w * d];
            for (int z = 2; z < 8; z++)
                for (int x = 2; x < 8; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            // Suppose cell (2, 4) is at the foot of mountain where H rises from 0 to ~3m
            // Make (2, 4) roof-only (cell type 2: ceiling at 2.7m)
            byte[] cells = new byte[w * d];
            cells[2 * w + 4] = 2;

            var tris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 2, 4, cells, null, tris);

            // Ceiling at 2.70m must only exist where H > 2.70m
            bool ceilingValid = true;
            foreach (var t in tris)
            {
                if (!t.surface && t.na.y < -0.9f)
                {
                    // Check if points are within region where shape height >= 2.70m
                    float hA = shape.HeightAt(t.a.x, t.a.z);
                    float hB = shape.HeightAt(t.b.x, t.b.z);
                    float hC = shape.HeightAt(t.c.x, t.c.z);
                    if (hA < 2.69f || hB < 2.69f || hC < 2.69f)
                    {
                        ceilingValid = false;
                    }
                }
            }
            Assert(ceilingValid, "Cave ceiling at 2.70m generated only where H >= 2.70m (clipped at tunnel mouth)");
        }

        static void TestAdjacentSolidFacesEliminated()
        {
            int w = 10, d = 10;
            var shape = new MountainShape3D(w, d, 222);
            bool[] fp = new bool[w * d];
            for (int z = 3; z < 7; z++)
                for (int x = 3; x < 7; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int z = 3; z < 7; z++)
                for (int x = 3; x < 7; x++)
                    cells[z * w + x] = 1;

            var trisCenter = new List<MountainTriangle3D>();
            // Cell (4, 4) surrounded by full solid rock (type 1) on all 4 sides
            MountainMesh3D.BuildCell(shape, 4, 4, cells, null, trisCenter);

            bool hasSideCliff = false;
            foreach (var t in trisCenter)
            {
                if (!t.surface) hasSideCliff = true;
            }
            Assert(!hasSideCliff, "No internal side cliff faces emitted between adjacent full solid cells");
        }

        static void TestUpperCapClipping()
        {
            int w = 16, d = 16;
            var shape = new MountainShape3D(w, d, 666);
            bool[] fp = new bool[w * d];
            for (int z = 2; z < 14; z++)
                for (int x = 2; x < 14; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            float[] ceilings = new float[w * d];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = 1;
                ceilings[i] = 5.0f; // upper floor cap at 5.0m
            }

            var tris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 8, 8, cells, ceilings, tris);

            bool exceedsCap = false;
            bool hasCapFlatSurface = false;
            foreach (var t in tris)
            {
                if (t.a.y > 5.001f || t.b.y > 5.001f || t.c.y > 5.001f) exceedsCap = true;
                if (!t.surface && Math.Abs(t.a.y - 5.0f) < 1e-3f && t.na.y > 0.9f) hasCapFlatSurface = true;
            }

            Assert(!exceedsCap, "Upper cap clips all triangles at ceiling height (5.0m)");
            Assert(hasCapFlatSurface, "Horizontal cap surface generated where cap cuts mountain slope", $"tris={tris.Count}, min={shape.CellMin(8,8)}, max={shape.CellMax(8,8)}");
        }

        static void TestRepeatedBuilds()
        {
            int w = 15, d = 15;
            var shape = new MountainShape3D(w, d, 101);
            bool[] fp1 = new bool[w * d];
            fp1[5 * w + 5] = true;
            shape.Build(fp1);
            float h1 = shape.SampleHeight(11, 11);

            bool[] fp2 = new bool[w * d];
            for (int i = 0; i < fp2.Length; i++) fp2[i] = true;
            shape.Build(fp2);
            float h2 = shape.SampleHeight(11, 11);

            shape.Build(fp1);
            float h3 = shape.SampleHeight(11, 11);

            Assert(h1 == h3 && h1 != h2, "Repeated builds reset state correctly and deterministically");
        }

        static void TestDegeneracyWindingAndManifoldForSolidAndExcavated()
        {
            int w = 8, d = 8;
            var shape = new MountainShape3D(w, d, 333);
            bool[] fp = new bool[w * d];
            for (int z = 1; z < 7; z++)
                for (int x = 1; x < 7; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int z = 1; z < 7; z++)
                for (int x = 1; x < 7; x++)
                    cells[z * w + x] = 1;
            // Dig out center
            cells[3 * w + 3] = 0;
            // Turn (3, 4) into cave roof
            cells[4 * w + 3] = 2;

            var allTris = new List<MountainTriangle3D>();
            for (int z = 1; z < 7; z++)
            {
                for (int x = 1; x < 7; x++)
                {
                    MountainMesh3D.BuildCell(shape, x, z, cells, null, allTris);
                }
            }

            bool nonDegenerate = true;
            bool validWinding = true;

            foreach (var t in allTris)
            {
                var ab = Sub(t.b, t.a);
                var ac = Sub(t.c, t.a);
                var cross = Cross(ab, ac);
                float area2 = (float)Math.Sqrt(cross.x * cross.x + cross.y * cross.y + cross.z * cross.z);
                if (area2 < 1e-7f)
                {
                    nonDegenerate = false;
                    break;
                }

                // Check face normal orientation roughly aligns with vertex normals
                float dot = cross.x * t.na.x + cross.y * t.na.y + cross.z * t.na.z;
                if (dot < -1e-4f)
                {
                    validWinding = false;
                    break;
                }
            }

            Assert(nonDegenerate, "All emitted triangles are non-degenerate (positive area)");
            Assert(validWinding, "Triangle winding is consistently oriented outward");
        }

        static void TestAspectNormalsAndOutwardCrossProducts()
        {
            int w = 14, d = 14;
            var shape = new MountainShape3D(w, d, 2026);
            bool[] fp = new bool[w * d];
            for (int z = 3; z <= 10; z++)
                for (int x = 3; x <= 10; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int z = 3; z <= 10; z++)
                for (int x = 3; x <= 10; x++)
                    cells[z * w + x] = 1;

            // Check south slope (z = 3, where height increases with +z => normal must point south: nz < 0, ny > 0)
            var southTris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 6, 3, cells, null, southTris);
            bool southNormalOk = southTris.Count > 0;
            foreach (var t in southTris)
            {
                if (!t.surface) continue;
                var cross = Cross(Sub(t.b, t.a), Sub(t.c, t.a));
                if (cross.y <= 0f || cross.z >= 0f || t.na.y <= 0f || t.na.z >= 0f)
                {
                    southNormalOk = false;
                    break;
                }
            }
            Assert(southNormalOk, "South slope triangles have outward normal pointing up (+Y) and south (-Z)");

            // Check north slope (z = 10, where height decreases with +z => normal must point north: nz > 0, ny > 0)
            var northTris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 6, 10, cells, null, northTris);
            bool northNormalOk = northTris.Count > 0;
            foreach (var t in northTris)
            {
                if (!t.surface) continue;
                var cross = Cross(Sub(t.b, t.a), Sub(t.c, t.a));
                if (cross.y <= 0f || cross.z <= 0f || t.na.y <= 0f || t.na.z <= 0f)
                {
                    northNormalOk = false;
                    break;
                }
            }
            Assert(northNormalOk, "North slope triangles have outward normal pointing up (+Y) and north (+Z)");

            // Check west/east/north/south vertical cliffs around excavated cell (6, 6)
            cells[6 * w + 6] = 0;
            var cliffCheck = new (int cx, int cz, float nx, float nz, string label)[]
            {
                (6, 5, 0f, 1f, "North-facing cliff from south neighbor (6,5)"),
                (6, 7, 0f, -1f, "South-facing cliff from north neighbor (6,7)"),
                (7, 6, -1f, 0f, "West-facing cliff from east neighbor (7,6)"),
                (5, 6, 1f, 0f, "East-facing cliff from west neighbor (5,6)")
            };
            foreach (var item in cliffCheck)
            {
                var tris = new List<MountainTriangle3D>();
                MountainMesh3D.BuildCell(shape, item.cx, item.cz, cells, null, tris);
                bool foundCliff = false;
                bool cliffOriented = true;
                foreach (var t in tris)
                {
                    if (t.surface) continue;
                    if (Math.Abs(t.na.x - item.nx) < 1e-3f && Math.Abs(t.na.z - item.nz) < 1e-3f)
                    {
                        foundCliff = true;
                        var cross = Cross(Sub(t.b, t.a), Sub(t.c, t.a));
                        float len = (float)Math.Sqrt(cross.x * cross.x + cross.y * cross.y + cross.z * cross.z);
                        float dot = (cross.x * item.nx + cross.z * item.nz) / len;
                        if (dot < 0.99f) cliffOriented = false;
                    }
                }
                Assert(foundCliff && cliffOriented, item.label + " has exact outward winding and normal");
            }
        }

        static void TestShallowRoofNeighborNoOverlapAndSplitWallArea()
        {
            int w = 12, d = 12;
            var shape = new MountainShape3D(w, d, 3030);
            bool[] fp = new bool[w * d];
            for (int z = 2; z <= 9; z++)
                for (int x = 2; x <= 9; x++)
                    fp[z * w + x] = true;
            shape.Build(fp);

            byte[] cells = new byte[w * d];
            for (int z = 2; z <= 9; z++)
                for (int x = 2; x <= 9; x++)
                    cells[z * w + x] = 1;

            // Cell (2, 5) is at the western foot of the mountain where heights along x=3 are below 2.7m.
            // Make (2, 5) a roof-only cell (type 2), while (3, 5) is full rock (type 1).
            cells[5 * w + 2] = 2;

            var trisSolid = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 3, 5, cells, null, trisSolid);

            // Sum the area of west-facing wall triangles emitted by (3, 5) at x = 3.0
            float actualWestArea = 0f;
            foreach (var t in trisSolid)
            {
                if (!t.surface && t.na.x < -0.9f)
                {
                    var cross = Cross(Sub(t.b, t.a), Sub(t.c, t.a));
                    actualWestArea += 0.5f * (float)Math.Sqrt(cross.x * cross.x + cross.y * cross.y + cross.z * cross.z);
                }
            }

            // Expected exposed wall area at x = 3.0 between z = 5.0 and z = 6.0:
            // Since neighbor (2, 5) is type 2 (bottom = 2.70m), any portion of (3, 5) below 2.70m is exposed,
            // and any portion where h < 2.70m has empty neighbor roof so the entire [0, h] is exposed ONCE (no double overlap).
            float h0 = shape.SampleHeight(6, 10);
            float h1 = shape.SampleHeight(6, 11);
            float h2 = shape.SampleHeight(6, 12);
            float expectedArea = ExposedAgainstRoofIntegral(h0, h1, 0.5f) + ExposedAgainstRoofIntegral(h1, h2, 0.5f);

            Assert(Math.Abs(actualWestArea - expectedArea) < 1e-3f,
                "Full rock cell adjacent to shallow roof cell emits exact non-overlapping side wall area",
                $"actual={actualWestArea:F4}, expected={expectedArea:F4}");

            // Also verify that a roof cell crossing 2.70m along its boundary against empty space (type 0)
            // splits the segment at h = 2.70m without leaving a triangular corner gap.
            // Find any cell with min < 2.70m and max > 2.70m and make it type 2 next to empty type 0.
            int testX = 3, testZ = 5;
            for (int z = 2; z <= 9; z++)
            {
                for (int x = 2; x <= 9; x++)
                {
                    if (shape.CellMin(x, z) < 2.5f && shape.CellMax(x, z) > 2.9f)
                    {
                        testX = x;
                        testZ = z;
                        break;
                    }
                }
            }
            Array.Clear(cells, 0, cells.Length);
            cells[testZ * w + testX] = 2; // isolated roof cell against empty neighbors
            var trisRoof = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, testX, testZ, cells, null, trisRoof);

            float actualNorthArea = 0f;
            foreach (var t in trisRoof)
            {
                if (!t.surface && t.na.z > 0.9f)
                {
                    var cross = Cross(Sub(t.b, t.a), Sub(t.c, t.a));
                    actualNorthArea += 0.5f * (float)Math.Sqrt(cross.x * cross.x + cross.y * cross.y + cross.z * cross.z);
                }
            }
            float hn0 = shape.SampleHeight(testX * 2, (testZ + 1) * 2);
            float hn1 = shape.SampleHeight(testX * 2 + 1, (testZ + 1) * 2);
            float hn2 = shape.SampleHeight(testX * 2 + 2, (testZ + 1) * 2);
            float expectedNorthArea = RoofSideIntegral(hn0, hn1, 0.5f) + RoofSideIntegral(hn1, hn2, 0.5f);
            Assert(Math.Abs(actualNorthArea - expectedNorthArea) < 1e-3f,
                "Roof cell side wall crossing 2.70m matches exact integral without triangular gap",
                $"actual={actualNorthArea:F4}, expected={expectedNorthArea:F4}");
        }

        static float ExposedAgainstRoofIntegral(float hA, float hB, float width)
        {
            // Neighbor roof occupies [2.7, max(2.7, h)]. So self [0, h] exposed against neighbor is min(h, 2.7).
            int steps = 1000;
            float sum = 0f;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                float h = hA + (hB - hA) * t;
                sum += Math.Min(Math.Max(0f, h), 2.70f);
            }
            return sum * (width / steps);
        }

        static float RoofSideIntegral(float hA, float hB, float width)
        {
            int steps = 1000;
            float sum = 0f;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                float h = hA + (hB - hA) * t;
                if (h > 2.70f) sum += h - 2.70f;
            }
            return sum * (width / steps);
        }

        static void Microbench250x250()
        {
            int w = 250, d = 250;
            Console.WriteLine($"\nStarting Microbench {w}x{250} (62,500 cells, 251,001 height samples)...");

            var sw = Stopwatch.StartNew();
            var shape = new MountainShape3D(w, d, 9999);

            bool[] fp = new bool[w * d];
            // 40% mountain coverage
            for (int z = 20; z < 230; z++)
            {
                for (int x = 20; x < 230; x++)
                {
                    if (((x - 125) * (x - 125) + (z - 125) * (z - 125)) <= 80 * 80)
                    {
                        fp[z * w + x] = true;
                    }
                }
            }

            long initMem = GC.GetTotalMemory(true);
            sw.Restart();
            shape.Build(fp);
            sw.Stop();
            long shapeTimeMs = sw.ElapsedMilliseconds;
            Console.WriteLine($"  MountainShape3D.Build(250x250) took: {shapeTimeMs} ms");

            byte[] cells = new byte[w * d];
            for (int i = 0; i < fp.Length; i++)
            {
                if (fp[i]) cells[i] = (byte)(i % 5 == 0 ? 2 : 1);
            }

            // Benchmark building cells for a 16x16 chunk
            var chunkTris = new List<MountainTriangle3D>(4096);
            sw.Restart();
            for (int cz = 80; cz < 96; cz++)
            {
                for (int cx = 80; cx < 96; cx++)
                {
                    MountainMesh3D.BuildCell(shape, cx, cz, cells, null, chunkTris);
                }
            }
            sw.Stop();
            long chunkBuildMs = sw.ElapsedMilliseconds;
            Console.WriteLine($"  MountainMesh3D 16x16 chunk build ({chunkTris.Count} tris) took: {chunkBuildMs} ms");
            long endMem = GC.GetTotalMemory(false);
            Console.WriteLine($"  Memory footprint delta: {(endMem - initMem) / 1024} KB");

            Assert(shapeTimeMs < 1000, "250x250 full heightfield generation under 1000ms");
            Assert(chunkBuildMs < 50, "16x16 chunk mesh generation under 50ms");
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
