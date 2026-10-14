// AI生成
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 往上帝相机的 RT 里画界面。坐标是纹理像素、左上为原点、y 向下。
    // Graphics.DrawTexture 只能在 OnGUI 里用，这里用 GL 直接写当前 RT。
    [StaticConstructorOnStartup]
    static class PanelDraw3D
    {
        static RenderTexture rt;
        static RenderTexture prevActive;
        static bool begun;
        static Material blit;
        static Material textMat;
        static Font textFont;
        static TextGenerator gen;
        static readonly StringBuilder plain = new StringBuilder(128);
        static readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>(32);

        public static float Width => rt != null ? rt.width : 0f;
        public static float Height => rt != null ? rt.height : 0f;

        public static void Begin(RenderTexture target)
        {
            if (begun) End();
            rt = target;
            begun = true;
            EnsureBlit();
            prevActive = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.Viewport(new Rect(0f, 0f, target.width, target.height));
            GL.LoadPixelMatrix(0f, target.width, 0f, target.height);
        }

        public static void End()
        {
            if (!begun) return;
            begun = false;
            GL.PopMatrix();
            RenderTexture.active = prevActive;
            rt = null;
        }

        public static void Fill(Rect top, Color color)
        {
            if (color.a < 0.02f) return;
            // 玩家里这份 GUI 着色器不乘顶点色，白贴图乘不上色就变成纯白。颜色写进贴图。
            Tex(top, SolidTex(color), new Rect(0f, 0f, 1f, 1f), Color.white);
        }

        public static void Box(Rect top, Color fill, Color border)
        {
            Fill(top, fill);
            Fill(new Rect(top.x, top.y, top.width, 1f), border);
            Fill(new Rect(top.x, top.yMax - 1f, top.width, 1f), border);
            Fill(new Rect(top.x, top.y, 1f, top.height), border);
            Fill(new Rect(top.xMax - 1f, top.y, 1f, top.height), border);
        }

        // 原版按钮图集：四角各 1/4，和 Widgets.DrawAtlas 同一套 UV。
        public static void Atlas(Rect top, Texture2D atlas)
        {
            if (atlas == null) return;
            float s = Mathf.Min(atlas.width * 0.25f, top.height * 0.5f, top.width * 0.5f);
            if (s < 1f)
            {
                Tex(top, atlas, new Rect(0f, 0f, 1f, 1f), Color.white);
                return;
            }
            Part(new Rect(top.x, top.y, s, s), new Rect(0f, 0f, 0.25f, 0.25f), atlas);
            Part(new Rect(top.xMax - s, top.y, s, s), new Rect(0.75f, 0f, 0.25f, 0.25f), atlas);
            Part(new Rect(top.x, top.yMax - s, s, s), new Rect(0f, 0.75f, 0.25f, 0.25f), atlas);
            Part(new Rect(top.xMax - s, top.yMax - s, s, s), new Rect(0.75f, 0.75f, 0.25f, 0.25f), atlas);
            Part(new Rect(top.x + s, top.y, top.width - s * 2f, s), new Rect(0.25f, 0f, 0.5f, 0.25f), atlas);
            Part(new Rect(top.x + s, top.yMax - s, top.width - s * 2f, s), new Rect(0.25f, 0.75f, 0.5f, 0.25f), atlas);
            Part(new Rect(top.x, top.y + s, s, top.height - s * 2f), new Rect(0f, 0.25f, 0.25f, 0.5f), atlas);
            Part(new Rect(top.xMax - s, top.y + s, s, top.height - s * 2f), new Rect(0.75f, 0.25f, 0.25f, 0.5f), atlas);
            Part(new Rect(top.x + s, top.y + s, top.width - s * 2f, top.height - s * 2f), new Rect(0.25f, 0.25f, 0.5f, 0.5f), atlas);
        }

        public static void Tex(Rect top, Texture tex, Rect uv, Color color)
        {
            if (rt == null || tex == null || blit == null || top.width < 0.5f || top.height < 0.5f) return;
            var bl = ToBottomLeft(top);
            blit.mainTexture = tex;
            blit.color = Color.white;
            blit.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            Vert(bl.x, bl.y, uv.x, uv.y);
            Vert(bl.xMax, bl.y, uv.xMax, uv.y);
            Vert(bl.xMax, bl.yMax, uv.xMax, uv.yMax);
            Vert(bl.x, bl.yMax, uv.x, uv.yMax);
            GL.End();
        }

        public static void Label(Rect top, string text, GameFont font, TextAnchor anchor, Color color, bool wrap)
        {
            if (rt == null || text.NullOrEmpty()) return;
            var style = Text.fontStyles[(int)font];
            var f = style != null ? style.font : null;
            if (f == null) return;
            string shown = Plain(text);
            if (shown.Length == 0) return;
            int size = style.fontSize > 0 ? style.fontSize : f.fontSize;
            if (size <= 0) size = font == GameFont.Tiny ? 12 : 16;
            f.RequestCharactersInTexture(shown, size, style.fontStyle);
            if (gen == null) gen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = f,
                color = color,
                fontSize = size,
                fontStyle = style.fontStyle,
                lineSpacing = 1f,
                richText = false,
                scaleFactor = 1f,
                textAnchor = anchor,
                alignByGeometry = false,
                resizeTextForBestFit = false,
                horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                verticalOverflow = wrap ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow,
                generationExtents = new Vector2(Mathf.Max(4f, top.width), Mathf.Max(4f, top.height)),
                pivot = Vector2.zero,
                updateBounds = false
            };
            gen.Populate(shown, settings);
            if (textFont != f || textMat == null)
            {
                textFont = f;
                textMat = new Material(f.material);
                textMat.SetInt("_ZWrite", 0);
                textMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            var atlas = f.material != null ? f.material.mainTexture : null;
            if (atlas == null) return;
            textMat.mainTexture = atlas;
            textMat.color = Color.white;
            textMat.SetPass(0);
            var bl = ToBottomLeft(top);
            var verts = gen.verts;
            GL.Begin(GL.QUADS);
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                Quad(verts[i], bl, color);
                Quad(verts[i + 1], bl, color);
                Quad(verts[i + 2], bl, color);
                Quad(verts[i + 3], bl, color);
            }
            GL.End();
        }

        static void Part(Rect top, Rect guiUv, Texture2D atlas)
        {
            var uv = guiUv;
            uv.y = 1f - uv.y - uv.height;
            Tex(top, atlas, uv, Color.white);
        }

        static Rect ToBottomLeft(Rect top)
        {
            return new Rect(top.x, rt.height - top.y - top.height, top.width, top.height);
        }

        static void Vert(float x, float y, float u, float v)
        {
            GL.TexCoord2(u, v);
            GL.Vertex3(x, y, 0f);
        }

        static void Quad(UIVertex v, Rect bl, Color tint)
        {
            var c = (Color)v.color * tint;
            GL.Color(c);
            GL.TexCoord2(v.uv0.x, v.uv0.y);
            GL.Vertex3(bl.x + v.position.x, bl.y + v.position.y, 0f);
        }

        static Texture2D SolidTex(Color color)
        {
            var k = (Color32)color;
            int key = k.r | (k.g << 8) | (k.b << 16) | (k.a << 24);
            if (solids.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Fps3D_PanelSolid",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixel(0, 0, color);
            tex.Apply(false, true);
            solids[key] = tex;
            return tex;
        }

        static string Plain(string s)
        {
            int cut = s.IndexOf('<');
            if (cut < 0) return s;
            plain.Length = 0;
            bool skip = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') skip = true;
                else if (c == '>') skip = false;
                else if (!skip) plain.Append(c);
            }
            return plain.ToString();
        }

        static void EnsureBlit()
        {
            if (blit != null) return;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-GUITexture");
            if (shader == null) return;
            blit = new Material(shader) { name = "Fps3D_PanelBlit" };
            blit.SetInt("_ZWrite", 0);
            blit.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
    }
}
