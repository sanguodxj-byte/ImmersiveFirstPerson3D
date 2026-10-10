$ErrorActionPreference = "Stop"

$src = "D:\rim mod\FirstPerson3D"
$dst = "D:\Steam\steamapps\common\RimWorld\Mods\FirstPerson3D"

$game = Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue
if ($game) {
    throw "RimWorld is running (PID $($game.Id)), DLL locked. Close game first."
}

Write-Output "==> deploy to $dst"
robocopy "$src\About" "$dst\About" /MIR /NFL /NDL /NJH /NJS | Out-Null
robocopy "$src\1.6" "$dst\1.6" /MIR /NFL /NDL /NJH /NJS | Out-Null
Copy-Item "$src\LoadFolders.xml" "$dst\LoadFolders.xml" -Force
Write-Output "==> deploy done"
