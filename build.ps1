param(
    [string]$KspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$managed = Join-Path $KspRoot "KSP_x64_Data\Managed"
$outputDir = Join-Path $projectRoot "GameData\KSPHoverSlam\Plugins"
$outputDll = Join-Path $outputDir "KSPHoverSlam.dll"
$sourceDir = Join-Path $projectRoot "src\KSPHoverSlam"

if (-not (Test-Path $managed)) {
    throw "Could not find KSP managed assemblies at '$managed'. Pass -KspRoot with your KSP 1 install path."
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdkLine = & $dotnet --list-sdks | Select-Object -Last 1
if ($sdkLine -notmatch "^(\S+)\s+\[(.+)\]$") {
    throw "Could not parse dotnet SDK line '$sdkLine'."
}

$sdkPath = Join-Path $Matches[2] $Matches[1]
$compiler = Join-Path $sdkPath "Roslyn\bincore\csc.dll"

if (-not (Test-Path $compiler)) {
    throw "Could not find Roslyn compiler at '$compiler'."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$references = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "Assembly-CSharp.dll",
    "Assembly-CSharp-firstpass.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.UIModule.dll"
) | ForEach-Object { "/reference:" + (Join-Path $managed $_) }

$sources = Get-ChildItem -Path $sourceDir -Filter "*.cs" -File | ForEach-Object { $_.FullName }

& $dotnet exec $compiler `
    /noconfig `
    /nostdlib+ `
    /target:library `
    /optimize+ `
    /debug- `
    /langversion:7.3 `
    /out:$outputDll `
    $references `
    $sources

$pdb = [System.IO.Path]::ChangeExtension($outputDll, ".pdb")
if (Test-Path $pdb) {
    Remove-Item -LiteralPath $pdb -Force
}

Write-Host "Built $outputDll"
