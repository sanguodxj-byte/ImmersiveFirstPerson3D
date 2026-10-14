// 自动生成 —— 由 scripts/wreck_atlas.py 产出，请勿手改。
// 键 = 残骸样板名（多视图残骸为 "<名>.<朝向>"），值 = 图集 UV (u0, v0, u1, v1)。
// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H。
// 源贴图：docs/wreckage/clean/*.png（已程序去黑边）。
using System.Collections.Generic;
using UnityEngine;

namespace RimFirstPerson3D
{
    public static class WreckageUVGen
    {
        public const string TexPath = "FPS3D/WreckageAtlas";
        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(9)
        {
            { "ancientwarwalkershell", new Vector4(0.00000f, 0.00000f, 0.30078f, 0.11902f) },
            { "concretebarrier", new Vector4(0.52539f, 0.08045f, 0.58203f, 0.11902f) },
            { "crashedshippart", new Vector4(0.00000f, 0.68617f, 0.97266f, 1.00000f) },
            { "giantwheela", new Vector4(0.30273f, 0.04189f, 0.40625f, 0.11902f) },
            { "largeengineblock", new Vector4(0.40820f, 0.08045f, 0.52344f, 0.11902f) },
            { "ruinedtank", new Vector4(0.41113f, 0.27726f, 0.70898f, 0.39694f) },
            { "rustedindustrialtruck.east", new Vector4(0.39844f, 0.40093f, 0.81543f, 0.68484f) },
            { "rustedindustrialtruck.north", new Vector4(0.00000f, 0.39827f, 0.39648f, 0.68484f) },
            { "rustedindustrialtruck.south", new Vector4(0.00000f, 0.12035f, 0.40918f, 0.39694f) },
        };

        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(9)
        {
            { "ancientwarwalkershell", new Vector2(308f, 179f) },
            { "concretebarrier", new Vector2(58f, 58f) },
            { "crashedshippart", new Vector2(996f, 472f) },
            { "giantwheela", new Vector2(106f, 116f) },
            { "largeengineblock", new Vector2(118f, 58f) },
            { "ruinedtank", new Vector2(305f, 180f) },
            { "rustedindustrialtruck.east", new Vector2(427f, 427f) },
            { "rustedindustrialtruck.north", new Vector2(406f, 431f) },
            { "rustedindustrialtruck.south", new Vector2(419f, 416f) },
        };
    }
}
