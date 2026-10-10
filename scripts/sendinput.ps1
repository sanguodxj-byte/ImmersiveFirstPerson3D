param(
  [string]$Action = "",
  [int]$Dx = 0,
  [int]$Dy = 0,
  [string]$Key = "",
  [int]$HoldMs = 500
)
$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @"
using System;
using System.Threading;
using System.Runtime.InteropServices;
public static class Inj3 {
  [StructLayout(LayoutKind.Sequential)]
  public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Sequential)]
  public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit)]
  public struct IU { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)]
  public struct INPUT { public uint type; public IU u; }
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
  public const uint MOUSE = 0, KBD = 1;
  public const uint M_MOVE = 1, M_RDOWN = 8, M_RUP = 16, M_LDOWN = 2, M_LUP = 4;
  public const uint K_UP = 2;
  static void Mouse(uint flags, int dx, int dy) {
    INPUT[] a = new INPUT[1]; a[0].type = MOUSE; a[0].u.mi.dwFlags = flags; a[0].u.mi.dx = dx; a[0].u.mi.dy = dy;
    SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  static void Key(ushort vk, bool up) {
    INPUT[] a = new INPUT[1]; a[0].type = KBD; a[0].u.ki.wVk = vk;
    a[0].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
    a[0].u.ki.dwFlags = up ? K_UP : 0;
    SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  public static string Run(string action, int dx, int dy, string key, int holdMs) {
    var ps = System.Diagnostics.Process.GetProcessesByName("RimWorldWin64");
    if (ps.Length == 0) return "no-game";
    IntPtr h = ps[0].MainWindowHandle;
    SwitchToThisWindow(h, true);
    Thread.Sleep(200);
    SetForegroundWindow(h);
    Thread.Sleep(300);
    uint fgPid = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
    string focus = (fgPid == (uint)ps[0].Id) ? "focus-ok" : "focus-FAIL";
    if (focus == "focus-FAIL") return focus;
    if (action == "rdown") Mouse(M_RDOWN, 0, 0);
    else if (action == "rup") Mouse(M_RUP, 0, 0);
    else if (action == "move") Mouse(M_MOVE, dx, dy);
    else if (action == "lclick") { Mouse(M_LDOWN, 0, 0); Thread.Sleep(60); Mouse(M_LUP, 0, 0); }
    else if (action == "key") {
      ushort vk = (ushort)key.ToUpper()[0];
      Key(vk, false); Thread.Sleep(holdMs); Key(vk, true);
    }
    return "ok";
  }
}
"@
[Inj3]::Run($Action, $Dx, $Dy, $Key, $HoldMs)
