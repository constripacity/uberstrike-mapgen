param(
    [string]$stack,
    [string]$outDir,
    [string]$themeAsset,
    [string]$ubervocab,
    [string]$seed,
    [string]$buildBundle,
    [string]$bundleTarget,
    [string]$bundleOutDir,
    [switch]$verbose
)

$UnityPath = $env:UNITY_EXE
if (-not $UnityPath) {
    # Default fallback
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.0.56f1\Editor\Unity.exe"
}

$ProjectPath = Resolve-Path "$PSScriptRoot\.."

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity executable not found at $UnityPath"
    exit 1
}

# Resolve Log Path
$LogPath = Join-Path $outDir "unity_editor.log"
# Ensure directory exists for log
$LogDir = Split-Path $LogPath -Parent
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$ArgsList = @(
    "-batchmode",
    "-quit",
    "-nographics",
    "-projectPath", "$($ProjectPath.Path)",
    "-executeMethod", "MapGen.Editor.CLI.HeadlessBuilder.Build",
    "-logFile", "-"
)

if ($stack) { $ArgsList += "-stack"; $ArgsList += """$stack""" }
if ($outDir) { $ArgsList += "-outDir"; $ArgsList += """$outDir""" }
if ($themeAsset) { $ArgsList += "-themeAsset"; $ArgsList += """$themeAsset""" }
if ($ubervocab) { $ArgsList += "-ubervocab"; $ArgsList += """$ubervocab""" }
if ($seed) { $ArgsList += "-seed"; $ArgsList += """$seed""" }
if ($buildBundle) { $ArgsList += "-buildBundle"; $ArgsList += """$buildBundle""" }
if ($bundleTarget) { $ArgsList += "-bundleTarget"; $ArgsList += """$bundleTarget""" }
if ($bundleOutDir) { $ArgsList += "-bundleOutDir"; $ArgsList += """$bundleOutDir""" }

Write-Host "[GenMap] Starting Unity..." -ForegroundColor Cyan
if ($verbose) { Write-Host "Command: & $UnityPath $ArgsList" }

& $UnityPath $ArgsList

if ($LASTEXITCODE -eq 0) {
    Write-Host "[GenMap] Success (Code 0)" -ForegroundColor Green
}
elseif ($LASTEXITCODE -eq 2) {
    Write-Host "[GenMap] Success with Warnings (Code 2)" -ForegroundColor Yellow
}
else {
    Write-Host "[GenMap] Failed (Code $LASTEXITCODE)" -ForegroundColor Red
}

$ReportPath = Join-Path $outDir "build_report.json"
if (Test-Path $ReportPath) {
    Write-Host "Report provided at: $ReportPath"
}
exit $LASTEXITCODE
