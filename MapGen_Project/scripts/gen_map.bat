@echo off
set ELEVATE_CMD=
set PROJECT_PATH=%~dp0..
if not defined UNITY_EXE set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.0.24f1\Editor\Unity.exe"

REM Check skipped, assuming correct path or overridden by Env Var.

echo [GenMap] Running Headless Builder...
echo [GenMap] Project: %PROJECT_PATH%

"%UNITY_EXE%" -batchmode -quit -nographics -projectPath "%PROJECT_PATH%" -executeMethod MapGen.Editor.CLI.HeadlessBuilder.Build -logFile - %*

echo.
echo [GenMap] Done.
echo.
echo [GenMap] Done.
