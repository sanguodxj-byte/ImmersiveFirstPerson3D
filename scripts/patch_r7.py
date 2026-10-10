# -*- coding: utf-8 -*-
# r7: 渲染管线三重探针
import io

def rd(p): return io.open(p, encoding='utf-8').read()
def wr(p, s): io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

# 1) 探针组件：GL 立即模式红方块 + 真实立方体
probe = '''using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 渲染管线探针：挂在透视相机上。
    // OnPostRender 的 GL 立即模式红方块绕过所有剔除/批处理，
    // 出现与否直接判定该相机是否参与最终呈现。
    public class Fps3DCamProbe : MonoBehaviour
    {
        public static bool DrawGlQuad = true;

        private static Material glMat;

        private void OnPostRender()
        {
            if (!DrawGlQuad || !Fps3DState.Active) return;
            if (glMat == null)
            {
                glMat = new Material(ShaderDatabase.SolidColor);
                glMat.color = new Color(1f, 0.2f, 0.1f, 1f);
            }
            GL.PushMatrix();
            glMat.SetPass(0);
            GL.LoadOrtho();
            GL.Color(new Color(1f, 0.2f, 0.1f, 1f));
            GL.Begin(GL.QUADS);
            GL.Vertex3(0.35f, 0.35f, 0f);
            GL.Vertex3(0.65f, 0.35f, 0f);
            GL.Vertex3(0.65f, 0.65f, 0f);
            GL.Vertex3(0.35f, 0.65f, 0f);
            GL.End();
            GL.PopMatrix();
        }
    }
}
'''
wr(base + r'\Fps3DCamProbe.cs', probe)
print("probe ok")

# 2) CameraRig: 挂探针 + MainCamera 标签 + 立方体
c = rd(base + r'\CameraRig3D.cs')
old = """            cam.depth = 10f;
            cam.enabled = false;
            camGO.AddComponent<AudioListener>();"""
new = """            cam.depth = 10f;
            cam.enabled = false;
            camGO.AddComponent<AudioListener>();
            camGO.AddComponent<Fps3DCamProbe>();
            try { camGO.tag = "MainCamera"; } catch { }
            // 真实立方体探针：MeshRenderer 路径
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Fps3D_ProbeCube";
            cube.transform.SetParent(camGO.transform, false);
            cube.transform.localPosition = new Vector3(0f, -0.25f, 0.6f);
            cube.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            var cm = cube.GetComponent<MeshRenderer>();
            cm.sharedMaterial = Util3D.CutoutMatFor(Util3D.WhiteTex);
            Object.DontDestroyOnLoad(cube);"""
assert old in c
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("camera ok")
