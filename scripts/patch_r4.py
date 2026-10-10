# -*- coding: utf-8 -*-
# r4: 停用原版相机对象 + 相机depth + 物品层矩阵缓存
import io

def rd(p):
    return io.open(p, encoding='utf-8').read()

def wr(p, s):
    io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

# 1) VanillaSuppressor: SetActive(false) 原版相机对象
s = rd(base + r'\VanillaSuppressor.cs')
old = """        public static void ApplyCameraSuppression()
        {
            var camGo = GameObject.Find("Camera");
            if (camGo != null)
            {
                if (!cameraStored)
                {
                    cameraStored = true;
                    mapCamera = camGo.GetComponent<Camera>();
                    colorCurves = camGo.GetComponent("ColorCorrectionCurves") as Behaviour;
                    mapCameraWasEnabled = mapCamera != null && mapCamera.enabled;
                    colorCurvesWasEnabled = colorCurves != null && colorCurves.enabled;
                }
                if (mapCamera != null) mapCamera.enabled = false;
                if (colorCurves != null) colorCurves.enabled = false;
            }
            var subGo = GameObject.Find("Subcameras");
            if (subGo != null)
            {
                if (!cameraStored || subcamerasRoot == null)
                {
                    subcamerasRoot = subGo;
                    subcamerasWasActive = subGo.activeSelf;
                }
                subGo.SetActive(false);
            }
        }

        public static void RestoreCameras()
        {
            if (mapCamera != null && mapCameraWasEnabled) mapCamera.enabled = true;
            if (colorCurves != null && colorCurvesWasEnabled) colorCurves.enabled = true;
            if (subcamerasRoot != null) subcamerasRoot.SetActive(subcamerasWasActive);
        }"""
new = """        private static GameObject vanillaCamGo;

        public static void ApplyCameraSuppression()
        {
            var camGo = GameObject.Find("Camera");
            if (camGo != null)
            {
                if (!cameraStored)
                {
                    cameraStored = true;
                    vanillaCamGo = camGo;
                    mapCamera = camGo.GetComponent<Camera>();
                    colorCurves = camGo.GetComponent("ColorCorrectionCurves") as Behaviour;
                    mapCameraWasEnabled = mapCamera == null || mapCamera.enabled;
                    colorCurvesWasEnabled = colorCurves == null || colorCurves.enabled;
                }
                // 整对象停用：连带的驱动脚本与后处理一并失效，杜绝任何路径重新启用相机。
                camGo.SetActive(false);
            }
            var subGo = GameObject.Find("Subcameras");
            if (subGo != null)
            {
                if (subcamerasRoot == null)
                {
                    subcamerasRoot = subGo;
                    subcamerasWasActive = subGo.activeSelf;
                }
                subGo.SetActive(false);
            }
        }

        public static void RestoreCameras()
        {
            if (vanillaCamGo != null) vanillaCamGo.SetActive(true);
            if (mapCamera != null && mapCameraWasEnabled) mapCamera.enabled = true;
            if (colorCurves != null && colorCurvesWasEnabled) colorCurves.enabled = true;
            if (subcamerasRoot != null) subcamerasRoot.SetActive(subcamerasWasActive);
        }"""
assert old in s, "suppressor old not found"
s = s.replace(old, new)
wr(base + r'\VanillaSuppressor.cs', s)
print("suppressor ok")

# 2) CameraRig3D: depth
c = rd(base + r'\CameraRig3D.cs')
old = """            cam.cullingMask = 1 << 0;
            cam.enabled = false;
            camGO.AddComponent<AudioListener>();"""
new = """            cam.cullingMask = 1 << 0;
            cam.depth = 10f;
            cam.enabled = false;
            camGO.AddComponent<AudioListener>();"""
assert old in c, "camera old not found"
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("camera ok")

# 3) ItemLayer3D: 矩阵按偏航桶缓存
i = rd(base + r'\ItemLayer3D.cs')
old_fields = """        private readonly List<Entry> entries = new List<Entry>();
        private bool dirty = true;"""
new_fields = """        private readonly List<Entry> entries = new List<Entry>();
        private bool dirty = true;
        private float lastYawBucket = float.MinValue;

        private class InstBatch
        {
            public Material mat;
            public Matrix4x4[] matrices;
        }

        private readonly List<InstBatch> standing = new List<InstBatch>();
        private readonly List<InstBatch> flat = new List<InstBatch>();"""
assert old_fields in i, "item fields not found"
i = i.replace(old_fields, new_fields)

start = i.index("        // 每帧：非尸体需要面向相机，矩阵按帧重建（物体数量通常可控）。")
end_marker = "        public void Dispose()"
end = i.index(end_marker)
new_draw = '''        // 立牌朝向只随相机偏航变化：按 3 度桶缓存矩阵数组，物品集变化时整建。
        public void Draw(Vector3 camPos)
        {
            if (entries.Count == 0) return;
            float yawBucket = Mathf.Round(Mathf.Atan2(camPos.x, camPos.z) * Mathf.Rad2Deg / 3f) * 3f;
            if (dirty || Mathf.Abs(yawBucket - lastYawBucket) > 0.1f)
            {
                lastYawBucket = yawBucket;
                RebuildMatrices(camPos);
            }
            var quad = Util3D.BillboardQuad;
            var plane = Util3D.GroundQuad;
            for (int i = 0; i < standing.Count; i++)
            {
                Graphics.DrawMeshInstanced(quad, 0, standing[i].mat, standing[i].matrices);
                PerfCounters.ItemBatches++;
                PerfCounters.ItemInstances += standing[i].matrices.Length;
            }
            for (int i = 0; i < flat.Count; i++)
            {
                Graphics.DrawMeshInstanced(plane, 0, flat[i].mat, flat[i].matrices);
                PerfCounters.ItemBatches++;
                PerfCounters.ItemInstances += flat[i].matrices.Length;
            }
        }

        private void RebuildMatrices(Vector3 camPos)
        {
            dirty = false;
            standing.Clear();
            flat.Clear();
            var byMat = new Dictionary<Material, List<Matrix4x4>>();
            var flatByMat = new Dictionary<Material, List<Matrix4x4>>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.thing == null || !e.thing.Spawned) continue;
                if (e.flatCorpse)
                {
                    var matrix = Matrix4x4.TRS(e.pos, Quaternion.AngleAxis(e.flatYaw, Vector3.up),
                        new Vector3(e.w, 1f, e.h));
                    List<Matrix4x4> l;
                    if (!flatByMat.TryGetValue(e.mat, out l)) { l = new List<Matrix4x4>(64); flatByMat[e.mat] = l; }
                    l.Add(matrix);
                }
                else
                {
                    float yaw = Mathf.Atan2(camPos.x - e.pos.x, camPos.z - e.pos.z) * Mathf.Rad2Deg;
                    var matrix = Matrix4x4.TRS(e.pos, Quaternion.AngleAxis(yaw, Vector3.up),
                        new Vector3(e.w, e.h, 1f));
                    List<Matrix4x4> l;
                    if (!byMat.TryGetValue(e.mat, out l)) { l = new List<Matrix4x4>(128); byMat[e.mat] = l; }
                    l.Add(matrix);
                }
            }
            foreach (var kv in byMat)
            {
                int offset = 0;
                while (offset < kv.Value.Count)
                {
                    int take = Mathf.Min(1023, kv.Value.Count - offset);
                    var arr = new Matrix4x4[take];
                    for (int k = 0; k < take; k++) arr[k] = kv.Value[offset + k];
                    standing.Add(new InstBatch { mat = kv.Key, matrices = arr });
                    offset += take;
                }
            }
            foreach (var kv in flatByMat)
            {
                int offset = 0;
                while (offset < kv.Value.Count)
                {
                    int take = Mathf.Min(1023, kv.Value.Count - offset);
                    var arr = new Matrix4x4[take];
                    for (int k = 0; k < take; k++) arr[k] = kv.Value[offset + k];
                    flat.Add(new InstBatch { mat = kv.Key, matrices = arr });
                    offset += take;
                }
            }
        }

'''
i = i[:start] + new_draw + i[end:]

# Update 不再消费 dirty（留给 Draw），但 entries 仍需重建
old_upd = """        public void Update()
        {
            if (!dirty) return;
            dirty = false;"""
new_upd = """        public void Update()
        {
            if (!dirty) return;
            // dirty 由 Draw 消费（矩阵随之重建），此处只重建 entries。"""
assert old_upd in i, "update block not found"
i = i.replace(old_upd, new_upd)
wr(base + r'\ItemLayer3D.cs', i)
print("item ok")

# 4) ModEntry r4
m = rd(base + r'\ModEntry.cs')
m = m.replace('initialized (r3)', 'initialized (r4)')
wr(base + r'\ModEntry.cs', m)
print("entry ok")
