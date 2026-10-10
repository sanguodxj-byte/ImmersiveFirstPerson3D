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
        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(32)
        {
            { "shelf.east.top", new Vector4(0.00781f, 0.77865f, 0.22266f, 0.99740f) },
            { "shelf.south.top", new Vector4(0.23828f, 0.93229f, 0.90234f, 0.99740f) },
            { "shelf.north.top", new Vector4(0.00781f, 0.71615f, 0.67188f, 0.77344f) },
            { "bed.south.top", new Vector4(0.68750f, 0.63021f, 0.92969f, 0.77344f) },
            { "bed.north.top", new Vector4(0.00781f, 0.48177f, 0.25000f, 0.62500f) },
            { "bed.east.top", new Vector4(0.26562f, 0.54688f, 0.69531f, 0.62500f) },
            { "dresser.east.top", new Vector4(0.71094f, 0.50521f, 0.96094f, 0.62500f) },
            { "dresser.south.top", new Vector4(0.00781f, 0.43490f, 0.50781f, 0.47656f) },
            { "dresser.south.front", new Vector4(0.00781f, 0.38802f, 0.50781f, 0.42969f) },
            { "dresser.north.top", new Vector4(0.00781f, 0.34115f, 0.50781f, 0.38281f) },
            { "dresser.north.front", new Vector4(0.00781f, 0.29427f, 0.50781f, 0.33594f) },
            { "shelf.south.front", new Vector4(0.00781f, 0.26953f, 0.67188f, 0.28906f) },
            { "shelf.north.front", new Vector4(0.00781f, 0.24479f, 0.67188f, 0.26432f) },
            { "dresser.east.front", new Vector4(0.68750f, 0.21745f, 0.93750f, 0.26432f) },
            { "shelf.south.wood", new Vector4(0.00781f, 0.16667f, 0.24219f, 0.21224f) },
            { "dresser.south.wood", new Vector4(0.25781f, 0.18099f, 0.45312f, 0.21224f) },
            { "stool.north.top", new Vector4(0.46875f, 0.17969f, 0.63672f, 0.21224f) },
            { "endtable.south.top", new Vector4(0.65234f, 0.18229f, 0.82031f, 0.21224f) },
            { "endtable.north.top", new Vector4(0.00781f, 0.13151f, 0.17578f, 0.16146f) },
            { "bed.south.front", new Vector4(0.19141f, 0.14193f, 0.43359f, 0.16146f) },
            { "bed.north.front", new Vector4(0.44922f, 0.14193f, 0.69141f, 0.16146f) },
            { "stool.east.top", new Vector4(0.70703f, 0.12240f, 0.81641f, 0.16146f) },
            { "endtable.east.top", new Vector4(0.83203f, 0.12500f, 0.94922f, 0.16146f) },
            { "bed.east.front", new Vector4(0.00781f, 0.10807f, 0.43750f, 0.11719f) },
            { "shelf.east.front", new Vector4(0.45312f, 0.10026f, 0.66797f, 0.11719f) },
            { "stool.north.wood", new Vector4(0.68359f, 0.09766f, 0.78125f, 0.11719f) },
            { "bed.south.wood", new Vector4(0.79688f, 0.10677f, 0.87500f, 0.11719f) },
            { "endtable.south.wood", new Vector4(0.89062f, 0.10417f, 0.95312f, 0.11719f) },
            { "endtable.south.front", new Vector4(0.00781f, 0.08464f, 0.07031f, 0.09245f) },
            { "endtable.north.front", new Vector4(0.08594f, 0.08464f, 0.14844f, 0.09245f) },
            { "stool.east.front", new Vector4(0.16406f, 0.06771f, 0.18359f, 0.09245f) },
            { "stool.north.front", new Vector4(0.19922f, 0.07031f, 0.21875f, 0.09245f) },
        };

        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(32)
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
            { "shelf.south.wood", new Vector2(60f, 35f) },
            { "dresser.south.wood", new Vector2(50f, 24f) },
            { "stool.north.top", new Vector2(43f, 25f) },
            { "endtable.south.top", new Vector2(43f, 23f) },
            { "endtable.north.top", new Vector2(43f, 23f) },
            { "bed.south.front", new Vector2(62f, 15f) },
            { "bed.north.front", new Vector2(62f, 15f) },
            { "stool.east.top", new Vector2(28f, 30f) },
            { "endtable.east.top", new Vector2(30f, 28f) },
            { "bed.east.front", new Vector2(110f, 7f) },
            { "shelf.east.front", new Vector2(55f, 13f) },
            { "stool.north.wood", new Vector2(25f, 15f) },
            { "bed.south.wood", new Vector2(20f, 8f) },
            { "endtable.south.wood", new Vector2(16f, 10f) },
            { "endtable.south.front", new Vector2(16f, 6f) },
            { "endtable.north.front", new Vector2(16f, 6f) },
            { "stool.east.front", new Vector2(5f, 19f) },
            { "stool.north.front", new Vector2(5f, 17f) },
        };
    }
}
