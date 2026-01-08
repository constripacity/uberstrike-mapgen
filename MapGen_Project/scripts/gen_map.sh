#!/bin/bash
PROJECT_PATH="$(dirname "$0")/.."
UNITY_EXE="/Applications/Unity/Hub/Editor/6000.0.24f1/Unity.app/Contents/MacOS/Unity"

# Linux path optional fallback
if [ ! -f "$UNITY_EXE" ]; then
    UNITY_EXE=unity
fi

echo "[GenMap] Project: $PROJECT_PATH"

"$UNITY_EXE" -batchmode -quit -projectPath "$PROJECT_PATH" -executeMethod MapGen.Editor.CLI.HeadlessBuilder.Build -logFile - "$@"

RET=$?
if [ $RET -eq 0 ]; then
    echo "[GenMap] Success (Code 0)"
elif [ $RET -eq 2 ]; then
    echo "[GenMap] Success with Warnings (Code 2)"
else
    echo "[GenMap] Failed (Code $RET)"
fi

exit $RET
