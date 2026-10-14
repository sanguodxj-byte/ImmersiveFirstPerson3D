param(
    [string]$OutPath = "D:\rim mod\FirstPerson3D\shots",
    [string]$Name = "shot"
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int nCmdShow);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$p = Get-Process -Name 'RimWorldWin64' -ErrorAction SilentlyContinue | Select-Object -First 1
$h = [IntPtr]::Zero
if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
    $h = $p.MainWindowHandle
} else {
    $h = [Win32]::FindWindow($null, "RimWorld by Ludeon Studios")
}
if ($h -eq [IntPtr]::Zero) { throw "RimWorld window not found" }
[Win32]::ShowWindow($h, 9) | Out-Null
[Win32]::SwitchToThisWindow($h, $true)
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$r = New-Object Win32+RECT
[Win32]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
if ($w -lt 100 -or $ht -lt 100) { throw "bad window rect ${w}x${ht}" }
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
$g.Dispose()
New-Item -ItemType Directory -Force -Path $OutPath | Out-Null
$file = Join-Path $OutPath ("{0}_{1}.png" -f $Name, (Get-Date -Format "HHmmss"))
$bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "$file ${w}x${ht}"
