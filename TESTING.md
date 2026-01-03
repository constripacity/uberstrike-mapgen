# Testing and verification

## Python toolchain
```bash
cd DesktopAgent
python -m pip install -r ../requirements.txt  # or install numpy scipy pillow networkx matplotlib pytest ruff
pytest DesktopAgent/tests
```

Tests are deterministic and seed-aware (see `agent/utils/seed.py`).

## Unity editor sanity
1. Open `UberStrikeGen` in Unity 6000.2.6f2 (or compatible 6.0.x).
2. Ensure asmdefs compile:
   - `Assets/_UberStrike/Scripts/UberStrikeGen.Editor.asmdef` (Editor-only)
   - `Assets/_UberStrike/Scripts/Runtime/UberStrikeGen.Runtime.asmdef`
   - `Assets/_UberStrike/Tests/UberStrikeGen.Tests.asmdef`
3. Run **Assets → Open C# Project** or trigger a domain reload to confirm clean compile.
4. Optionally run **Tools/UberStrike/MapGen/Import From Legend PNG…** on a small legend to validate editor scripts.

For headless Unity, see `VERIFY.md`.
