// AI生成
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
            { "shelf.east.top", new Vector4(0.00781f, 0.75852f, 0.22266f, 0.99716f) },
            { "shelf.south.top", new Vector4(0.23828f, 0.92614f, 0.90234f, 0.99716f) },
            { "shelf.north.top", new Vector4(0.00781f, 0.69034f, 0.67188f, 0.75284f) },
            { "bed.south.top", new Vector4(0.68750f, 0.59659f, 0.92969f, 0.75284f) },
            { "bed.north.top", new Vector4(0.00781f, 0.43466f, 0.25000f, 0.59091f) },
            { "bed.east.top", new Vector4(0.26562f, 0.50568f, 0.69531f, 0.59091f) },
            { "dresser.east.top", new Vector4(0.71094f, 0.46023f, 0.96094f, 0.59091f) },
            { "dresser.south.top", new Vector4(0.00781f, 0.38352f, 0.50781f, 0.42898f) },
            { "dresser.south.front", new Vector4(0.00781f, 0.33239f, 0.50781f, 0.37784f) },
            { "dresser.north.top", new Vector4(0.00781f, 0.28125f, 0.50781f, 0.32670f) },
            { "dresser.north.front", new Vector4(0.00781f, 0.23011f, 0.50781f, 0.27557f) },
            { "shelf.south.front", new Vector4(0.00781f, 0.20312f, 0.67188f, 0.22443f) },
            { "shelf.north.front", new Vector4(0.00781f, 0.17614f, 0.67188f, 0.19744f) },
            { "dresser.east.front", new Vector4(0.68750f, 0.14631f, 0.93750f, 0.19744f) },
            { "stool.north.top", new Vector4(0.00781f, 0.10511f, 0.17578f, 0.14062f) },
            { "endtable.south.top", new Vector4(0.19141f, 0.10795f, 0.35938f, 0.14062f) },
            { "endtable.north.top", new Vector4(0.37500f, 0.10795f, 0.54297f, 0.14062f) },
            { "bed.south.front", new Vector4(0.55859f, 0.11932f, 0.80078f, 0.14062f) },
            { "bed.north.front", new Vector4(0.00781f, 0.07812f, 0.25000f, 0.09943f) },
            { "stool.east.top", new Vector4(0.26562f, 0.05682f, 0.37500f, 0.09943f) },
            { "endtable.east.top", new Vector4(0.39062f, 0.05966f, 0.50781f, 0.09943f) },
            { "bed.east.front", new Vector4(0.52344f, 0.08949f, 0.95312f, 0.09943f) },
            { "shelf.east.front", new Vector4(0.00781f, 0.03267f, 0.22266f, 0.05114f) },
            { "endtable.south.front", new Vector4(0.23828f, 0.04261f, 0.30078f, 0.05114f) },
            { "endtable.north.front", new Vector4(0.31641f, 0.04261f, 0.37891f, 0.05114f) },
            { "stool.east.front", new Vector4(0.39453f, 0.02415f, 0.41406f, 0.05114f) },
            { "stool.north.front", new Vector4(0.42969f, 0.02699f, 0.44922f, 0.05114f) },
        };

        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(27)
        {
            { "shelf.east.top", new Vector2(55f, 168f) },
            { "shelf.south.top", new Vector2(170f, 50f) },
            { "shelf.north.top", new Vector2(170f, 44f) },
            { "bed.south.top", new Vector2(62f, 110f) },
            { "bed.north.top", new Vector2(62f, 110f) },
            { "bed.east.top", new Vector2(110f, 60f) },
            { "dresser.east.top", new Vector2(64f, 92f) },
            { "dresser.south.top", new Vector2(128f, 32f) },
            { "dresser.south.front", new Vector2(128f, 32f) },
            { "dresser.north.top", new Vector2(128f, 32f) },
            { "dresser.north.front", new Vector2(128f, 32f) },
            { "shelf.south.front", new Vector2(170f, 15f) },
            { "shelf.north.front", new Vector2(170f, 15f) },
            { "dresser.east.front", new Vector2(64f, 36f) },
            { "stool.north.top", new Vector2(43f, 25f) },
            { "endtable.south.top", new Vector2(43f, 23f) },
            { "endtable.north.top", new Vector2(43f, 23f) },
            { "bed.south.front", new Vector2(62f, 15f) },
            { "bed.north.front", new Vector2(62f, 15f) },
            { "stool.east.top", new Vector2(28f, 30f) },
            { "endtable.east.top", new Vector2(30f, 28f) },
            { "bed.east.front", new Vector2(110f, 7f) },
            { "shelf.east.front", new Vector2(55f, 13f) },
            { "endtable.south.front", new Vector2(16f, 6f) },
            { "endtable.north.front", new Vector2(16f, 6f) },
            { "stool.east.front", new Vector2(5f, 19f) },
            { "stool.north.front", new Vector2(5f, 17f) },
        };
    }
}
