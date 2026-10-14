Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinKey {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int nCmdShow);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    public static void SendKey(byte vk) {
        keybd_event(vk, 0, 0, 0);
        System.Threading.Thread.Sleep(60);
        keybd_event(vk, 0, 2, 0);
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(2, 0, 0, 0, 0);
        System.Threading.Thread.Sleep(60);
        mouse_event(4, 0, 0, 0, 0);
    }

    public static void RightClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(8, 0, 0, 0, 0); // right down
        System.Threading.Thread.Sleep(60);
        mouse_event(16, 0, 0, 0, 0); // right up
    }
}
"@
$p = Get-Process -Name 'RimWorldWin64' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no-game"; exit }
$h = $p.MainWindowHandle
[WinKey]::ShowWindow($h, 9) | Out-Null
[WinKey]::SwitchToThisWindow($h, $true)
[WinKey]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
