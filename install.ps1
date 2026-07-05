param(
    [string]$KspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
)

$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "GameData\KSPHoverSlam"
$destination = Join-Path $KspRoot "GameData\KSPHoverSlam"

if (-not (Test-Path (Join-Path $source "Plugins\KSPHoverSlam.dll"))) {
    throw "KSPHoverSlam.dll is missing. Run .\build.ps1 first."
}

New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force

Write-Host "Installed KSPHoverSlam to $destination"
