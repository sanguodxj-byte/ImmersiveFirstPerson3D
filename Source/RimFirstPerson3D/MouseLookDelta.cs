// AI生成
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RimFirstPerson3D
{
    // 自由环视的鼠标增量来源。
    // 背景：RimWorld 的 Unity InputManager 未定义 "Mouse X"/"Mouse Y" 轴，
    // 因此 Input.GetAxis("Mouse X") 恒返回 0，锁定光标的环视无法转向。
    // 方案：不锁定光标（仅隐藏）+ 取光标位置差得到环视增量，并每帧把系统
    // 光标移回窗口客户区中心，避免光标撞到屏幕边缘后无法继续转向。
    //
    // 关键：读取与回中统一使用 Win32 光标坐标（GetCursorPos/SetCursorPos），
    // 且在回中后「回读实际落点」作为下一帧基准。这样稳态增量为 0，
    // 不会出现“读数与回中目标不一致”导致的视角持续漂移。
    internal static class MouseLookDelta
    {
        private static int lastX, lastY;
        private static bool ready;

        public static void Reset() { ready = false; }

        // 返回本帧像素位移；Y 向上为正，与既有 Pitch/Yaw 公式一致。
        public static Vector2 Read()
        {
            if (!TryGetCursor(out int cx, out int cy))
            {
                // 非 Windows / 读取失败：退化为 Unity 位置差（Y 已向上），不做回中。
                Vector2 p = Input.mousePosition;
                Vector2 d = ready ? (p - new Vector2(lastX, lastY)) : Vector2.zero;
                lastX = Mathf.RoundToInt(p.x);
                lastY = Mathf.RoundToInt(p.y);
                ready = true;
                return Clamp(d);
            }

            // Win32 光标 Y 向下，转为 Y 向上以匹配既有公式。
            Vector2 delta = ready ? new Vector2(cx - lastX, lastY - cy) : Vector2.zero;
            ready = true;

            // 回中后回读“实际”落点写入 last：下一帧稳态增量恒为 0，根治漂移。
            TryRecenter();
            if (TryGetCursor(out int ax, out int ay)) { lastX = ax; lastY = ay; }
            else { lastX = cx; lastY = cy; }

            return Clamp(delta);
        }

        private static Vector2 Clamp(Vector2 d)
        {
            d.x = Mathf.Clamp(d.x, -160f, 160f);
            d.y = Mathf.Clamp(d.y, -160f, 160f);
            return d;
        }

        private static bool IsWindows =>
            Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.WindowsEditor;

        // 把系统光标移回窗口客户区中心。
        private static void TryRecenter()
        {
            if (!IsWindows) return;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                if (!GetClientRect(hwnd, out RECT rc)) return;
                var pt = new POINT { x = (rc.right - rc.left) / 2, y = (rc.bottom - rc.top) / 2 };
                if (!ClientToScreen(hwnd, ref pt)) return;
                SetCursorPos(pt.x, pt.y);
            }
            catch { }
        }

        private static bool TryGetCursor(out int x, out int y)
        {
            x = 0; y = 0;
            if (!IsWindows) return false;
            try
            {
                if (!GetCursorPos(out POINT p)) return false;
                x = p.x; y = p.y;
                return true;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    }
}