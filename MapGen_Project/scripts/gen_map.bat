@echo off
set ELEVATE_CMD=
set PROJECT_PATH=%~dp0..
REM Try Unity 2022 first (UberStrike 4.3 mapgen), then Unity 6
if not defined UNITY_EXE (
    if exist "C:\Program Files\Unity\Hub\Editor\2022.3.40f1\Editor\Unity.exe" (
        set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.40f1\Editor\Unity.exe"
    ) else if exist "C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Unity.exe" (
        set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Unity.exe"
    ) else (
        set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.0.56f1\Editor\Unity.exe"
    )
)

REM Check skipped, assuming correct path or overridden by Env Var.

echo [GenMap] Running Headless Builder...
echo [GenMap] Project: %PROJECT_PATH%

"%UNITY_EXE%" -batchmode -quit -nographics -projectPath "%PROJECT_PATH%" -executeMethod MapGen.Editor.CLI.HeadlessBuilder.Build -logFile - %*

echo.
echo [GenMap] Done.
echo.
echo [GenMap] Done.
