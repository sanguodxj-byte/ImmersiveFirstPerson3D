using UnityEngine;

namespace RimFirstPerson3D
{
    // F016 整帧层的唯一入口（Fps3DRoot 在所有地图场景画完后调一次）。
    // 顺序：泛光相机就位 → 屏幕状态（水下/室内外）→ 大气 → 天气 → 眩光 → 镜面反射 → 泛光合成。
    // 各层自己判开关、自己计时（Shaderpack3D.Begin/End）。
    public static class ShaderpackFrame3D
    {
        public static void Draw(Camera cam)
        {
            if (Shaderpack3D.Tier == ShaderTier.Off)
            {
                Atmosphere3D.ReleaseEngineFog();
                return;
            }
            ScreenFx3D.Tick(cam);          // L9：先定水下（雾密度 ×4）与室内外跳变
            Atmosphere3D.Draw(cam);        // L5
            Weather3D.Draw(cam);           // L7
            Flares3D.Draw();               // L4-A
            WaterFx3D.RenderMirror(cam);   // L6-A：本帧所有几何提交后渲反射，下一帧叠到水面
            Bloom3D.RenderAndComposite(cam);
        }

        /// <summary>退出三维时还原 F016 动过的全局状态（引擎雾等）。</summary>
        public static void Restore()
        {
            Atmosphere3D.Restore();
            WaterFx3D.Reset();
            Shadows3D.Clear();
            Shaderpack3D.ResetSession();
            Bloom3D.ResetSession();
        }
    }
}
