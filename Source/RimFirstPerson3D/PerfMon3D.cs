// AI生成
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 性能监控：帧率/帧耗时/GC/绘制计数，HUD 与文件双输出。
    [StaticConstructorOnStartup]
    public static class PerfMon3D
    {
        private const int FrameWindow = 120;
        private static readonly float[] frameMs = new float[FrameWindow];
        private static int frameIdx;
        private static int framesCounted;
        private static float windowTimer;
        private static float fpsResult, avgMs, maxMs, p95Ms;
        private static long lastGcBytes;
        private static float gcMbPerSec;
        private static float gcTimer;
        private static float fileTimer;
        private static readonly StringBuilder sb = new StringBuilder(512);

        private static GUIStyle hudStyle;
        private static Texture2D hudBg;

        public static void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float ms = dt * 1000f;
            frameMs[frameIdx] = ms;
            frameIdx = (frameIdx + 1) % FrameWindow;
            framesCounted++;
            windowTimer += dt;
            if (windowTimer >= 1f)
            {
                fpsResult = framesCounted / windowTimer;
                int n = Mathf.Min(framesCounted, FrameWindow);
                float sum = 0f, mx = 0f;
                for (int i = 0; i < n; i++)
                {
                    float v = frameMs[i];
                    sum += v;
                    if (v > mx) mx = v;
                }
                avgMs = sum / n;
                maxMs = mx;
                // p95
                int take = Mathf.Max(1, (int)(n * 0.95f));
                float[] copy = new float[n];
                System.Array.Copy(frameMs, copy, n);
                System.Array.Sort(copy);
                p95Ms = copy[Mathf.Clamp(take, 0, n - 1)];
                windowTimer = 0f;
                framesCounted = 0;
            }

            gcTimer += dt;
            if (gcTimer >= 2f)
            {
                long now = System.GC.GetTotalMemory(false);
                if (lastGcBytes > 0)
                {
                    gcMbPerSec = (now - lastGcBytes) / (1024f * 1024f) / gcTimer;
                }
                lastGcBytes = now;
                gcTimer = 0f;
            }

            fileTimer += dt;
            if (fileTimer > 10f)
            {
                fileTimer = 0f;
                AppendFileLine();
            }
        }

        public static string SnapshotJson()
        {
            var st = FirstPerson3DMod.settings;
            return string.Format(
                "{{\"mode\":\"{0}\",\"fps\":{1:F1},\"avgMs\":{2:F2},\"p95Ms\":{3:F2},\"maxMs\":{4:F2}," +
                "\"gc\":{5:F2},\"terr\":{6},\"bld\":{7},\"hull\":{8},\"plant\":{9},\"plantB\":{10},\"item\":{11},\"itemB\":{12},\"pawnQ\":{13}," +
                "\"tb\":{14:F2},\"bb\":{15:F2},\"pb\":{16:F2},\"cam\":\"{17:F1};{18:F1};{19:F1}\",\"who\":\"{20}\"," +
                "\"fov\":{21:F0},\"fog\":\"{22};{23:F0}\",\"census\":\"{24}\"," +
                "\"tq\":{25},\"bq\":{26},\"animal\":{27}}}",
                Fps3DState.Mode.ToString(), fpsResult, avgMs, p95Ms, maxMs, gcMbPerSec,
                PerfCounters.TerrainMeshesDrawn, PerfCounters.BuildingMeshesDrawn, PerfCounters.OutlineMeshesDrawn,
                PerfCounters.PlantInstances, PerfCounters.PlantBatches, PerfCounters.ItemInstances, PerfCounters.ItemBatches, PerfCounters.PawnQuads,
                PerfCounters.LastTerrainBuildMs, PerfCounters.LastBuildingBuildMs, PerfCounters.LastPlantBuildMs,
                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                JsonEscape(CameraRig3D.PossessedInfo),
                st == null ? 72f : st.fov,
                st != null && st.fogOn ? "on" : "off",
                st == null ? 0f : st.fogEnd,
                JsonEscape(CameraRig3D.CameraCensus()),
                PerfCounters.TerrainQuads, PerfCounters.BuildingQuads, PerfCounters.AnimalMeshes);
        }

        // JSON 字符串转义（不含首尾引号，引号由格式串提供）：
        // 角色名/相机名可能含引号或反斜杠，直接拼接会破坏整段快照。
        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void AppendFileLine()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "FirstPerson3D_perf.log");
                File.AppendAllText(path, SnapshotJson() + "\n");
            }
            catch { }
        }

        public static void DrawHud()
        {
            if (!Fps3DState.ShowPerfHud) return;
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle();
                hudStyle.fontSize = 13;
                hudStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);
                hudStyle.fontStyle = FontStyle.Bold;
                hudStyle.alignment = TextAnchor.UpperLeft;
                hudStyle.wordWrap = false;
                hudStyle.padding = new RectOffset(8, 8, 6, 6);
                hudBg = new Texture2D(1, 1);
                hudBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
                hudBg.Apply();
                hudStyle.normal.background = hudBg;
            }
            sb.Length = 0;
            sb.Append("FPS ").Append(fpsResult.ToString("F1"));
            sb.Append("  avg ").Append(avgMs.ToString("F1")).Append("ms");
            sb.Append("  p95 ").Append(p95Ms.ToString("F1")).Append("ms");
            sb.Append("  max ").Append(maxMs.ToString("F1")).Append("ms");
            sb.Append("  GC ").Append(gcMbPerSec.ToString("F2")).Append(" MB/s\n");
            sb.Append("mode ").Append(Fps3DState.Mode);
            sb.Append("  chunks ").Append(PerfCounters.TerrainMeshesDrawn);
            sb.Append("  bld ").Append(PerfCounters.BuildingMeshesDrawn);
            sb.Append("  hull ").Append(PerfCounters.OutlineMeshesDrawn).Append('\n');
            sb.Append("plant ").Append(PerfCounters.PlantInstances).Append('/').Append(PerfCounters.PlantBatches);
            sb.Append("  item ").Append(PerfCounters.ItemInstances).Append('/').Append(PerfCounters.ItemBatches);
            sb.Append("  pawnQ ").Append(PerfCounters.PawnQuads).Append('\n');
            sb.Append("build ms  terr ").Append(PerfCounters.LastTerrainBuildMs.ToString("F2"));
            sb.Append("  bld ").Append(PerfCounters.LastBuildingBuildMs.ToString("F2"));
            sb.Append("  plant ").Append(PerfCounters.LastPlantBuildMs.ToString("F2"));

            var content = new GUIContent(sb.ToString());
            var size = hudStyle.CalcSize(content);
            var rect = new Rect(10f, 10f, size.x, size.y);
            GUI.Label(rect, content, hudStyle);
        }
    }
}
