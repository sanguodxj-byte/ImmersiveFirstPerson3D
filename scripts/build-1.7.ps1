param(
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $root 'Source\RimFirstPerson3D\RimFirstPerson3D.csproj'

& $dotnet build $project '-p:DeployToGame=false'
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
if ($Deploy) { & (Join-Path $PSScriptRoot 'deploy.ps1') }
