# MapGen Project (Clean Unity Architecture)

This project contains the modernized UberStrike Map Generation pipeline. It is designed for Unity 6 URP and supports both Editor-based and Headless (Batch) generation.

## 📂 Project Structure
*   **Assets/MapGen/Core**: Runtime logic (Map definition, Geometry generation, Theme application).
    *   `StackDefinition.cs`: Defines the map layers.
    *   `GreyboxBuilder.cs`: The core generator engine with Greedy Meshing.
    *   `GreedyMesher.cs`: Optimizes voxel grids into minimal quads.
*   **Assets/MapGen/Editor**: Editor tools.
    *   `MapGenEditorWindow.cs`: The "MapGen > Generator Window" UI.
    *   `CLI/HeadlessBuilder.cs`: The entry point for command-line builds.

## 🚀 How to Use (Editor)
1.  Open **Window > MapGen > Generator Window**.
2.  **Load Stack**: Select a `.stack.json` file (from blueprints).
3.  **Select Theme**: (Optional) Drag a `ThemeDefinition` asset.
4.  **Generat**: Click "Build Greybox".

## 🤖 How to Use (CLI / Agent)
This project includes scripts for headless generation in `scripts/`:
*   `gen_map.bat` (Windows)
*   `gen_map.ps1` (PowerShell - supports env vars)
*   `gen_map.sh` (Linux/Mac)

**Example:**
```bash
./scripts/gen_map.bat -stack "path/to/map.stack.json" -outDir "Assets/_Generated/MyMap"
```

## 🛠 Design Notes
*   **Separation of Concerns**: Core logic is pure C# (mostly) and separated from Editor glue.
*   **Greedy Meshing**: Drastically reduces triangle count for physics and rendering.
*   **FlowTokens**: Gameplay elements (Spawns, Pickups) are resolved via `UberVocab` to Prefabs.
