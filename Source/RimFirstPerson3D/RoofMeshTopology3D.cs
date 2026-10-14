using System;

namespace RimFirstPerson3D
{
    public static class RoofMeshTopology3D
    {
        public static bool IsCovered(bool[] covered, int width, int height, int x, int z)
        {
            if (covered == null || width <= 0 || height <= 0 || x < 0 || z < 0 || x >= width || z >= height)
                return false;
            return covered[z * width + x];
        }

        public static bool IsBoundaryEdge(bool[] covered, int width, int height, int x, int z, int dir)
        {
            if (!IsCovered(covered, width, height, x, z)) return false;
            switch (dir)
            {
                case 0: return !IsCovered(covered, width, height, x, z - 1);
                case 1: return !IsCovered(covered, width, height, x, z + 1);
                case 2: return !IsCovered(covered, width, height, x - 1, z);
                case 3: return !IsCovered(covered, width, height, x + 1, z);
                default: throw new ArgumentOutOfRangeException(nameof(dir));
            }
        }

        public static int BoundaryEdgeCount(bool[] covered, int width, int height)
        {
            if (covered == null || width <= 0 || height <= 0) return 0;
            int count = 0;
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    for (int dir = 0; dir < 4; dir++)
                        if (IsBoundaryEdge(covered, width, height, x, z, dir)) count++;
            return count;
        }
    }
}
