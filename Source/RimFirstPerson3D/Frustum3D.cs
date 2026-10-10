// AI生成
using UnityEngine;

namespace RimFirstPerson3D
{
    // 每帧相机视锥缓存：把 6 个裁剪平面算一次，供各地理 chunk 用 AABB
    // 做屏幕外剔除（跳过 Graphics.DrawMesh 提交，省掉引擎逐 mesh 的剔除开销）。
    // 全 mod 默认走 Graphics.DrawMesh（无 MeshRenderer），引擎不做对象级剔除，
    // 因此这里补上这一层：屏幕外 / 超远（FarClip 之外）的 chunk 不再提交。
    public static class Frustum3D
    {
        private static readonly Plane[] planes = new Plane[6];
        private static Vector3 camPos;
        private static bool valid;

        // 供剔除的距离半径（世界单位）；比 FarClip 略收，避免贴边抖动。
        // 取 0 表示只用视锥（含相机 FarClip 远平面）判定，不再额外按距离剔。
        public static float MaxDist { get; set; }

        // 每帧更新一次（在绘制前调用，幂等）。
        public static void Update()
        {
            var cam = CameraRig3D.Cam;
            if (cam == null)
            {
                valid = false;
                return;
            }
            camPos = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            valid = true;
        }

        // 世界空间 AABB 是否可能可见。未初始化时一律返回 true（保守放行）。
        public static bool Visible(in Bounds bounds)
        {
            if (!valid) return true;
            if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return false;
            if (MaxDist > 0f)
            {
                // 相机到 AABB 最近点距离 > MaxDist 则剔除（用 AABB 半径做保守收缩）
                float d = Vector3.Distance(camPos, bounds.center) - bounds.extents.magnitude;
                if (d > MaxDist) return false;
            }
            return true;
        }

        // 逐件绘制用：给定世界坐标与半径，判断是否在视锥/距离内。
        // 未初始化时放行（保守），避免误剔。
        public static bool VisiblePoint(Vector3 worldPos, float radius)
        {
            if (!valid) return true;
            var b = new Bounds(worldPos, new Vector3(radius * 2f, radius * 2f, radius * 2f));
            return Visible(b);
        }
    }
}