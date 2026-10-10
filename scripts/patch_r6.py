# -*- coding: utf-8 -*-
# r6: 相机普查诊断 + 自由相机设置
import io

def rd(p): return io.open(p, encoding='utf-8').read()
def wr(p, s): io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

# 1) 设置面板加自由相机
s = rd(base + r'\ModSettings3D.cs')
old = """        public float camYaw = 90f;
        public float camPitch = -8f;"""
new = """        public float camYaw = 90f;
        public float camPitch = -8f;
        public float camX = 0f;
        public float camY = 0f;
        public float camZ = 0f;
        public bool freeCam = false;"""
assert old in s
s = s.replace(old, new)
old = """            Scribe_Values.Look(ref camPitch, "camPitch", -8f);"""
new = """            Scribe_Values.Look(ref camPitch, "camPitch", -8f);
            Scribe_Values.Look(ref camX, "camX", 0f);
            Scribe_Values.Look(ref camY, "camY", 0f);
            Scribe_Values.Look(ref camZ, "camZ", 0f);
            Scribe_Values.Look(ref freeCam, "freeCam", false);"""
assert old in s
s = s.replace(old, new)
wr(base + r'\ModSettings3D.cs', s)
print("settings ok")

# 2) CameraRig: 自由相机支持 + 相机普查日志
c = rd(base + r'\CameraRig3D.cs')
old = """            var dp = Possessed.DrawPos;
            var anchor = new Vector3(dp.x, 0f, dp.z);
            Vector3 eye;
            float yaw = Yaw, pitch = Pitch;"""
new = """            var st = FirstPerson3DMod.settings;
            float yaw = Yaw, pitch = Pitch;
            Vector3 anchor, eye;
            if (st != null && st.freeCam)
            {
                anchor = new Vector3(st.camX, 0f, st.camZ);
                eye = new Vector3(st.camX, st.camY, st.camZ);
                camGO.transform.position = eye;
                camGO.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                LastCamPos = eye;
                DrawNoseQuad(eye, pitch, yaw);
                SyncFollow(eye);
                return;
            }
            var dp = Possessed.DrawPos;
            anchor = new Vector3(dp.x, 0f, dp.z);"""
assert old in c
c = c.replace(old, new)

old = """            // 诊断：鼻尖白色面片（相机正前方 0.5m），用于验证相机渲染管线
            var noseRot = Quaternion.Euler(pitch, yaw, 0f);
            var noseMatrix = Matrix4x4.TRS(eye + noseRot * Vector3.forward * 0.5f, noseRot,
                new Vector3(0.4f, 0.4f, 1f));
            Graphics.DrawMesh(Util3D.BillboardQuad, noseMatrix, Util3D.CutoutMatFor(Util3D.WhiteTex), 0);

            // 原版相机的 AudioListener 与视域计算跟随玩家
            syncTimer += dt;
            if (syncTimer > 0.5f)
            {
                syncTimer = 0f;
                VanillaSuppressor.SyncVanillaViewCenter(anchor);
                var vanillaCamGo = GameObject.Find("Camera");
                if (vanillaCamGo != null) vanillaCamGo.transform.position = eye;
            }
        }"""
new = """            DrawNoseQuad(eye, pitch, yaw);
            SyncFollow(eye);
        }

        private void DrawNoseQuad(Vector3 eye, float pitch, float yaw)
        {
            var noseRot = Quaternion.Euler(pitch, yaw, 0f);
            var noseMatrix = Matrix4x4.TRS(eye + noseRot * Vector3.forward * 0.5f, noseRot,
                new Vector3(0.4f, 0.4f, 1f));
            Graphics.DrawMesh(Util3D.BillboardQuad, noseMatrix, Util3D.CutoutMatFor(Util3D.WhiteTex), 0);
        }

        private void SyncFollow(Vector3 eye)
        {
            syncTimer += Time.deltaTime;
            if (syncTimer > 0.5f)
            {
                syncTimer = 0f;
                VanillaSuppressor.SyncVanillaViewCenter(new Vector3(eye.x, 0f, eye.z));
                var vanillaCamGo = GameObject.Find("Camera");
                if (vanillaCamGo != null) vanillaCamGo.transform.position = eye;
            }
        }

        // 相机普查：枚举场景中所有 Camera 的关键状态。
        public static string CameraCensus()
        {
            var sb = new System.Text.StringBuilder();
            var cams = Object.FindObjectsOfType<Camera>();
            int i = 0;
            foreach (var cam in cams)
            {
                if (i++ > 0) sb.Append('|');
                sb.Append(cam.name).Append('/')
                  .Append(cam.enabled ? "on" : "off").Append('/')
                  .Append(cam.gameObject.activeInHierarchy ? "act" : "inact").Append('/')
                  .Append("d").Append(cam.depth.ToString("F0")).Append('/')
                  .Append(cam.orthographic ? "ortho" : "persp").Append('/')
                  .Append(cam.clearFlags.ToString()).Append('/')
                  .Append(((int)(cam.backgroundColor.r * 255)).ToString() + ","
                      + ((int)(cam.backgroundColor.g * 255)).ToString() + ","
                      + ((int)(cam.backgroundColor.b * 255)).ToString());
            }
            return sb.ToString();
        }"""
assert old in c
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("camera ok")

# 3) Fps3DRoot: 激活时打一次相机普查日志
f = rd(base + r'\Fps3DRoot.cs')
old = """            if (!suppressed)
            {
                suppressed = true;
                VanillaSuppressor.ApplyCameraSuppression();
                CameraRig3D.OnModeChanged();
            }"""
new = """            if (!suppressed)
            {
                suppressed = true;
                VanillaSuppressor.ApplyCameraSuppression();
                CameraRig3D.OnModeChanged();
                Log.Message(ModEntry.LogPrefix + "cameras-before: " + CameraRig3D.CameraCensus());
                if (CameraRig3D.Cam != null)
                {
                    Log.Message(ModEntry.LogPrefix + "mycam: enabled=" + CameraRig3D.Cam.enabled
                        + " act=" + CameraRig3D.Cam.gameObject.activeInHierarchy
                        + " depth=" + CameraRig3D.Cam.depth
                        + " mask=" + CameraRig3D.Cam.cullingMask);
                }
            }"""
assert old in f
f = f.replace(old, new)
wr(base + r'\Fps3DRoot.cs', f)
print("root ok")

# 4) 快照里带相机普查（截断到 300 字符）
p = rd(base + r'\PerfMon3D.cs')
old = """                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                CameraRig3D.PossessedInfo.Replace(',', ' '));"""
new = """                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                CameraRig3D.PossessedInfo.Replace(',', ' '))
                + ",\\"census\\":\\"" + (CameraRig3D.CameraCensus().Length > 300
                    ? CameraRig3D.CameraCensus().Substring(0, 300)
                    : CameraRig3D.CameraCensus()).Replace(',', ';').Replace('|', '.') + "\\"}}";"""
assert old in p
# 原串以 )); 结束，需调整
old_full = """                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                CameraRig3D.PossessedInfo.Replace(',', ' '));"""
new_full = """                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                CameraRig3D.PossessedInfo.Replace(',', ' '))
                + ",\\"census\\":\\"" + CameraRig3D.CameraCensus().Replace(',', ';').Replace('|', '.') + "\\"}}";"""
p = p.replace(old_full, new_full)
# 原来的格式串结尾是 ...{20}\"}}  —— census 拼接在 }} 之后需要去掉原有的 }}
p = p.replace('"cam\":\"{17:F1},{18:F1},{19:F1}\",\"pawn\":\"{20}\"}}",',
              '"cam\":\"{17:F1},{18:F1},{19:F1}\",\"pawn\":\"{20}",')
wr(base + r'\PerfMon3D.cs', p)
print("perf ok")
