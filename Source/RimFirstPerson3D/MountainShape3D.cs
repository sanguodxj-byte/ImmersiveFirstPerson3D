// AI生成
using System;

namespace RimFirstPerson3D
{
    public sealed class MountainShape3D
    {
        public const int SamplesPerCell = 2;

        public readonly int Width;
        public readonly int Depth;
        public readonly int Seed;

        public readonly int SampleWidth;
        public readonly int SampleDepth;

        public float[] Heights;

        public MountainShape3D(int width, int depth, int seed)
        {
            if (width < 0 || depth < 0) throw new ArgumentOutOfRangeException();
            Width = width;
            Depth = depth;
            Seed = seed;
            SampleWidth = width * SamplesPerCell + 1;
            SampleDepth = depth * SamplesPerCell + 1;
            Heights = new float[SampleWidth * SampleDepth];
        }

        public float SampleHeight(int sx, int sz)
        {
            if (sx < 0 || sx >= SampleWidth || sz < 0 || sz >= SampleDepth)
                return 0f;
            return Heights[sz * SampleWidth + sx];
        }

        public bool Diagonal00To11(int sx, int sz)
        {
            return ((sx ^ sz) & 1) == 0;
        }

        public float HeightAt(float x, float z)
        {
            if (Width <= 0 || Depth <= 0) return 0f;
            if (x < 0f || z < 0f || x > Width || z > Depth)
                return 0f;

            float subX = x * SamplesPerCell;
            float subZ = z * SamplesPerCell;

            int sx = (int)Math.Floor(subX);
            int sz = (int)Math.Floor(subZ);

            if (sx < 0) sx = 0;
            if (sz < 0) sz = 0;
            if (sx >= SampleWidth - 1) sx = SampleWidth - 2;
            if (sz >= SampleDepth - 1) sz = SampleDepth - 2;

            float fx = subX - sx;
            float fz = subZ - sz;
            if (fx < 0f) fx = 0f; else if (fx > 1f) fx = 1f;
            if (fz < 0f) fz = 0f; else if (fz > 1f) fz = 1f;

            float h00 = Heights[sz * SampleWidth + sx];
            float h10 = Heights[sz * SampleWidth + (sx + 1)];
            float h01 = Heights[(sz + 1) * SampleWidth + sx];
            float h11 = Heights[(sz + 1) * SampleWidth + (sx + 1)];

            if (Diagonal00To11(sx, sz))
            {
                if (fx >= fz)
                {
                    return h00 + fx * (h10 - h00) + fz * (h11 - h10);
                }
                else
                {
                    return h00 + fx * (h11 - h01) + fz * (h01 - h00);
                }
            }
            else
            {
                if (fx + fz <= 1f)
                {
                    return h00 + fx * (h10 - h00) + fz * (h01 - h00);
                }
                else
                {
                    return h11 + (1f - fx) * (h01 - h11) + (1f - fz) * (h10 - h11);
                }
            }
        }

        public float CellMin(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Depth) return 0f;
            int sx0 = x * SamplesPerCell;
            int sz0 = z * SamplesPerCell;

            float min = float.MaxValue;
            for (int dz = 0; dz <= SamplesPerCell; dz++)
            {
                int row = (sz0 + dz) * SampleWidth;
                for (int dx = 0; dx <= SamplesPerCell; dx++)
                {
                    float h = Heights[row + (sx0 + dx)];
                    if (h < min) min = h;
                }
            }
            return min;
        }

        public float CellMax(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Depth) return 0f;
            int sx0 = x * SamplesPerCell;
            int sz0 = z * SamplesPerCell;

            float max = float.MinValue;
            for (int dz = 0; dz <= SamplesPerCell; dz++)
            {
                int row = (sz0 + dz) * SampleWidth;
                for (int dx = 0; dx <= SamplesPerCell; dx++)
                {
                    float h = Heights[row + (sx0 + dx)];
                    if (h > max) max = h;
                }
            }
            return max;
        }

        public void Build(bool[] footprint)
        {
            if (Heights.Length != SampleWidth * SampleDepth)
            {
                Heights = new float[SampleWidth * SampleDepth];
            }
            else
            {
                Array.Clear(Heights, 0, Heights.Length);
            }

            if (footprint == null || footprint.Length != Width * Depth || Width == 0 || Depth == 0)
                return;

            float mountainMaxHeight = Metrics3D.MountainMaxHeight;
            float footSlopeDeg = Metrics3D.MountainFootSlopeDegrees;
            float mainSlopeDeg = Metrics3D.MountainMainSlopeDegrees;
            float maxSlopeDeg = Metrics3D.MountainMaxSlopeDegrees;
            float footDist = Metrics3D.MountainFootDistance;
            float shoulderDist = Metrics3D.MountainShoulderDistance;
            float noiseScale = Metrics3D.MountainNoiseScale;
            float noiseAmp = Metrics3D.MountainNoiseAmplitude;
            int smoothingPasses = Metrics3D.MountainSmoothingPasses;

            float tanFoot = (float)Math.Tan(footSlopeDeg * Math.PI / 180.0);
            float tanMain = (float)Math.Tan(mainSlopeDeg * Math.PI / 180.0);
            float tanMax = (float)Math.Tan(maxSlopeDeg * Math.PI / 180.0);

            int totalSamples = SampleWidth * SampleDepth;
            float[] dist = new float[totalSamples];

            ComputeDistanceTransform(footprint, dist);
            SoftenDistanceField(dist);

            for (int sz = 0; sz < SampleDepth; sz++)
            {
                int row = sz * SampleWidth;
                float worldZ = sz * 0.5f;

                for (int sx = 0; sx < SampleWidth; sx++)
                {
                    float d = dist[row + sx];
                    if (d <= 0f)
                    {
                        Heights[row + sx] = 0f;
                        continue;
                    }

                    // Profile from foot -> main slope -> summit plateau
                    // Note: Natural rock / mountain roof in RimWorld has cave ceiling at 2.70m,
                    // so slopes rise briskly at the foot (tanMain) and transition into the shoulder and plateau.
                    float baseH;
                    if (d <= footDist)
                    {
                        float t = d / footDist;
                        float slope = tanFoot + (tanMain - tanFoot) * (0.35f + 0.65f * t);
                        baseH = d * slope;
                    }
                    else if (d <= shoulderDist)
                    {
                        float hFoot = footDist * (tanFoot + (tanMain - tanFoot) * 1.0f);
                        baseH = hFoot + (d - footDist) * tanMain;
                    }
                    else
                    {
                        float hFoot = footDist * (tanFoot + (tanMain - tanFoot) * 1.0f);
                        float hShoulder = hFoot + (shoulderDist - footDist) * tanMain;
                        float extra = d - shoulderDist;
                        float remaining = mountainMaxHeight - hShoulder;
                        if (remaining > 0f)
                        {
                            float tanhArg = (tanMain * extra) / remaining;
                            baseH = hShoulder + remaining * (float)Math.Tanh(tanhArg);
                        }
                        else
                        {
                            baseH = mountainMaxHeight;
                        }
                    }

                    if (baseH > mountainMaxHeight) baseH = mountainMaxHeight;

                    float worldX = sx * 0.5f;
                    float nBroad = DeterministicNoise(worldX / noiseScale, worldZ / noiseScale, Seed);
                    float crag = nBroad;
                    if (d > footDist)
                    {
                        float interiorBlend = Math.Min(1f, (d - footDist) * 0.25f);
                        float nDetail = DeterministicNoise(worldX / (noiseScale * 0.45f) + 17.3f, worldZ / (noiseScale * 0.45f) + 31.7f, Seed ^ 0x5A391B);
                        float nRidge = 1f - Math.Abs(DeterministicNoise(worldX / (noiseScale * 0.65f) + 53.1f, worldZ / (noiseScale * 0.65f) + 11.9f, Seed ^ 0x2C7E91));
                        crag += (nDetail * 0.25f + (nRidge - 0.30f) * 0.45f) * interiorBlend;
                    }

                    float fade = d < footDist ? (d / footDist) : 1f;
                    float h = baseH + crag * noiseAmp * fade;

                    if (h < 0.05f) h = 0.05f;
                    if (h > mountainMaxHeight) h = mountainMaxHeight;
                    Heights[row + sx] = h;
                }
            }

            for (int p = 0; p < smoothingPasses; p++)
            {
                SmoothPass(dist, mountainMaxHeight);
            }

            LimitGradients(dist, tanMax, mountainMaxHeight);

            for (int i = 0; i < totalSamples; i++)
            {
                if (dist[i] <= 0f)
                {
                    Heights[i] = 0f;
                }
                else if (Heights[i] > mountainMaxHeight)
                {
                    Heights[i] = mountainMaxHeight;
                }
            }
        }

        // Mild smoothing of interior Euclidean distance field so medial-axis ridges round off naturally
        // without changing which samples are boundary (dist == 0 remains strictly 0).
        private void SoftenDistanceField(float[] dist)
        {
            float[] copy = new float[dist.Length];
            Array.Copy(dist, copy, dist.Length);

            int[] dx = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dz = { 0, 0, -1, 1, -1, 1, -1, 1 };
            float[] w = { 2f, 2f, 2f, 2f, 1f, 1f, 1f, 1f };

            for (int sz = 1; sz < SampleDepth - 1; sz++)
            {
                int row = sz * SampleWidth;
                for (int sx = 1; sx < SampleWidth - 1; sx++)
                {
                    int idx = row + sx;
                    float d0 = copy[idx];
                    // Only soften deeper medial-axis ridges (d > 3.5m) so small mounds keep full prominence
                    if (d0 <= 3.5f) continue;

                    float sum = d0 * 10f;
                    float weight = 10f;
                    for (int k = 0; k < 8; k++)
                    {
                        int nidx = (sz + dz[k]) * SampleWidth + (sx + dx[k]);
                        if (copy[nidx] <= 0f) continue;
                        sum += copy[nidx] * w[k];
                        weight += w[k];
                    }
                    float avg = sum / weight;
                    dist[idx] = avg > 0.05f ? avg : d0;
                }
            }
        }

        private void ComputeDistanceTransform(bool[] footprint, float[] dist)
        {
            int total = SampleWidth * SampleDepth;
            const int inf = 10000000;

            int[] nearestX = new int[total];
            int[] nearestZ = new int[total];

            for (int sz = 0; sz < SampleDepth; sz++)
            {
                int row = sz * SampleWidth;
                for (int sx = 0; sx < SampleWidth; sx++)
                {
                    int idx = row + sx;
                    if (IsInteriorSample(sx, sz, footprint))
                    {
                        nearestX[idx] = inf;
                        nearestZ[idx] = inf;
                    }
                    else
                    {
                        nearestX[idx] = sx;
                        nearestZ[idx] = sz;
                    }
                }
            }

            int[] fdx = { -1, 0, -1, 1 };
            int[] fdz = { 0, -1, -1, -1 };
            int[] bdx = { 1, 0, 1, -1 };
            int[] bdz = { 0, 1, 1, 1 };

            for (int pass = 0; pass < 2; pass++)
            {
                for (int sz = 0; sz < SampleDepth; sz++)
                {
                    int row = sz * SampleWidth;
                    for (int sx = 0; sx < SampleWidth; sx++)
                    {
                        int idx = row + sx;
                        int curNx = nearestX[idx];
                        int curNz = nearestZ[idx];
                        long bestDistSq = curNx >= inf ? long.MaxValue : DistSq(sx, sz, curNx, curNz);

                        for (int k = 0; k < 4; k++)
                        {
                            int px = sx + fdx[k];
                            int pz = sz + fdz[k];
                            if (px >= 0 && px < SampleWidth && pz >= 0 && pz < SampleDepth)
                            {
                                int pidx = pz * SampleWidth + px;
                                int nx = nearestX[pidx];
                                int nz = nearestZ[pidx];
                                if (nx < inf)
                                {
                                    long dSq = DistSq(sx, sz, nx, nz);
                                    if (dSq < bestDistSq)
                                    {
                                        bestDistSq = dSq;
                                        curNx = nx;
                                        curNz = nz;
                                    }
                                }
                            }
                        }

                        nearestX[idx] = curNx;
                        nearestZ[idx] = curNz;
                    }
                }

                for (int sz = SampleDepth - 1; sz >= 0; sz--)
                {
                    int row = sz * SampleWidth;
                    for (int sx = SampleWidth - 1; sx >= 0; sx--)
                    {
                        int idx = row + sx;
                        int curNx = nearestX[idx];
                        int curNz = nearestZ[idx];
                        long bestDistSq = curNx >= inf ? long.MaxValue : DistSq(sx, sz, curNx, curNz);

                        for (int k = 0; k < 4; k++)
                        {
                            int px = sx + bdx[k];
                            int pz = sz + bdz[k];
                            if (px >= 0 && px < SampleWidth && pz >= 0 && pz < SampleDepth)
                            {
                                int pidx = pz * SampleWidth + px;
                                int nx = nearestX[pidx];
                                int nz = nearestZ[pidx];
                                if (nx < inf)
                                {
                                    long dSq = DistSq(sx, sz, nx, nz);
                                    if (dSq < bestDistSq)
                                    {
                                        bestDistSq = dSq;
                                        curNx = nx;
                                        curNz = nz;
                                    }
                                }
                            }
                        }

                        nearestX[idx] = curNx;
                        nearestZ[idx] = curNz;

                        if (pass == 1)
                        {
                            if (curNx >= inf)
                            {
                                dist[idx] = 0f;
                            }
                            else
                            {
                                float dSamples = (float)Math.Sqrt(bestDistSq);
                                dist[idx] = dSamples * 0.5f;
                            }
                        }
                    }
                }
            }
        }

        private static long DistSq(int x1, int z1, int x2, int z2)
        {
            long dx = x1 - x2;
            long dz = z1 - z2;
            return dx * dx + dz * dz;
        }

        private bool IsInteriorSample(int sx, int sz, bool[] footprint)
        {
            if (sx <= 0 || sz <= 0 || sx >= SampleWidth - 1 || sz >= SampleDepth - 1)
                return false;

            int minCx = (sx - 1) / 2;
            int maxCx = sx / 2;
            int minCz = (sz - 1) / 2;
            int maxCz = sz / 2;

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                if (cz < 0 || cz >= Depth) return false;
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (cx < 0 || cx >= Width) return false;
                    if (!footprint[cz * Width + cx]) return false;
                }
            }
            return true;
        }

        private void SmoothPass(float[] dist, float maxH)
        {
            float[] temp = new float[Heights.Length];
            Array.Copy(Heights, temp, Heights.Length);

            int[] dx = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dz = { 0, 0, -1, 1, -1, 1, -1, 1 };
            float[] w = { 2f, 2f, 2f, 2f, 1f, 1f, 1f, 1f };

            for (int sz = 0; sz < SampleDepth; sz++)
            {
                int row = sz * SampleWidth;
                for (int sx = 0; sx < SampleWidth; sx++)
                {
                    int idx = row + sx;
                    if (dist[idx] <= 0f) continue;

                    float sum = temp[idx] * 4f;
                    float weight = 4f;

                    for (int k = 0; k < 8; k++)
                    {
                        int nx = sx + dx[k];
                        int nz = sz + dz[k];
                        if (nx >= 0 && nx < SampleWidth && nz >= 0 && nz < SampleDepth)
                        {
                            int nidx = nz * SampleWidth + nx;
                            if (dist[nidx] > 0f)
                            {
                                sum += temp[nidx] * w[k];
                                weight += w[k];
                            }
                        }
                    }

                    float smoothed = sum / weight;
                    if (smoothed > maxH) smoothed = maxH;
                    Heights[idx] = smoothed;
                }
            }
        }

        private void LimitGradients(float[] dist, float tanMax, float maxH)
        {
            float maxDeltaStraight = 0.5f * tanMax;
            float maxDeltaDiag = 0.5f * 1.41421356f * tanMax;

            int[] dx = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dz = { 0, 0, -1, 1, -1, 1, -1, 1 };
            float[] maxDelta = {
                maxDeltaStraight, maxDeltaStraight, maxDeltaStraight, maxDeltaStraight,
                maxDeltaDiag, maxDeltaDiag, maxDeltaDiag, maxDeltaDiag
            };

            for (int iter = 0; iter < 4; iter++)
            {
                for (int sz = 0; sz < SampleDepth; sz++)
                {
                    int row = sz * SampleWidth;
                    for (int sx = 0; sx < SampleWidth; sx++)
                    {
                        int idx = row + sx;
                        if (dist[idx] <= 0f) continue;
                        float h = Heights[idx];

                        for (int k = 0; k < 8; k++)
                        {
                            int nx = sx + dx[k];
                            int nz = sz + dz[k];
                            if (nx >= 0 && nx < SampleWidth && nz >= 0 && nz < SampleDepth)
                            {
                                int nidx = nz * SampleWidth + nx;
                                float limit = Heights[nidx] + maxDelta[k];
                                if (h > limit) h = limit;
                            }
                        }
                        if (h > maxH) h = maxH;
                        Heights[idx] = h;
                    }
                }

                for (int sz = SampleDepth - 1; sz >= 0; sz--)
                {
                    int row = sz * SampleWidth;
                    for (int sx = SampleWidth - 1; sx >= 0; sx--)
                    {
                        int idx = row + sx;
                        if (dist[idx] <= 0f) continue;
                        float h = Heights[idx];

                        for (int k = 0; k < 8; k++)
                        {
                            int nx = sx + dx[k];
                            int nz = sz + dz[k];
                            if (nx >= 0 && nx < SampleWidth && nz >= 0 && nz < SampleDepth)
                            {
                                int nidx = nz * SampleWidth + nx;
                                float limit = Heights[nidx] + maxDelta[k];
                                if (h > limit) h = limit;
                            }
                        }
                        if (h > maxH) h = maxH;
                        Heights[idx] = h;
                    }
                }
            }
        }

        private static float DeterministicNoise(float x, float z, int seed)
        {
            int ix = (int)Math.Floor(x);
            int iz = (int)Math.Floor(z);
            float fx = x - ix;
            float fz = z - iz;

            float u = fx * fx * (3f - 2f * fx);
            float v = fz * fz * (3f - 2f * fz);

            float n00 = Hash2D(ix, iz, seed);
            float n10 = Hash2D(ix + 1, iz, seed);
            float n01 = Hash2D(ix, iz + 1, seed);
            float n11 = Hash2D(ix + 1, iz + 1, seed);

            float nx0 = n00 + u * (n10 - n00);
            float nx1 = n01 + u * (n11 - n01);
            return nx0 + v * (nx1 - nx0);
        }

        private static float Hash2D(int x, int z, int seed)
        {
            uint h = (uint)seed ^ ((uint)x * 374761393u) ^ ((uint)z * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h = h ^ (h >> 16);
            return ((h & 0xFFFF) / 32767.5f) - 1f;
        }
    }
}
