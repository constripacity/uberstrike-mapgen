# VERIFY (v0.7)

## Unity editor/domain reload
1. Open `UberStrikeGen` with Unity 6000.2.6f2 (or 6.0.x equivalent).
2. Confirm assemblies compile:
   - `Assets/_UberStrike/Scripts/UberStrikeGen.Runtime.asmdef`
   - `Assets/_UberStrike/Scripts/UberStrikeGen.Editor.asmdef`
   - `Assets/_UberStrike/Tests/UberStrikeGen.Tests.asmdef`
3. Run a quick legend import:
   - `Tools/UberStrike/MapGen/Import From Legend PNG…`
   - Validate that floors/walls generate and no duplicate-type errors appear.

## Headless sanity (best-effort)
```
unity-editor -batchmode -quit -projectPath ./UberStrikeGen -executeMethod UnityAI.BatchProcessor.GenerateMap -seed 123 -blueprint Assets/_Generated/Maps/cli/legend.png -output Assets/_Generated/Maps/cli/Generated.prefab
```
*If licensing blocks execution, ensure the method resolves and arguments parse without editor errors.*

## Python algorithms
```
cd DesktopAgent
python -m pip install numpy scipy pillow networkx matplotlib pytest
pytest DesktopAgent/tests
```
