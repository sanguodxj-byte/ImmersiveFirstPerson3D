// AI生成
// 自动生成 —— 由 scripts/wall_atlas.py 产出，请勿手改。
// 键 = "<外观>.<区段>"（区段 top=墙帽 / side=立面），值 = 图集 UV (u0, v0, u1, v1)。
// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H；始终 v0 < v1。
using System.Collections.Generic;
using UnityEngine;

namespace RimFirstPerson3D
{
    public static class WallUVGen
    {
        public const string TexPath = "FPS3D/WallAtlas";
        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(6)
        {
            { "bricks.top", new Vector4(0.00781f, 0.59375f, 0.24219f, 0.96875f) },
            { "planks.top", new Vector4(0.25781f, 0.59375f, 0.49219f, 0.96875f) },
            { "smooth.top", new Vector4(0.50781f, 0.59375f, 0.74219f, 0.96875f) },
            { "bricks.side", new Vector4(0.75781f, 0.65625f, 0.99219f, 0.96875f) },
            { "planks.side", new Vector4(0.00781f, 0.21875f, 0.24219f, 0.53125f) },
            { "smooth.side", new Vector4(0.25781f, 0.21875f, 0.49219f, 0.53125f) },
        };

        /// <summary>取样区的源像素尺寸（宽, 高）。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(6)
        {
            { "bricks.top", new Vector2(60f, 24f) },
            { "planks.top", new Vector2(60f, 24f) },
            { "smooth.top", new Vector2(60f, 24f) },
            { "bricks.side", new Vector2(60f, 20f) },
            { "planks.side", new Vector2(60f, 20f) },
            { "smooth.side", new Vector2(60f, 20f) },
        };
    }
}
