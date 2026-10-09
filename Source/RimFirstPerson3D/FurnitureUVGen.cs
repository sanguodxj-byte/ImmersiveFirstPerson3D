// 自动生成 —— 由 scripts/furn_atlas.py 产出，请勿手改。
// 键 = "<原型>.<朝向>.<区段>"，值 = 图集 UV (u0, v0, u1, v1)。
// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H。
using System.Collections.Generic;
using UnityEngine;

namespace RimFirstPerson3D
{
    public static class FurnitureUVGen
    {
        public const string TexPath = "FPS3D/FurnitureAtlas";
        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(27)
        {
            { "shelf.east.top", new Vector4(0.00781f, 0.79567f, 0.25000f, 0.99760f) },
            { "shelf.south.top", new Vector4(0.26562f, 0.93750f, 0.92969f, 0.99760f) },
            { "shelf.north.top", new Vector4(0.00781f, 0.73077f, 0.67188f, 0.79087f) },
            { "bed.east.top", new Vector4(0.00781f, 0.65385f, 0.50781f, 0.72596f) },
            { "bed.south.top", new Vector4(0.52344f, 0.59375f, 0.76562f, 0.72596f) },
            { "bed.north.top", new Vector4(0.00781f, 0.45673f, 0.25000f, 0.58894f) },
            { "dresser.east.top", new Vector4(0.26562f, 0.47837f, 0.51562f, 0.58894f) },
            { "dresser.south.top", new Vector4(0.00781f, 0.41346f, 0.50781f, 0.45192f) },
            { "dresser.south.front", new Vector4(0.00781f, 0.37019f, 0.50781f, 0.40865f) },
            { "dresser.north.top", new Vector4(0.00781f, 0.32692f, 0.50781f, 0.36538f) },
            { "dresser.north.front", new Vector4(0.00781f, 0.28365f, 0.50781f, 0.32212f) },
            { "shelf.south.front", new Vector4(0.00781f, 0.25361f, 0.67188f, 0.27885f) },
            { "shelf.north.front", new Vector4(0.00781f, 0.22356f, 0.67188f, 0.24880f) },
            { "dresser.east.front", new Vector4(0.68750f, 0.20553f, 0.93750f, 0.24880f) },
            { "endtable.south.front", new Vector4(0.00781f, 0.15745f, 0.17578f, 0.20072f) },
            { "endtable.north.front", new Vector4(0.19141f, 0.15745f, 0.35938f, 0.20072f) },
            { "bed.south.front", new Vector4(0.37500f, 0.17909f, 0.61719f, 0.20072f) },
            { "bed.north.front", new Vector4(0.63281f, 0.17909f, 0.87500f, 0.20072f) },
            { "stool.north.top", new Vector4(0.00781f, 0.12260f, 0.17578f, 0.15264f) },
            { "endtable.south.top", new Vector4(0.19141f, 0.12500f, 0.35938f, 0.15264f) },
            { "endtable.north.top", new Vector4(0.37500f, 0.12500f, 0.54297f, 0.15264f) },
            { "bed.east.front", new Vector4(0.00781f, 0.10938f, 0.50781f, 0.11779f) },
            { "stool.east.top", new Vector4(0.52344f, 0.08173f, 0.63281f, 0.11779f) },
            { "endtable.east.top", new Vector4(0.64844f, 0.08413f, 0.76562f, 0.11779f) },
            { "shelf.east.front", new Vector4(0.00781f, 0.06130f, 0.25000f, 0.07692f) },
            { "stool.east.front", new Vector4(0.26562f, 0.05409f, 0.28516f, 0.07692f) },
            { "stool.north.front", new Vector4(0.30078f, 0.05649f, 0.32031f, 0.07692f) },
        };

        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(27)
        {
            { "shelf.east.top", new Vector2(62f, 168f) },
            { "shelf.south.top", new Vector2(170f, 50f) },
            { "shelf.north.top", new Vector2(170f, 50f) },
            { "bed.east.top", new Vector2(128f, 60f) },
            { "bed.south.top", new Vector2(62f, 110f) },
            { "bed.north.top", new Vector2(62f, 110f) },
            { "dresser.east.top", new Vector2(64f, 92f) },
            { "dresser.south.top", new Vector2(128f, 32f) },
            { "dresser.south.front", new Vector2(128f, 32f) },
            { "dresser.north.top", new Vector2(128f, 32f) },
            { "dresser.north.front", new Vector2(128f, 32f) },
            { "shelf.south.front", new Vector2(170f, 21f) },
            { "shelf.north.front", new Vector2(170f, 21f) },
            { "dresser.east.front", new Vector2(64f, 36f) },
            { "endtable.south.front", new Vector2(43f, 36f) },
            { "endtable.north.front", new Vector2(43f, 36f) },
            { "bed.south.front", new Vector2(62f, 18f) },
            { "bed.north.front", new Vector2(62f, 18f) },
            { "stool.north.top", new Vector2(43f, 25f) },
            { "endtable.south.top", new Vector2(43f, 23f) },
            { "endtable.north.top", new Vector2(43f, 23f) },
            { "bed.east.front", new Vector2(128f, 7f) },
            { "stool.east.top", new Vector2(28f, 30f) },
            { "endtable.east.top", new Vector2(30f, 28f) },
            { "shelf.east.front", new Vector2(62f, 13f) },
            { "stool.east.front", new Vector2(5f, 19f) },
            { "stool.north.front", new Vector2(5f, 17f) },
        };
    }
}
