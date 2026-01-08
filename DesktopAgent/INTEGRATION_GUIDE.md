# Desktop Agent Integration Guide (Mission 7)

## Overview
The Desktop Agent (v2) now supports generating Unity maps via the `build` command. This integration wraps the Unity Headless Builder, providing a robust Python interface for AI-driven generation.

## 🔧 Setup
1.  **Unity Path**: The agent attempts to auto-discover Unity. To enforce a specific path, create `DesktopAgent/config/local.json`:
    ```json
    {
        "unity_path": "C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor/Unity.exe"
    }
    ```
    *Alternatively, set the `UNITY_EXE` environment variable.*

## 💻 CLI Usage
Run the following from the repo root:

```bash
python DesktopAgent/run_assistant.py build [OPTIONS]
```

### Options
| Flag | Description | Required | Default |
| :--- | :--- | :---: | :---: |
| `--stack` | Path to `.stack.json` blueprint | ✅ | - |
| `--out-dir` | Output directory (e.g., `Assets/_Generated/Test`) | ✅ | - |
| `--seed` | Random seed for generation | ❌ | None |
| `--theme` | Path to ThemeDefinition asset | ❌ | Default |
| `--ubervocab` | Path to ubervocab.json | ❌ | Default |
| `--use-ps1` | Use PowerShell script (Windows only) | ❌ | False |
| `--unity-log` | Custom path for Unity log file | ❌ | In OutDir |
| `--open` | Open output folder on success (Windows) | ❌ | False |

### Example
```bash
python DesktopAgent/run_assistant.py build \
  --stack "UberStrikeGen/Assets/_UberStrike/Blueprints/Stacks/ArenaStack_Sample.stack.json" \
  --out-dir "Assets/_Generated/MyMap" \
  --seed 42 \
  --open
```

## 🧠 Architecture
1.  **Agent**: `run_assistant.py` parses args.
2.  **Pipeline**: `headless_pipeline.py` prepares environment and calls script.
3.  **Shell**: `gen_map.bat/.ps1/.sh` launches Unity in `-batchmode`.
4.  **Unity**: `HeadlessBuilder.cs` loads assets, builds map, saves scene.
5.  **Report**: `build_report.json` is written to `out-dir`.
6.  **Agent**: Reads report and returns verification stats.
