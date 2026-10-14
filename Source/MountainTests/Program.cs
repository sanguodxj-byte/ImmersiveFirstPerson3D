// AI生成
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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
            TestArtificialRoofBoundaryTopology();
            TestEnclosedNaturalRoofAtMountainEdge();
            TestEnclosedNaturalRoofRingAndUpperCap();
            TestThinRockRoofProtectedConstraints();
            TestProtectedTransitionGradients();
            TestUnprotectedOutputFingerprint();

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
                    if (h <= 0f) continue;
                    if (sx + 1 < shape.SampleWidth)
                    {
                        float hR = shape.SampleHeight(sx + 1, sz);
                        if (hR > 0f)
                        {
                            float g = Math.Abs(hR - h) / 0.5f;
                            if (g > maxGrad) maxGrad = g;
                        }
                    }
                    if (sz + 1 < shape.SampleDepth)
                    {
                        float hU = shape.SampleHeight(sx, sz + 1);
                        if (hU > 0f)
                        {
                            float g = Math.Abs(hU - h) / 0.5f;
                            if (g > maxGrad) maxGrad = g;
                        }
                    }
                    if (sx + 1 < shape.SampleWidth && sz + 1 < shape.SampleDepth)
                    {
                        float hDiag = shape.SampleHeight(sx + 1, sz + 1);
                        if (hDiag > 0f)
                        {
                            float g = Math.Abs(hDiag - h) / (0.5f * 1.41421356f);
                            if (g > maxGrad) maxGrad = g;
                        }
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

        static void TestEnclosedNaturalRoofAtMountainEdge()
        {
            const int w = 5, d = 5, x = 1, z = 1;
            var footprint = new bool[w * d];
            footprint[z * w + x] = true;
            footprint[z * w + x + 1] = true;
            var enclosed = new bool[w * d];
            enclosed[z * w + x] = true;
            var shape = new MountainShape3D(w, d, 333);
            var original = new MountainShape3D(w, d, 333);
            original.Build(footprint);
            shape.Build(footprint, enclosed);
            Assert(shape.CellMin(x, z) >= Metrics3D.RockHeight,
                "Enclosed natural roof has solid mountain thickness across the entire cell");

            var cells = new byte[w * d];
            cells[z * w + x] = 2;
            cells[z * w + x + 1] = 1;
            var triangles = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, x, z, cells, null, triangles);
            double ceilingArea = 0, topArea = 0;
            bool noGeometryBelowCeiling = true;
            foreach (var t in triangles)
            {
                double area = Math.Abs((t.b.x - t.a.x) * (t.c.z - t.a.z)
                    - (t.b.z - t.a.z) * (t.c.x - t.a.x)) * 0.5;
                if (t.na.y < -0.9f) ceilingArea += area;
                if (t.surface) topArea += area;
                if (t.a.y < Metrics3D.MountainCaveCeiling - 1e-4f
                    || t.b.y < Metrics3D.MountainCaveCeiling - 1e-4f
                    || t.c.y < Metrics3D.MountainCaveCeiling - 1e-4f)
                    noGeometryBelowCeiling = false;
            }
            Assert(Math.Abs(ceilingArea - 1.0) < 1e-5,
                "Enclosed natural roof ceiling covers full cell at mountain boundary", $"area={ceilingArea}");
            Assert(Math.Abs(topArea - 1.0) < 1e-5 && noGeometryBelowCeiling,
                "Enclosed natural roof retains top and leaves room clear below ceiling");

            bool sharedEdge = true;
            for (int sz = z * 2; sz <= (z + 1) * 2; sz++)
                sharedEdge &= shape.SampleHeight((x + 1) * 2, sz) >= Metrics3D.RockHeight;
            Assert(sharedEdge, "Room roof constraint uses shared samples with adjacent mountain");

            triangles.Clear();
            MountainMesh3D.BuildCell(shape, x, z + 1, cells, null, triangles);
            Assert(triangles.Count == 0, "Room roof constraint does not add roof to an unroofed cell");
            enclosed[z * w + x] = false;
            shape.Build(footprint, enclosed);
            bool restored = true;
            for (int i = 0; i < shape.Heights.Length; i++)
                restored &= shape.Heights[i] == original.Heights[i];
            Assert(restored, "Room roof constraint removal restores original outdoor mountain shape");
        }

        static void TestEnclosedNaturalRoofRingAndUpperCap()
        {
            const int w = 7, d = 7;
            var footprint = new bool[w * d];
            var enclosed = new bool[w * d];
            var cells = new byte[w * d];
            var caps = new float[w * d];
            for (int i = 0; i < caps.Length; i++) caps[i] = float.PositiveInfinity;
            for (int z = 1; z <= 5; z++)
                for (int x = 1; x <= 5; x++)
                {
                    if (x == 3 && z == 3) continue;
                    int i = z * w + x;
                    footprint[i] = enclosed[i] = true;
                    cells[i] = 2;
                    caps[i] = Metrics3D.RockHeight;
                }
            var shape = new MountainShape3D(w, d, 121);
            shape.Build(footprint, enclosed);
            var triangles = new List<MountainTriangle3D>();
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                    MountainMesh3D.BuildCell(shape, x, z, cells, caps, triangles);
            double area = 0;
            bool capped = true, holeClear = true, winding = true;
            foreach (var t in triangles)
            {
                if (t.a.y > Metrics3D.RockHeight + 1e-4f
                    || t.b.y > Metrics3D.RockHeight + 1e-4f
                    || t.c.y > Metrics3D.RockHeight + 1e-4f) capped = false;
                if (t.na.y >= -0.9f) continue;
                double crossY = (t.b.z - t.a.z) * (t.c.x - t.a.x)
                    - (t.b.x - t.a.x) * (t.c.z - t.a.z);
                area += Math.Abs(crossY) * 0.5;
                winding &= crossY < 0;
                float cx = (t.a.x + t.b.x + t.c.x) / 3f;
                float cz = (t.a.z + t.b.z + t.c.z) / 3f;
                if (cx > 3 && cx < 4 && cz > 3 && cz < 4) holeClear = false;
            }
            Assert(Math.Abs(area - 24) < 1e-5 && winding,
                "Enclosed natural roof ring covers every roof cell with downward winding", $"area={area}");
            Assert(holeClear, "Enclosed room preserves unroofed skylight in roof ring");
            Assert(capped, "Enclosed room roof respects upper-level height cap");
        }

        static void TestUnprotectedOutputFingerprint()
        {
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            foreach (int seed in new[] { 1, 777, 9999 })
                foreach (int kind in new[] { 0, 1, 2, 3 })
                {
                    const int w = 32, d = 28;
                    var fp = new bool[w * d];
                    for (int z = 0; z < d; z++)
                        for (int x = 0; x < w; x++)
                            fp[z * w + x] = kind == 0 || (kind == 1 && x > 3 && x < 27 && z > 3 && z < 24)
                                || (kind == 2 && (x == 1 && z == 1 || x > 12 && x < 22 && z > 8 && z < 18))
                                || (kind == 3 && (x - 16) * (x - 16) + (z - 14) * (z - 14) < 100
                                    && !(x > 13 && x < 19 && z > 11 && z < 17));
                    var shape = new MountainShape3D(w, d, seed);
                    shape.Build(fp);
                    foreach (float h in shape.Heights) writer.Write(h);
                }
            writer.Flush();
            string fingerprint = Convert.ToHexString(SHA256.HashData(output.ToArray()));
            Assert(fingerprint == "BC4D716A52882229BDD7C30A3A988728110F53C2E1D7C04F57E542FC0CBEADC9",
                "Unprotected heightfield remains bit-identical across twelve inputs", fingerprint);
        }

        static void TestProtectedTransitionGradients()
        {
            const int w = 24, d = 24;
            foreach (int seed in new[] { 1, 42, 777, 9999 })
            {
                var fp = new bool[w * d];
                var protectedRoofs = new bool[w * d];
                for (int z = 2; z < 22; z++)
                    for (int x = 2; x < 22; x++) fp[z * w + x] = true;
                for (int z = 4; z < 20; z++) protectedRoofs[z * w + 2] = true;
                var shape = new MountainShape3D(w, d, seed);
                shape.Build(fp, protectedRoofs);
                float maxGradient = 0;
                for (int sz = 0; sz < shape.SampleDepth; sz++)
                    for (int sx = 0; sx < shape.SampleWidth; sx++)
                    {
                        float h = shape.SampleHeight(sx, sz);
                        if (h <= 0) continue;
                        foreach (var delta in new[] { (1, 0), (0, 1), (1, 1), (-1, 1) })
                        {
                            float neighbor = shape.SampleHeight(sx + delta.Item1, sz + delta.Item2);
                            if (neighbor <= 0) continue;
                            float distance = delta.Item1 != 0 && delta.Item2 != 0 ? 0.70710678f : 0.5f;
                            maxGradient = Math.Max(maxGradient, Math.Abs(h - neighbor) / distance);
                        }
                    }
                float limit = (float)Math.Tan(Metrics3D.MountainMaxSlopeDegrees * Math.PI / 180);
                Assert(maxGradient <= limit + 1e-4f,
                    $"Protected transition obeys existing slope limit excluding outer cliffs, seed={seed}",
                    $"max={maxGradient}, limit={limit}");
            }
            var isolated = new MountainShape3D(1, 1, 17);
            isolated.Build(new[] { true }, new[] { true });
            Assert(isolated.CellMin(0, 0) >= Metrics3D.RockHeight,
                "Protected isolated cell retains all nine constrained samples at map boundaries");
        }

        static void TestThinRockRoofProtectedConstraints()
        {
            // 1. 受保护屋顶相邻实心岩体连续性：
            // 在山体边缘设置保护格 (x=2, z=2)，东侧 (x=3, z=2) 是实心岩体 (type 1)，
            // 保护格要求最小高度约束下，整个单元格内所有样本高度均 >= RockHeight 且与东侧共享边高度完全一致。
            int w = 8, d = 8;
            var fp = new bool[w * d];
            var enclosed = new bool[w * d];
            fp[2 * w + 2] = true; enclosed[2 * w + 2] = true;
            fp[2 * w + 3] = true; // neighbor solid rock
            fp[3 * w + 3] = true;

            var shape = new MountainShape3D(w, d, 42);
            shape.Build(fp, enclosed);

            Assert(shape.CellMin(2, 2) >= Metrics3D.RockHeight,
                "ThinRockRoof: Protected roof cell achieves full RockHeight thickness");

            // 检查与相邻实心岩体 (3, 2) 的共享边采样点连续且 >= RockHeight
            bool sharedEdgeContinuous = true;
            for (int sz = 4; sz <= 6; sz++)
            {
                float hShared = shape.SampleHeight(6, sz);
                if (hShared < Metrics3D.RockHeight - 1e-4f) sharedEdgeContinuous = false;
            }
            Assert(sharedEdgeContinuous,
                "ThinRockRoof: Shared edge between protected roof and adjacent solid rock is continuous >= RockHeight");

            // 检查受保护屋顶生成完整 2.70m 洞顶几何（1.0 满平米）
            var cells = new byte[w * d];
            cells[2 * w + 2] = 2; // roof
            cells[2 * w + 3] = 1; // solid rock
            var tris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(shape, 2, 2, cells, null, tris);

            double ceilingArea = 0;
            bool noGeometryBelowCeiling = true;
            foreach (var t in tris)
            {
                if (t.na.y < -0.9f)
                {
                    double a = Math.Abs((t.b.x - t.a.x) * (t.c.z - t.a.z) - (t.b.z - t.a.z) * (t.c.x - t.a.x)) * 0.5;
                    ceilingArea += a;
                }
                if (t.a.y < Metrics3D.MountainCaveCeiling - 1e-4f ||
                    t.b.y < Metrics3D.MountainCaveCeiling - 1e-4f ||
                    t.c.y < Metrics3D.MountainCaveCeiling - 1e-4f)
                {
                    noGeometryBelowCeiling = false;
                }
            }
            Assert(Math.Abs(ceilingArea - 1.0) < 1e-5 && noGeometryBelowCeiling,
                "ThinRockRoof: Protected roof generates complete 2.70m cave ceiling (1.0m2) with clear space below");

            // 2. 无顶孔洞不封死、不向无顶格扩展天花板或footprint：
            // 邻接格 (1, 2) 未在 footprint，高度必须为 0
            bool noFootprintExpansion = true;
            for (int sz = 4; sz <= 6; sz++)
            {
                if (shape.SampleHeight(2, sz) != 0f || shape.SampleHeight(3, sz) != 0f)
                {
                    noFootprintExpansion = false;
                }
            }
            Assert(noFootprintExpansion,
                "ThinRockRoof: Protection hard constraint does not expand footprint into adjacent unroofed cells");

            // 3. 多保护Build重建与移除一致性：
            var shapeRebuild = new MountainShape3D(w, d, 42);
            shapeRebuild.Build(fp, null);
            var shapeRestored = new MountainShape3D(w, d, 42);
            // 先加保护再移除保护，恢复后应与无保护完全相同
            shapeRestored.Build(fp, enclosed);
            shapeRestored.Build(fp, null);

            bool exactlyMatchesUnprotected = true;
            for (int i = 0; i < shapeRebuild.Heights.Length; i++)
            {
                if (Math.Abs(shapeRebuild.Heights[i] - shapeRestored.Heights[i]) > 1e-6f)
                {
                    exactlyMatchesUnprotected = false;
                    break;
                }
            }
            Assert(exactlyMatchesUnprotected,
                "ThinRockRoof: Rebuilding without enclosedRoofs fully restores unprotected mountain shape");

            // 4. 上下 cap 与梯度在有保护时均正常：
            var caps = new float[w * d];
            for (int i = 0; i < caps.Length; i++) caps[i] = 2.80f; // cap lower than RockHeight
            tris.Clear();
            MountainMesh3D.BuildCell(shape, 2, 2, cells, caps, tris);
            bool cappedProperly = true;
            foreach (var t in tris)
            {
                if (t.a.y > 2.8001f || t.b.y > 2.8001f || t.c.y > 2.8001f) cappedProperly = false;
            }
            Assert(cappedProperly, "ThinRockRoof: Upper cap correctly clips protected roof surface");
        }

        static void TestArtificialRoofBoundaryTopology()
        {
            bool[] cells =
            {
                true, true, false,
                true, true, false,
                false, false, false
            };
            int count = RoofMeshTopology3D.BoundaryEdgeCount(cells, 3, 3);
            bool corner = RoofMeshTopology3D.IsBoundaryEdge(cells, 3, 3, 0, 0, 0);
            bool isInternal = RoofMeshTopology3D.IsBoundaryEdge(cells, 3, 3, 0, 0, 3);
            bool outside = !RoofMeshTopology3D.IsCovered(cells, 3, 3, 2, 0);
            cells[1] = false;
            int changedCount = RoofMeshTopology3D.BoundaryEdgeCount(cells, 3, 3);
            Assert(count == 8, "Artificial roof boundary emits only outer edges", $"count={count}");
            Assert(corner && !isInternal && outside, "Artificial roof boundary distinguishes covered and exposed neighbors");
            Assert(changedCount == 8, "Artificial roof boundary updates after coverage changes", $"count={changedCount}");
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
