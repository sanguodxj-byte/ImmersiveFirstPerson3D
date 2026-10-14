$ErrorActionPreference = "Stop"

$src = "D:\rim mod\FirstPerson3D"
$dst = "D:\Steam\steamapps\common\RimWorld\Mods\FirstPerson3D"
$arc = "D:\Steam\steamapps\common\RimWorld\Mods\_deploy_archive\FirstPerson3D"

$game = Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue
if ($game) {
    throw "RimWorld is running, DLL locked. Close game first."
}

# Mirror one subfolder. Files present in dest but absent in src are MOVED to
# archive dir (never deleted), then src is copied over dest.
function Sync-Dir([string]$from, [string]$to, [string]$label) {
    if (-not (Test-Path $from)) { return }
    if (Test-Path $to) {
        $keep = @{}
        Get-ChildItem $from -Recurse -File | ForEach-Object { $keep[$_.FullName.Substring($from.Length)] = $true }
        foreach ($f in (Get-ChildItem $to -Recurse -File)) {
            $rel = $f.FullName.Substring($to.Length)
            if (-not $keep.ContainsKey($rel)) {
                $tgt = Join-Path (Join-Path $arc $label) $rel.TrimStart('\')
                New-Item -ItemType Directory -Path (Split-Path $tgt) -Force | Out-Null
                Move-Item $f.FullName $tgt -Force
                Write-Output ("    archived " + $label + $rel)
            }
        }
        # drop directories that became empty after archiving (no data removed)
        Get-ChildItem $to -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
            if (-not (Get-ChildItem $_.FullName -Force)) { [System.IO.Directory]::Delete($_.FullName, $false) }
        }
    }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    Copy-Item (Join-Path $from '*') $to -Recurse -Force
}

Write-Output "==> deploy to $dst"
Sync-Dir "$src\About"    "$dst\About"    "About"
Sync-Dir "$src\Defs"     "$dst\Defs"     "Defs"
Sync-Dir "$src\Textures" "$dst\Textures" "Textures"
Sync-Dir "$src\1.7"      "$dst\1.7"      "1.7"
Copy-Item "$src\LoadFolders.xml" "$dst\LoadFolders.xml" -Force
Write-Output "==> deploy done"
