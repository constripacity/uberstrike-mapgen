param(
    [string]$stack,
    [string]$outDir,
    [string]$themeAsset,
    [string]$ubervocab,
    [string]$seed,
    [switch]$verbose
)

$UnityPath = $env:UNITY_EXE
if (-not $UnityPath) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe"
}

$ProjectPath = Resolve-Path "$PSScriptRoot\.."

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity executable not found at $UnityPath"
    exit 1
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
