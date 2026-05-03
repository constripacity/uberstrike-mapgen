# UberStrike MapGen Tools

## Prefab Setup
1. In Unity, open **Tools → UberStrike → MapGen → Open Prefab Catalog**.
2. Assign real gameplay prefabs (spawn, jump pad, teleporter, health, armor) and materials for water/glass.
3. Tune the default wall height value to match your blockout scale.

### Persistent Asset Catalog
- Open **Tools → UberStrike → Asset Catalog Builder** to scan `Assets/UberStrike` and write a persistent catalog asset under `Assets/_UberStrike/Data/`. The catalog avoids repeated `FindAssets` scans and feeds PrefabPlacementAI and theming with curated prefab/material lists.

## Legend Generation Workflow
1. Open **Tools → UberStrike → MapGen → Generator Window**.
2. Pick size, seed, and teleporter pairs, then click **Generate Legend PNG** to save into `Assets/_Generated/Maps/Editor`.
3. With a legend selected (or by browsing to a PNG), click **Build Scene from Selected Legend** to spawn geometry and prefabs.

## Stack Generator v0.6
1. Open **Tools → UberStrike → MapGen → Stack Generator v0.6 → Upgrade Stack JSON…**.
2. Pick any legacy stack JSON file; the tool will synthesize missing height/flow/theme/lighting/collision layers plus a `_v0p6.json` output under `auto_layers/`.
3. Generated textures are authored with deterministic seeds so rerunning yields stable data that can be versioned.

Use the **Generate Lighting From Layout** option when you only need to stamp a quick lighting PNG from a hand-painted layout.

## Dataset Export
1. Load a scene you want to capture.
2. Run **Tools → UberStrike → MapGen → Export Dataset (Current Scene)** for a single map or **Export All Maps** to iterate every `.unity` scene.
3. Outputs land under `Assets/_Generated/Maps/<SceneName>/` with `legend.png`, `height.png`, and `map.json` (QC + instances).

## Pattern Extraction
1. Use **Tools → UberStrike → MapGen → Extract Patterns (Active Scene)** for a quick JSON dump of the current scene's spawn/flow/height profile.
2. Use **Extract Patterns (All Scenes)** to iterate every `.unity` scene and write a timestamped JSON file to `Assets/_Generated/Patterns/`.
3. Feed these JSON files into the DesktopAgent or external ML notebooks to learn playstyle clusters (Arena vs CTF vs Deathmatch).

## Voronoi Theme Generation
- Open **Tools → UberStrike → MapGen → Voronoi Theme Generator** to auto-synthesize a theme PNG using Poisson-disk seeds, smoothed Voronoi cells, and weighted theme assignments that respect an optional layout mask.
- Stack builds now auto-generate a theme layer when one is missing; the Voronoi generator also exposes a CLI in `DesktopAgent/agent/tools/voronoi_theme_generator.py` and a batch helper via `MapGenOrchestrator.generate_theme_variants`.

## Wave Function Collapse (WFC) Layout Fixes
- Open **Tools → UberStrike → MapGen → Wave Function Collapse Generator** to author architecturally valid layouts with enforced socket rules (walls, doors, bridges, spawns) and optional connectivity guarantees.
- The main blueprint importer auto-runs a lightweight WFC pre-pass to repair impossible geometry (floating walls, disconnected floors) while honoring spawn/water constraints.
- Headless generation is available via `python DesktopAgent/agent/tools/wave_function_collapse.py --width 64 --height 64 --spawns 2 --output wfc_map.png`.

### WFC Tileset Test Harness
A pure-Python harness mirrors `WFCCore.cs` so tileset/socket changes can be A/B tested outside Unity. Edit `Tools/MapGen/wfc_harness.py` to add a new `Variant`, then:
```powershell
python Tools/MapGen/wfc_harness.py --variant all --size 16 --seeds 16
python Tools/MapGen/wfc_harness.py --variant wall_interior_tuned --size 32 --seeds 16
```
Outputs PNGs under `Tools/MapGen/_harness_out/<variant>/`. Reports per-variant convergence rate, contradiction/disconnect counts, average restarts, and tile-mix percentages. Use this before touching `WFCCore.BaseTiles` or `SocketsMatch` so weight or socket changes are validated against a wide seed sweep first.

## Graph Flow Analysis
- Run **Tools → UberStrike → MapGen → Graph Flow Analyzer** to evaluate chokepoints, dead zones, heat maps, spawn balance, circulation loops, and exposure maps from the live scene.
- The analyzer can auto-warn when spawn balance is poor; use it alongside PrefabPlacementAI or a regeneration pass to rebalance.
- A CLI-oriented analyzer lives at `DesktopAgent/agent/tools/graph_flow_analyzer.py` and exports JSON/visualizations for offline QC pipelines.

## Adaptive LOD
- Open **Tools → UberStrike → MapGen → Adaptive LOD Generator** to batch-create LODGroups for every mesh under the selected map using importance-mapped falloffs around spawns, items, and chokepoints.
- The generator honors per-level quality sliders and can sweep all prefabs under `Assets/_UberStrike/Maps/` for quick performance passes.
- For offline mesh decimation and reporting, use the CLI helper at `DesktopAgent/agent/tools/adaptive_lod_optimizer.py --input mesh.obj --output optimized.obj`.

## Batch & Orchestration
- Create tournament-sized batches via `DesktopAgent/agent/tools/batch_generator.py --unity <Unity.exe> --project <path> --count 25 --name arena` to fan out blueprint/theme/item permutations and filter by QC score.
- Use `DesktopAgent/agent/tools/master_orchestrator.py --name Demo --style arena --size medium --players 8 --complexity complex` for a single end-to-end run (WFC layout, Voronoi theming, simulated-annealing placement, flow analysis, LOD importance maps) or add `--batch 10` to produce multiple variants and emit JSON + PNG bundles under `Generated_Maps/`.

## CLI
Run batch generation from PowerShell:
```powershell
.\Tools\MapGen\Gen.ps1 -seed 42 -size 128 -t 2
```
This launches Unity in batchmode, writes a legend PNG under `Assets/_Generated/Maps/CLI/`, and instantiates the scene with prefabs.

## Web Preview
Serve `Tools/WebPreview/index.html` (for example `npx serve Tools/WebPreview`) to inspect exported maps in a Three.js scene, copy share links, and record local votes.

## Future Work
- Themes JSON authoring for prefab/material swaps.
- Heightmap shaping passthrough from secondary grayscale PNGs.
- Wave Function Collapse refinement pipeline for alternative layouts.
- Deeper QC metrics (sightlines, coverage, navigation stress tests).
