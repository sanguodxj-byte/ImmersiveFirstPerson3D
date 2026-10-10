# AI生成
import math
# AI生成
# FP3D 手持地图终端建模：Blender 无头脚本
# 产物：D:/rim mod/FirstPerson3D/1.6/Assets/hand_terminal.obj(+mtl)
# 坐标约定：Blender Z-up、屏幕正面朝 -Y；OBJ 默认导出(-Z fwd, Y up)后
# 在 Unity 中正面朝 +Z（正对第一人称相机）、上方 +Y，无需二次旋转。
# 尺寸单位：米（1 格 = 1 米的世界尺度）。
import bpy
import os

OBJ_PATH = "D:/rim mod/FirstPerson3D/1.6/Assets/hand_terminal.obj"
QC_DIR = os.environ.get("DSH_OUTDIR", "D:/123/blender-rt/work/fp3d_terminal")

W, H, T = 0.36, 0.27, 0.032          # 机身宽/高/厚
SCR_W, SCR_H = 0.29, 0.205           # 屏幕有效面
BEZ_O_W, BEZ_O_H = 0.32, 0.235       # 黑边框外沿

# ---- 清场 ----
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
    for item in list(blk):
        if item.users == 0:
            blk.remove(item)

# ---- 材质（usemtl 组名=游戏内导入分组键）----
def make_mat(name, rgb, rough=0.55, metal=0.0, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = metal
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.2
    return m

mat_body = make_mat("body", (0.55, 0.47, 0.38))            # 暖灰外壳
mat_dark = make_mat("dark", (0.10, 0.11, 0.13), rough=0.7)  # 边框/握把
mat_metal = make_mat("metal", (0.72, 0.68, 0.55), metal=0.8, rough=0.35)
mat_screen = make_mat("screen", (0.05, 0.10, 0.14), emit=(0.1, 0.5, 0.6))

# ---- 建件工具 ----
def add_box(name, size, loc, mat, bevel=0.0, smooth=False):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        bev = o.modifiers.new("bev", "BEVEL")
        bev.width = bevel
        bev.segments = 3
        bev.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier="bev")
    if smooth:
        bpy.ops.object.shade_smooth()
    else:
        bpy.ops.object.shade_auto_smooth(angle=0.6)
    o.data.materials.append(mat)
    return o

# 机身
add_box("body", (W, T, H), (0, 0, 0), mat_body, bevel=0.009)

# 黑边框：屏幕四周一圈凸起框（4 条）
bw = (BEZ_O_W - SCR_W) / 2          # 左右框条宽
bh = (BEZ_O_H - SCR_H) / 2          # 上下框条高
fy = -T / 2 - 0.0012                # 凸出正面 1.2mm
add_box("bezel_top", (BEZ_O_W, 0.0016, bh), (0, fy, SCR_H / 2 + bh / 2), mat_dark, bevel=0.0012)
add_box("bezel_bot", (BEZ_O_W, 0.0016, bh), (0, fy, -SCR_H / 2 - bh / 2), mat_dark, bevel=0.0012)
add_box("bezel_l", (bw, 0.0016, SCR_H), (-SCR_W / 2 - bw / 2, fy, 0), mat_dark, bevel=0.0012)
add_box("bezel_r", (bw, 0.0016, SCR_H), (SCR_W / 2 + bw / 2, fy, 0), mat_dark, bevel=0.0012)

# 屏幕面不在 Blender 建：单面 plane 的朝向/位置经导出链路易错
#（实测两轮旋转都未落到预期位），改由游戏端程序 quad 承载（HandTerminal3D.MakeQuad），
# 机身只负责边框与壳体。

# 双侧握把（下伸，圆角胶囊感）
gx = W / 2 + 0.05 / 2 - 0.014
add_box("grip_l", (0.05, 0.032, 0.115), (-gx, 0, -H / 2 - 0.032), mat_dark, bevel=0.013)
add_box("grip_r", (0.05, 0.032, 0.115), (gx, 0, -H / 2 - 0.032), mat_dark, bevel=0.013)

# 背部电池包 + 散热条
add_box("battery", (0.20, 0.014, 0.062), (0, T / 2 + 0.007, 0.02), mat_metal, bevel=0.004)
for i in range(3):
    add_box("vent%d" % i, (0.16, 0.002, 0.007), (0, T / 2 + 0.0009, 0.088 + i * 0.014), mat_dark)

# 天线：杆 + 球头（顶部右侧）
bpy.ops.mesh.primitive_cylinder_add(radius=0.0032, depth=0.055,
                                    location=(0.148, 0, H / 2 + 0.0275))
ant = bpy.context.active_object
ant.name = "antenna"
ant.data.materials.append(mat_metal)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0068, location=(0.148, 0, H / 2 + 0.058))
tip = bpy.context.active_object
tip.name = "antenna_tip"
tip.data.materials.append(mat_dark)
bpy.ops.object.shade_smooth()

# ---- 导出 OBJ（三角化、带 UV/法线/材质组，默认 -Z fwd / Y up）----
os.makedirs(os.path.dirname(OBJ_PATH), exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.wm.obj_export(
    filepath=OBJ_PATH,
    export_triangulated_mesh=True,
    export_uv=True,
    export_normals=True,
    export_materials=True,
    path_mode="COPY",
)

# ---- QC 渲染：正面 / 斜 45 / 背面 ----
import math
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.world = bpy.data.worlds.new("qc_world")
scene.world.use_nodes = True
bg = scene.world.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.16, 0.18, 0.22, 1.0)
bg.inputs[1].default_value = 1.0

key = bpy.data.lights.new("key", "AREA")
key.energy = 60
key.size = 1.2
ko = bpy.data.objects.new("key", key)
scene.collection.objects.link(ko)
rim = bpy.data.lights.new("rim", "AREA")
rim.energy = 15
rim.size = 0.8
ro = bpy.data.objects.new("rim", rim)
scene.collection.objects.link(ro)

cam_data = bpy.data.cameras.new("qc_cam")
cam_data.lens = 60
cam = bpy.data.objects.new("qc_cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

os.makedirs(QC_DIR, exist_ok=True)
views = [
    ("front", (0.0, -0.92, 0.0)),
    ("threeq", (0.55, -0.62, 0.32)),
    ("back", (0.3, 0.78, 0.3)),
]
for name, pos in views:
    cam.location = pos
    direction = Vector((0, 0, 0)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    ko.location = cam.location + Vector((0.2, -0.3, 0.6))
    ko.rotation_euler = (Vector((0, 0, 0)) - ko.location).to_track_quat("-Z", "Y").to_euler()
    ro.location = Vector((-0.5, 0.4, 0.5))
    ro.rotation_euler = (Vector((0, 0, 0)) - ro.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(QC_DIR, "terminal_%s.png" % name)
    bpy.ops.render.render(write_still=True)

print("FP3D terminal OBJ ->", OBJ_PATH)
print("FP3D terminal QC  ->", QC_DIR)
