# 机检之外的最后一道网：清 Chrome 自己造的 scoped_dir / HeadlessChrome*。
# 本仓库的出图脚本已不需要它（每次启动一个 D 盘沙盒，出完图 drop() 整目录删，
# 见 scripts/_tmpsweep.py），留着是为了**仓库外**的无头 Chrome
# （浏览器自动化、别的代理的工具），它们照样每次往 C 盘 %TEMP% 丢 23~26MB。
# 只动静置 > 90s 的，避开正在跑的实例。可选：不起它本仓库也不会漏。
$t = 'C:\Users\Administrator\AppData\Local\Temp'
$pat = '^scoped_dir|^HeadlessChrome'
$cut = 90
while ($true) {
    $n = 0
    Get-ChildItem $t -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $pat -and $_.LastWriteTime -lt (Get-Date).AddSeconds(-$cut) } |
        ForEach-Object { try { [System.IO.Directory]::Delete($_.FullName, $true); $n++ } catch {} }
    if ($n -gt 0) {
        Write-Output ("{0} 清{1}个 C 盘可用{2:N2}GB" -f (Get-Date -Format 'HH:mm:ss'), $n,
                      ((Get-PSDrive C).Free / 1GB))
    }
    Start-Sleep -Seconds 20
}
