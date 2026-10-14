using UnityEngine;

namespace RimFirstPerson3D
{
    public static class SkyMath
    {
        public static float Wrap01(float value)
        {
            float rem = value - Mathf.Floor(value);
            return rem >= 1f ? 0f : rem;
        }

        public static float SmoothStep01(float a, float b, float x)
        {
            if (Mathf.Approximately(a, b)) return x >= b ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static Vector3 SphereDirection(int longitudeIndex, int latitudeIndex, int longitudeSegments, int latitudeSegments, bool hemisphere)
        {
            if (latitudeSegments <= 0) latitudeSegments = 1;
            if (longitudeSegments <= 0) longitudeSegments = 1;

            if (latitudeIndex <= 0) return Vector3.up;
            if (!hemisphere && latitudeIndex >= latitudeSegments) return Vector3.down;

            float maxPhi = hemisphere ? (Mathf.PI * 0.5f) : Mathf.PI;
            float phi = (latitudeIndex / (float)latitudeSegments) * maxPhi;

            int lonMod = longitudeIndex % longitudeSegments;
            if (lonMod < 0) lonMod += longitudeSegments;
            float theta = (lonMod / (float)longitudeSegments) * (Mathf.PI * 2f);

            float sp = Mathf.Sin(phi);
            float cp = Mathf.Cos(phi);
            Vector3 dir = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta));
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        }

        public static Vector2 CloudUv(Vector3 direction, float scale)
        {
            Vector3 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            float denom = Mathf.Max(0f, d.y) + 0.32f;
            float inv = scale / denom;
            return new Vector2(d.x * inv, d.z * inv);
        }

        public static float CloudNoise(float u, float v)
        {
            float nu = Wrap01(u);
            float nv = Wrap01(v);

            float sum = 0f;
            float amp = 0.54f;
            float maxAmp = 0f;

            sum += PeriodicValueNoise(nu, nv, 4, 101) * amp;
            maxAmp += amp;
            amp *= 0.5f;

            sum += PeriodicValueNoise(nu, nv, 8, 211) * amp;
            maxAmp += amp;
            amp *= 0.5f;

            sum += PeriodicValueNoise(nu, nv, 16, 353) * amp;
            maxAmp += amp;
            amp *= 0.5f;

            sum += PeriodicValueNoise(nu, nv, 32, 577) * amp;
            maxAmp += amp;

            return maxAmp > 0f ? (sum / maxAmp) : 0.5f;
        }

        private static float PeriodicValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period;
            float y = v * period;

            int x0 = (int)Mathf.Floor(x);
            int y0 = (int)Mathf.Floor(y);
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;

            x0 = ((x0 % period) + period) % period;
            y0 = ((y0 % period) + period) % period;

            float fx = x - Mathf.Floor(x);
            float fy = y - Mathf.Floor(y);

            float tx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float ty = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

            float v00 = LatticeHash(x0, y0, seed);
            float v10 = LatticeHash(x1, y0, seed);
            float v01 = LatticeHash(x0, y1, seed);
            float v11 = LatticeHash(x1, y1, seed);

            float nx0 = Mathf.Lerp(v00, v10, tx);
            float nx1 = Mathf.Lerp(v01, v11, tx);
            return Mathf.Lerp(nx0, nx1, ty);
        }

        private static float LatticeHash(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 3628273;
                n = (n ^ (n >> 13)) * 1274126177;
                n = n ^ (n >> 16);
                return (n & 0x7fffffff) / (float)0x7fffffff;
            }
        }
    }
}
