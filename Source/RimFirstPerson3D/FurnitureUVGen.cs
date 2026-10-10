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
        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(45)
        {
            { "shelf.east.top", new Vector4(0.00781f, 0.81027f, 0.22266f, 0.99777f) },
            { "shelf.south.top", new Vector4(0.23828f, 0.94196f, 0.90234f, 0.99777f) },
            { "shelf.north.top", new Vector4(0.00781f, 0.75670f, 0.67188f, 0.80580f) },
            { "bed.south.top", new Vector4(0.68750f, 0.68304f, 0.92969f, 0.80580f) },
            { "bed.north.top", new Vector4(0.00781f, 0.55580f, 0.25000f, 0.67857f) },
            { "bed.east.top", new Vector4(0.26562f, 0.61161f, 0.69531f, 0.67857f) },
            { "dresser.east.top", new Vector4(0.71094f, 0.57589f, 0.96094f, 0.67857f) },
            { "dresser.south.top", new Vector4(0.00781f, 0.51562f, 0.50781f, 0.55134f) },
            { "dresser.south.front", new Vector4(0.00781f, 0.47545f, 0.50781f, 0.51116f) },
            { "dresser.north.top", new Vector4(0.00781f, 0.43527f, 0.50781f, 0.47098f) },
            { "dresser.north.front", new Vector4(0.00781f, 0.39509f, 0.50781f, 0.43080f) },
            { "armchair.east.top", new Vector4(0.52344f, 0.36830f, 0.80469f, 0.43080f) },
            { "armchair.north.back", new Vector4(0.00781f, 0.31473f, 0.32812f, 0.36384f) },
            { "armchair.south.front", new Vector4(0.34375f, 0.32031f, 0.67578f, 0.36384f) },
            { "shelf.south.front", new Vector4(0.00781f, 0.29353f, 0.67188f, 0.31027f) },
            { "shelf.north.front", new Vector4(0.00781f, 0.27232f, 0.67188f, 0.28906f) },
            { "dresser.east.front", new Vector4(0.68750f, 0.24888f, 0.93750f, 0.28906f) },
            { "armchair.south.wood", new Vector4(0.00781f, 0.20424f, 0.24219f, 0.24442f) },
            { "shelf.south.wood", new Vector4(0.25781f, 0.20536f, 0.49219f, 0.24442f) },
            { "armchair.south.back", new Vector4(0.50781f, 0.21540f, 0.80078f, 0.24442f) },
            { "armchair.north.wood", new Vector4(0.00781f, 0.16406f, 0.24219f, 0.19978f) },
            { "armchair.south.top", new Vector4(0.25781f, 0.16741f, 0.51172f, 0.19978f) },
            { "armchair.north.top", new Vector4(0.52734f, 0.18192f, 0.87891f, 0.19978f) },
            { "dresser.south.wood", new Vector4(0.00781f, 0.13281f, 0.20312f, 0.15960f) },
            { "stool.north.top", new Vector4(0.21875f, 0.13170f, 0.38672f, 0.15960f) },
            { "endtable.south.top", new Vector4(0.40234f, 0.13393f, 0.57031f, 0.15960f) },
            { "endtable.north.top", new Vector4(0.58594f, 0.13393f, 0.75391f, 0.15960f) },
            { "bed.south.front", new Vector4(0.00781f, 0.11049f, 0.25000f, 0.12723f) },
            { "bed.north.front", new Vector4(0.26562f, 0.11049f, 0.50781f, 0.12723f) },
            { "stool.east.top", new Vector4(0.52344f, 0.09375f, 0.63281f, 0.12723f) },
            { "endtable.east.top", new Vector4(0.64844f, 0.09598f, 0.76562f, 0.12723f) },
            { "bed.east.front", new Vector4(0.00781f, 0.08147f, 0.43750f, 0.08929f) },
            { "shelf.east.front", new Vector4(0.45312f, 0.07478f, 0.66797f, 0.08929f) },
            { "armchair.east.arml", new Vector4(0.68359f, 0.06250f, 0.78125f, 0.08929f) },
            { "armchair.east.wood", new Vector4(0.79688f, 0.07366f, 0.95312f, 0.08929f) },
            { "stool.north.wood", new Vector4(0.00781f, 0.04129f, 0.10547f, 0.05804f) },
            { "armchair.east.back", new Vector4(0.12109f, 0.04129f, 0.17578f, 0.05804f) },
            { "bed.south.wood", new Vector4(0.19141f, 0.04911f, 0.26953f, 0.05804f) },
            { "endtable.south.wood", new Vector4(0.28516f, 0.04688f, 0.34766f, 0.05804f) },
            { "armchair.north.armr", new Vector4(0.36328f, 0.05022f, 0.42578f, 0.05804f) },
            { "armchair.north.arml", new Vector4(0.44141f, 0.05022f, 0.50000f, 0.05804f) },
            { "endtable.south.front", new Vector4(0.51562f, 0.05134f, 0.57812f, 0.05804f) },
            { "endtable.north.front", new Vector4(0.59375f, 0.05134f, 0.65625f, 0.05804f) },
            { "stool.east.front", new Vector4(0.67188f, 0.03683f, 0.69141f, 0.05804f) },
            { "stool.north.front", new Vector4(0.70703f, 0.03906f, 0.72656f, 0.05804f) },
        };

        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>
        public static readonly Dictionary<string, Vector2> SrcSize =
            new Dictionary<string, Vector2>(45)
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
            { "armchair.east.top", new Vector2(72f, 56f) },
            { "armchair.north.back", new Vector2(82f, 44f) },
            { "armchair.south.front", new Vector2(85f, 39f) },
            { "shelf.south.front", new Vector2(170f, 15f) },
            { "shelf.north.front", new Vector2(170f, 15f) },
            { "dresser.east.front", new Vector2(64f, 36f) },
            { "armchair.south.wood", new Vector2(60f, 36f) },
            { "shelf.south.wood", new Vector2(60f, 35f) },
            { "armchair.south.back", new Vector2(75f, 26f) },
            { "armchair.north.wood", new Vector2(60f, 32f) },
            { "armchair.south.top", new Vector2(65f, 29f) },
            { "armchair.north.top", new Vector2(90f, 16f) },
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
            { "armchair.east.arml", new Vector2(25f, 24f) },
            { "armchair.east.wood", new Vector2(40f, 14f) },
            { "stool.north.wood", new Vector2(25f, 15f) },
            { "armchair.east.back", new Vector2(14f, 15f) },
            { "bed.south.wood", new Vector2(20f, 8f) },
            { "endtable.south.wood", new Vector2(16f, 10f) },
            { "armchair.north.armr", new Vector2(16f, 7f) },
            { "armchair.north.arml", new Vector2(15f, 7f) },
            { "endtable.south.front", new Vector2(16f, 6f) },
            { "endtable.north.front", new Vector2(16f, 6f) },
            { "stool.east.front", new Vector2(5f, 19f) },
            { "stool.north.front", new Vector2(5f, 17f) },
        };
    }
}
