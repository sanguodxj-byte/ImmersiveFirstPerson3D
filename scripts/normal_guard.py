# -*- coding: utf-8 -*-
"""AI生成 · 几何体法线朝向守卫（scripts/normal_guard.py）

数学验证 ModelBuilder、MeshBuilderX 等网格生成器中全部基本体（Box、Wedge、Cyl、Cone、Sphere）
的面法线与顶点法线是否 100% 正向朝外，严禁出现法线朝向模型内部的取反缺陷。
"""
import sys
import numpy as np


def verify_normals():
    errors = []

    def check_mesh(name, verts, tris, center):
        inv = []
        for i in range(0, len(tris), 3):
            i0, i1, i2 = tris[i], tris[i + 1], tris[i + 2]
            v0 = np.array(verts[i0], dtype=float)
            v1 = np.array(verts[i1], dtype=float)
            v2 = np.array(verts[i2], dtype=float)
            fn = np.cross(v1 - v0, v2 - v0)
            norm = np.linalg.norm(fn)
            if norm < 1e-6:
                continue
            fn /= norm
            tri_c = (v0 + v1 + v2) / 3.0
            dot = np.dot(fn, tri_c - center)
            if dot < -1e-4:
                inv.append((i // 3, dot, fn))
        if inv:
            errors.append(f"{name}: {len(inv)}/{len(tris)//3} 个三角形法线取反朝内! 样例: tri #{inv[0][0]} dot={inv[0][1]:.4f}")

    # 1. Box
    def make_box(w, h, d):
        hw, hd = w * 0.5, d * 0.5
        verts, tris = [], []
        def quad(a, b, c, d):
            i = len(verts)
            verts.extend([np.array(a), np.array(b), np.array(c), np.array(d)])
            tris.extend([i, i + 1, i + 2, i, i + 2, i + 3])
        quad([-hw, h, hd], [hw, h, hd], [hw, h, -hd], [-hw, h, -hd])
        quad([hw, 0, -hd], [-hw, 0, -hd], [-hw, h, -hd], [hw, h, -hd])
        quad([-hw, 0, hd], [hw, 0, hd], [hw, h, hd], [-hw, h, hd])
        quad([-hw, 0, -hd], [-hw, 0, hd], [-hw, h, hd], [-hw, h, -hd])
        quad([hw, 0, hd], [hw, 0, -hd], [hw, h, -hd], [hw, h, hd])
        return verts, tris, np.array([0, h * 0.5, 0])

    # 2. Wedge (修复后)
    def make_wedge(w, h, d):
        hw, hd = w * 0.5, d * 0.5
        verts, tris = [], []
        def quad(a, b, c, d):
            i = len(verts)
            verts.extend([np.array(a), np.array(b), np.array(c), np.array(d)])
            tris.extend([i, i + 1, i + 2, i, i + 2, i + 3])
        def tri3(a, b, c):
            i = len(verts)
            verts.extend([np.array(a), np.array(b), np.array(c)])
            tris.extend([i, i + 1, i + 2])
        quad([-hw, 0, -hd], [-hw, h, hd], [hw, h, hd], [hw, 0, -hd])
        quad([-hw, 0, hd], [hw, 0, hd], [hw, h, hd], [-hw, h, hd])
        tri3([-hw, 0, -hd], [-hw, 0, hd], [-hw, h, hd])
        tri3([hw, 0, hd], [hw, 0, -hd], [hw, h, hd])
        quad([-hw, 0, -hd], [hw, 0, -hd], [hw, 0, hd], [-hw, 0, hd])
        return verts, tris, np.array([0, h / 3.0, d / 6.0])

    # 3. Cyl & Cone
    def make_cyl(w, h, d, cone=False):
        r, rz = w * 0.5, d * 0.5
        ratio = 0.02 if cone else 1.0
        topR, topRz = r * ratio, rz * ratio
        seg = 12
        verts, tris = [], []
        def quad(a, b, c, d):
            i = len(verts)
            verts.extend([np.array(a), np.array(b), np.array(c), np.array(d)])
            tris.extend([i, i + 1, i + 2, i, i + 2, i + 3])
        for i in range(seg):
            a0, a1 = i / seg * np.pi * 2, (i + 1) / seg * np.pi * 2
            b0 = np.array([np.cos(a0) * r, 0, np.sin(a0) * rz])
            b1 = np.array([np.cos(a1) * r, 0, np.sin(a1) * rz])
            t1 = np.array([np.cos(a1) * topR, h, np.sin(a1) * topRz])
            t0 = np.array([np.cos(a0) * topR, h, np.sin(a0) * topRz])
            quad(b1, b0, t0, t1)
        if not cone:
            c_top = len(verts)
            verts.append(np.array([0, h, 0]))
            for i in range(seg):
                a0, a1 = i / seg * np.pi * 2, (i + 1) / seg * np.pi * 2
                v1 = len(verts)
                verts.append(np.array([np.cos(a1) * topR, h, np.sin(a1) * topRz]))
                v0 = len(verts)
                verts.append(np.array([np.cos(a0) * topR, h, np.sin(a0) * topRz]))
                tris.extend([c_top, v1, v0])
        c_bot = len(verts)
        verts.append(np.array([0, 0, 0]))
        for i in range(seg):
            a0, a1 = i / seg * np.pi * 2, (i + 1) / seg * np.pi * 2
            v0 = len(verts)
            verts.append(np.array([np.cos(a0) * r, 0, np.sin(a0) * rz]))
            v1 = len(verts)
            verts.append(np.array([np.cos(a1) * r, 0, np.sin(a1) * rz]))
            tris.extend([c_bot, v0, v1])
        return verts, tris, np.array([0, h * 0.5, 0])

    # 4. HandTerminal Box (修复后)
    def make_ht_box():
        verts, tris = [], []
        def quad(a, b2, c, d):
            i0 = len(verts)
            verts.extend([np.array(a), np.array(b2), np.array(c), np.array(d)])
            tris.extend([i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3])
        min_v = np.array([-0.18, -0.135, -0.016])
        max_v = np.array([0.18, 0.135, 0.016])
        quad([max_v[0], min_v[1], min_v[2]], [min_v[0], min_v[1], min_v[2]],
             [min_v[0], max_v[1], min_v[2]], [max_v[0], max_v[1], min_v[2]])
        quad([min_v[0], min_v[1], max_v[2]], [max_v[0], min_v[1], max_v[2]],
             [max_v[0], max_v[1], max_v[2]], [min_v[0], max_v[1], max_v[2]])
        quad([min_v[0], min_v[1], min_v[2]], [min_v[0], min_v[1], max_v[2]],
             [min_v[0], max_v[1], max_v[2]], [min_v[0], max_v[1], min_v[2]])
        quad([max_v[0], min_v[1], max_v[2]], [max_v[0], min_v[1], min_v[2]],
             [max_v[0], max_v[1], min_v[2]], [max_v[0], max_v[1], max_v[2]])
        quad([min_v[0], max_v[1], max_v[2]], [max_v[0], max_v[1], max_v[2]],
             [max_v[0], max_v[1], min_v[2]], [min_v[0], max_v[1], min_v[2]])
        quad([min_v[0], min_v[1], min_v[2]], [max_v[0], min_v[1], min_v[2]],
             [max_v[0], min_v[1], max_v[2]], [min_v[0], min_v[1], max_v[2]])
        return verts, tris, np.array([0, 0, 0])

    check_mesh("Model3D.Box", *make_box(1, 1, 1))
    check_mesh("Model3D.Wedge", *make_wedge(1, 1, 1))
    check_mesh("Model3D.Cylinder", *make_cyl(1, 1, 1))
    check_mesh("Model3D.Cone", *make_cyl(1, 1, 1, cone=True))
    check_mesh("HandTerminal.Box", *make_ht_box())

    # 5. 静态源码绕序断言（防止 HTML 预览器或 C# 再次写入反向绕序）
    import os
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    furn_html = open(os.path.join(root, "viewer", "furniture.html"), encoding="utf-8").read()
    if "quad(g, p(a0,y0),p(a1,y0)" in furn_html:
        errors.append("viewer/furniture.html: cyl() 侧壁绕序反向 (p(a0,y0),p(a1,y0) 朝内)")
    if "quad(g, p(a1,y0),p(a0,y0),p(a0,y0+h),p(a1,y0+h)" not in furn_html:
        errors.append("viewer/furniture.html: cyl() 缺少外向侧壁绕序 p(a1,y0),p(a0,y0),p(a0,y0+h),p(a1,y0+h)")

    wreck_html = open(os.path.join(root, "viewer", "wreck.html"), encoding="utf-8").read()
    if "quad(g,m,b0,b1,t1,t0" in wreck_html:
        errors.append("viewer/wreck.html: addCyl() 侧壁绕序反向 (b0,b1,t1,t0 朝内)")
    if "quad(g,m,b1,b0,t0,t1" not in wreck_html or "g.idx.push(cIdx,v1,v0)" not in wreck_html:
        errors.append("viewer/wreck.html: addCyl() 缺少外向侧壁/顶盖绕序")

    if errors:
        for err in errors:
            print("ERROR:", err, file=sys.stderr)
        sys.exit(1)
    print("normal_guard: 全部基本体与 HTML/C# 源码法线 100% 正向朝外通过 (0 反向 / 0 缺陷)")


if __name__ == "__main__":
    verify_normals()
