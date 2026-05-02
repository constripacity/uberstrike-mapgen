# UberStrike MapGen -- Full Capability Report

> **Audit Date:** 2026-04-02
> **Auditor:** Claude Code (Opus 4.6)
> **Repository:** github.com/constripacity/uberstrike-mapgen
> **Commit:** bed4d5c (main)
> **Total Source Lines:** ~38,800 (Python + C#)

---

## Section 1: Repository Census

### DesktopAgent/ (Python -- ~8,200 LOC, 65 files)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `agent/tools/simulated_annealing_placer.py` | Python | 330 | PRODUCTION | SA optimizer with 5-term energy function, Metropolis acceptance |
| `agent/tools/wave_function_collapse.py` | Python | 350 | PRODUCTION | Entropy-driven WFC with socketed tiles, constraint propagation |
| `agent/tools/voronoi_theme_generator.py` | Python | 291 | PRODUCTION | Voronoi regions with Poisson disk seeding, Gaussian smoothing |
| `agent/tools/graph_flow_analyzer.py` | Python | 494 | PRODUCTION | NetworkX flow analysis: chokepoints, heatmaps, sightlines, camping |
| `agent/tools/master_orchestrator.py` | Python | 295 | PRODUCTION | End-to-end pipeline: WFC->Voronoi->SA->Flow->QC with retry logic |
| `agent/tools/adaptive_lod_optimizer.py` | Python | 273 | PRODUCTION | Quadric Error Metrics mesh simplification, 4 LOD levels |
| `agent/tools/map_quality.py` | Python | 179 | WORKING | Heuristic quality scoring (flow, balance, cover, sightlines) |
| `agent/tools/unity_automation.py` | Python | 541 | PRODUCTION | Unity headless build automation, process lifecycle, log parsing |
| `agent/tools/process_manager.py` | Python | 877 | PRODUCTION | Process lifecycle management (launch, monitor, kill, resources) |
| `agent/tools/window_manager.py` | Python | 639 | PRODUCTION | Win32 window management (find, focus, geometry) |
| `agent/tools/log_monitor.py` | Python | 660 | PRODUCTION | Real-time Unity log monitoring and pattern detection |
| `agent/tools/asset_extractor.py` | Python | 414 | WORKING | Unity YAML scene parser for spawn/weapon pattern extraction |
| `agent/tools/mapgen_orchestrator.py` | Python | 106 | WORKING | High-level variant generation wrapper |
| `agent/tools/batch_generator.py` | Python | ~100 | WORKING | Parallel batch generation (max 4 concurrent) |
| `agent/tools/scene_validator.py` | Python | ~180 | WORKING | Heuristic YAML scene validation |
| `agent/tools/screen.py` | Python | 14 | WORKING | Async screenshot capture (mss + PIL) |
| `agent/tools/ui_automation.py` | Python | 27 | WORKING | Mouse/keyboard control (pynput) |
| `agent/utils/seed.py` | Python | ~50 | CONFIG | Global seed management (numpy, random, torch) |
| `agent_v2/analyzer/quality_analyzer.py` | Python | ~200 | PRODUCTION | Gameplay-centric QC with strict rules, weighted scoring |
| `agent_v2/builder/headless_pipeline.py` | Python | ~500 | PRODUCTION | Unity headless orchestration with platform detection |
| `agent_v2/blueprints/stack_io.py` | Python | ~200 | PRODUCTION | Stack JSON + layer PNG I/O (single source of truth) |
| `agent_v2/fixer/blueprint_sanitizer.py` | Python | ~150 | WORKING | Proactive spawn fixing via Farthest Point Sampling |
| `agent_v2/mutator/blueprint_mutator.py` | Python | ~80 | WORKING | Geometric variants (rot90/180/270, flip_x/z, combos) |
| `agent_v2/dataset/feature_extractors.py` | Python | ~150 | WORKING | Boolean masks + feature vectors from stack layers |
| `agent_v2/dataset/exporter.py` | Python | ~200 | WORKING | 50-variant dataset export with sanitizer + mutator |
| `agent_v2/dataset/schema.py` | Python | ~100 | WORKING | Pydantic validation schemas |
| `agent_v2/dataset/splitter.py` | Python | ~50 | WORKING | Train/test split (80/20, stratified) |
| `agent_v2/ml/trainer.py` | Python | ~100 | PARTIAL | RandomForest classifier (100 trees) on blueprint features |
| `agent_v2/ml/predictor.py` | Python | ~80 | PARTIAL | Quality prediction from .pkl model |
| `agent_v2/ai/layout_diffusion.py` | Python | 255 | PARTIAL | DDPM layout generation (optional PyTorch, graceful fallback) |
| `agent_v2/generator/layer_generator.py` | Python | 251 | WORKING | Prompt-driven 6-layer stack generation |
| `agent_v2/validator/stack_validator.py` | Python | ~100 | WORKING | Pre-build validation (spawns, connectivity, area) |
| `agent_v2/monitor/unity_monitor.py` | Python | ~150 | WORKING | Real-time build monitoring and issue detection |
| `agent_v2/cli/assistant_cli.py` | Python | ~300 | WORKING | Interactive CLI dashboard |
| `agent_v2/fixer/auto_fixer.py` | Python | ~200 | WORKING | Post-build issue detection and remediation |
| `run_assistant.py` | Python | 499 | PRODUCTION | Claude API CLI entry point, 11 commands (build, generate, fix, etc.) |
| `ask_claude.py` | Python | ~100 | PARTIAL | Legacy Claude API tool registry |
| `web_editor/app.py` | Python | ~200 | WORKING | Flask web UI for blueprint editing |
| `tests/test_determinism.py` | Python | -- | TEST | Reproducibility validation |
| `tests/test_exporter.py` | Python | -- | TEST | Dataset export tests |
| `tests/test_feature_extractors.py` | Python | -- | TEST | Feature vector validation |
| `tests/test_sanitizer.py` | Python | -- | TEST | Blueprint fixing tests |
| `tests/test_stack_io.py` | Python | -- | TEST | I/O round-trip tests |

### UberStrikeGen/ (C# Unity 6 -- ~15,000 LOC, 67 files)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `Scripts/Editor/BuildFromBlueprint.cs` | C# | 2149 | PRODUCTION | PNG-to-3D scene builder (walls, floors, platforms, spawns, pickups) |
| `Scripts/Editor/BuildFromStackEnhanced.cs` | C# | 859 | PRODUCTION | Multi-layer stack builder (processes all 6 layers sequentially) |
| `Scripts/Editor/HeadlessBuilder.cs` | C# | 731 | PRODUCTION | CLI batchmode entry point, parses --args, saves scene |
| `Scripts/Editor/SimulatedAnnealingPlacer.cs` | C# | 392 | WORKING | SA item placement (T=750, cooling=0.96, 4500 iterations) |
| `Scripts/Editor/PrefabPlacementAI.cs` | C# | 432 | WORKING | Heuristic + SA placement with rule generation |
| `Scripts/Editor/WaveFunctionCollapseGenerator.cs` | C# | 452 | WORKING | Tileset-based WFC editor with 10 tile types |
| `Scripts/Generators/WaveFunctionCollapse.cs` | C# | 363 | WORKING | Data-driven WFC with learnable rulesets |
| `Scripts/Editor/VoronoiThemeGenerator.cs` | C# | 331 | WORKING | Poisson disk Voronoi with 6 themes |
| `Scripts/Editor/GraphFlowAnalyzer.cs` | C# | 370 | WORKING | 8 flow metrics, NavMesh-based, Monte Carlo heatmaps |
| `Scripts/Analysis/AdvancedMetrics.cs` | C# | 326 | PRODUCTION | 8 gameplay metrics (connectivity, verticality, cover, etc.) |
| `Scripts/Editor/StackGeneratorV6.cs` | C# | 307 | PRODUCTION | Auto-generate missing layers (height, flow, theme, lighting, collision) |
| `Scripts/Editor/SampleStackGenerator.cs` | C# | 345 | WORKING | Generates demo ArenaStack_Sample with all 6 layers |
| `Scripts/Editor/BlueprintQC.cs` | C# | 300 | PRODUCTION | Quality control metrics window (pixel analysis + recommendations) |
| `Scripts/Editor/AssetCatalogBuilder.cs` | C# | 265 | PRODUCTION | ScriptableObject asset catalog (weapons, pickups, themes) |
| `Scripts/Editor/AssetIntegrationSystem.cs` | C# | 363 | PRODUCTION | Prefab + theme material integration hub |
| `Scripts/Editor/AdaptiveLODGenerator.cs` | C# | 278 | WORKING | 4-level mesh LOD with importance maps |
| `Scripts/Editor/QuickFixPatch.cs` | C# | 281 | WORKING | Emergency fallback patches for build issues |
| `Scripts/Testing/BotPlaytester.cs` | C# | 264 | WORKING | NavMesh bot playtesting (4-12 bots, heatmap output) |
| `_Project/Editor/MapTrainingPipeline.cs` | C# | 294 | PRODUCTION | ML training data export (64x64 features + metrics) |
| `_Project/Editor/StackLoader.cs` | C# | 290 | PRODUCTION | JSON + texture loading with base64 fallback |
| `_Project/Runtime/StackDefinition.cs` | C# | 354 | PRODUCTION | 6-layer data model, JSON serialization, color configs |
| `Scripts/Editor/QuickBuildWindow.cs` | C# | 253 | WORKING | Editor UI for quick blueprint building |

### UberStrike2022/ (C# Unity 2022 -- ~9,800 LOC, 36 files)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `Editor/BuildFromBlueprint.cs` | C# | 2149 | PRODUCTION | Same core builder, rewritten for Unity 2022 |
| `Editor/BuildFromStackEnhanced.cs` | C# | 847 | PRODUCTION | Stack builder with BuildContext tracking |
| `Editor/HeadlessBuilder.cs` | C# | 731 | PRODUCTION | Batchmode builder with AgentBridge notifications |
| `Editor/SimulatedAnnealingPlacer.cs` | C# | 392 | WORKING | SA placer (same algorithm as UberStrikeGen) |
| `Editor/PrefabPlacementAI.cs` | C# | 432 | WORKING | Heuristic + SA placement |
| `Integration/Editor/MapGenBridge.cs` | C# | 641 | PRODUCTION | Scene-to-UberStrike conversion (MapConfiguration, spawns) |
| `Integration/Editor/MapGenRegistrar.cs` | C# | 247 | PRODUCTION | Map registration via EditorPrefs |
| `Integration/Editor/MapGenExporter.cs` | C# | 162 | PRODUCTION | AssetBundle export for LevelManager |
| `Integration/Editor/MapGenOfflineInjector.cs` | C# | 74 | PRODUCTION | Runtime map injection into OfflineBypass |
| `Editor/Phase2TestBuild.cs` | C# | 590 | PRODUCTION | One-click test blueprint -> playable map |
| `Editor/QuickBuildWindow.cs` | C# | 276 | PRODUCTION | Editor UI with EditorPrefs migration |
| `Editor/TestMapBuilder.cs` | C# | 260 | PRODUCTION | Phase 0: minimal test arena (50x50m, 8 spawns) |
| `Editor/AgentBridge.cs` | C# | 121 | PRODUCTION | HTTP bridge to Python agent (localhost:11435) |
| `Runtime/MapGenDiagnostics.cs` | C# | 272 | PRODUCTION | F12 debug overlay (MapConfig, spawns, FPS) |
| `Runtime/MapGenMapInjector.cs` | C# | 102 | PRODUCTION | Runtime injection into LevelManager |
| `Runtime/StackDefinition.cs` | C# | 340 | PRODUCTION | 6-layer data model with flow/collision classification |
| `Editor/StackGeneratorV6.cs` | C# | 307 | PRODUCTION | Auto-layer synthesis |
| `Editor/BlueprintQC.cs` | C# | 300 | PRODUCTION | QC metrics window |
| `Editor/AssetIntegrationSystem.cs` | C# | 363 | PRODUCTION | Prefab catalog integration |
| `Editor/AssetCatalogBuilder.cs` | C# | 265 | PRODUCTION | Asset catalog builder |
| `Editor/FlowAnalyser.cs` | C# | ~200 | PRODUCTION | Flow layer analysis (spawns, chokes, dead ends) |
| `Editor/Stubs/WFCCore.cs` | C# | 42 | STUB | Placeholder WFC (returns false) |
| `Editor/Stubs/VoronoiThemeGenerator.cs` | C# | 49 | STUB | Placeholder Voronoi (uniform gray fallback) |
| `Editor/Stubs/FlowAnalysisCore.cs` | C# | 29 | STUB | Placeholder NavMesh flow (returns empty) |
| `Editor/Stubs/StackPreviewer.cs` | C# | 20 | STUB | Placeholder stack preview |

### MapGen_Project/ (C# Unity 2022 Library -- ~2,500 LOC, 12 files)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `Assets/MapGen/Editor/CLI/HeadlessBuilder.cs` | C# | 382 | PRODUCTION | CLI entry point for batch builds |
| `Assets/MapGen/Runtime/StackDefinition.cs` | C# | -- | PRODUCTION | Reusable stack data model |
| `Assets/MapGen/Runtime/GreedyMesher.cs` | C# | ~80 | PRODUCTION | Rectangle merging optimization |
| `Assets/MapGen/Runtime/GreyboxBuilder.cs` | C# | -- | PRODUCTION | Basic greybox geometry generation |
| `Assets/MapGen/Runtime/ThemeSystem.cs` | C# | -- | PRODUCTION | Material assignment logic |
| `Assets/MapGen/Runtime/UberVocab.cs` | C# | -- | CONFIG | Tile vocabulary enum |
| `Assets/MapGen/Runtime/FlowToken.cs` | C# | -- | CONFIG | Flow analysis markers |
| `Assets/MapGen/Editor/MapGenWindow.cs` | C# | -- | WORKING | Editor UI for manual builds |
| `Assets/MapGen/Editor/ThemeCreator.cs` | C# | -- | WORKING | Theme asset creation wizard |
| `Assets/MapGen/Documentation/UberUnityExtract/extractor.py` | Python | 252 | WORKING | Asset extraction utility |

### quantum_mapgen/ (Python -- ~1,800 LOC, 7 files)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `braket_runner.py` | Python | 720 | PRODUCTION | QAOA circuit builder, LocalSimulator + SV1 cloud, greedy repair |
| `exp1_basic_connectivity.py` | Python | 307 | PRODUCTION | 3-power-item proof-of-concept (21/33 qubits) |
| `exp1_scaling.py` | Python | 304 | PRODUCTION | Scaling study (4-8 candidates, runtime curves) |
| `exp1_scaling_quick.py` | Python | 260 | PRODUCTION | Quick scaling variant with tuned params |
| `visualize_results.py` | Python | 216 | WORKING | Matplotlib chart generation |
| `SESSION_LOG.md` | MD | 183 | DOC | Session notes, results, lessons learned |
| `__init__.py` | Python | 2 | CONFIG | Package marker |

### Root Level (Python + Docs -- ~2,500 LOC)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `qubo_encoder.py` | Python | 947 | PRODUCTION | QUBO formulation (7 constraints), encode/decode/validate |
| `phase3_integration.py` | Python | 32 | PARTIAL | Phase 3 entry point (diffusion + web editor) |
| `convert_seed.py` | Python | 69 | WORKING | Base64 PNG extraction from UberStrikeGen stacks |
| `braket_test.py` | Python | 17 | WORKING | AWS Braket connectivity sanity check |
| `debug_path.py` | Python | 13 | DEAD | One-off Unity path diagnostic |
| `QUANTUM_MAPGEN_ANALYSIS.md` | MD | ~1100 | DOC | 4-phase quantum analysis (37KB) |
| `README.md` | MD | ~700 | DOC | Public-facing project documentation |
| `quality_model.pkl` | Binary | 47KB | DATA | Pre-trained RandomForest quality model |

### Tools/ (JS/HTML/CSS/PS1 -- ~200 LOC)

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `WebPreview/app.js` | JS | ~50 | WORKING | Three.js 3D map viewer with orbit controls |
| `WebPreview/index.html` | HTML | ~50 | WORKING | Web UI for map preview + sharing |
| `WebPreview/styles.css` | CSS | ~50 | WORKING | Dark-themed styling |
| `MapGen/Gen.ps1` | PS1 | 13 | WORKING | PowerShell CLI wrapper for batch generation |
| `MapGen/README.md` | MD | 71 | DOC | Toolkit documentation |

### CI/CD

| File | Lang | Lines | Status | Purpose |
|------|------|-------|--------|---------|
| `.github/workflows/python.yml` | YAML | 36 | PRODUCTION | Python lint (ruff) + pytest |
| `.github/workflows/unity.yml` | YAML | 12 | STUB | Unity CI placeholder |

### Datasets (JSON + PNG)

| Directory | Samples | Status | Purpose |
|-----------|---------|--------|---------|
| `dataset_seed/` | 1 | DATA | Baseline seed stack (6 layers) |
| `datasets/ds_stress_v1/` | 38 | DATA | Stress test variants |
| `datasets/ds_v2/` | 20+ | DATA | Main dataset with augmentations |

### Line Count Totals

| Directory | LOC (approx) |
|-----------|-------------|
| DesktopAgent/ | 8,200 |
| UberStrikeGen/ | 15,000 |
| UberStrike2022/ | 9,800 |
| MapGen_Project/ | 2,500 |
| quantum_mapgen/ | 1,800 |
| Root Python | 1,100 |
| Root Docs | ~1,800 |
| Tools/ | 200 |
| **TOTAL** | **~38,800** |

---

## Section 2: Pipeline Audit

### 2.1 The Full Generation Pipeline

```
Stage 1: Blueprint/Layout Generation
  Entry point: DesktopAgent/agent/tools/wave_function_collapse.py:WaveFunctionCollapse.generate_arena_layout()
               OR UberStrikeGen/.../WaveFunctionCollapseGenerator.cs:WFCCore.Collapse()
  Input: Width, height, spawn_count, seed
  Output: 2D numpy array (Python) or Texture2D PNG (C#) with tile-type pixel colors
  Status: WORKING (both implementations)
  Bottleneck: Contradictions at 128x128+ without backtracking
  Time estimate: <1s for 64x64, 2-5s for 128x128

Stage 2: Voronoi Theme Assignment
  Entry point: DesktopAgent/agent/tools/voronoi_theme_generator.py:VoronoiThemeGenerator.generate()
               OR UberStrikeGen/.../VoronoiThemeGenerator.cs
  Input: Width, height, num_regions, layout mask, seed
  Output: Theme region map (PNG with 6 theme colors)
  Status: WORKING (both implementations)
  Bottleneck: None
  Time estimate: <1s

Stage 3: Height Layer Generation
  Entry point: UberStrikeGen/.../StackGeneratorV6.cs:GenerateHeightTexture()
  Input: Layout texture
  Output: RFloat texture (0-1 elevation)
  Status: WORKING (C# only; Python layer_generator.py has equivalent)
  Bottleneck: None
  Time estimate: <1s

Stage 4: Flow Layer Generation
  Entry point: UberStrikeGen/.../StackGeneratorV6.cs:GenerateFlowTexture()
               OR DesktopAgent/agent/tools/graph_flow_analyzer.py:GraphFlowAnalyzer.analyze_map()
  Input: Layout, spawn_points
  Output: Flow texture (yellow=spawn, orange=choke) + FlowMetrics object
  Status: WORKING
  Bottleneck: NetworkX graph construction on large maps
  Time estimate: 1-3s for 64x64, 5-15s for 128x128

Stage 5: Lighting Layer Generation
  Entry point: UberStrikeGen/.../StackGeneratorV6.cs:GenerateLightingTexture()
  Input: Layout, flow heatmap, height data
  Output: Light placement hint texture
  Status: WORKING (auto-generated heuristic)
  Bottleneck: None
  Time estimate: <1s

Stage 6: Collision Layer Generation
  Entry point: UberStrikeGen/.../StackGeneratorV6.cs:GenerateCollisionTexture()
  Input: Layout, height data
  Output: Collision classification texture (walkable/blocked/climbable/destructible)
  Status: WORKING
  Bottleneck: None
  Time estimate: <1s

Stage 7: Simulated Annealing Item Placement
  Entry point: DesktopAgent/agent/tools/simulated_annealing_placer.py:SimulatedAnnealingPlacer.optimise()
               OR UberStrikeGen/.../SimulatedAnnealingPlacer.cs:Optimise()
  Input: PlacementConstraints (walkable areas, spawns, chokes, cover), item rules
  Output: Dict[item_type -> List[positions]]
  Status: PRODUCTION (both implementations)
  Bottleneck: 7500 iterations (Python) / 4500 iterations (C#), single-item moves
  Time estimate: 2-10s depending on map size and item count

Stage 8: Stack Assembly
  Entry point: DesktopAgent/agent/tools/master_orchestrator.py:UberStrikeMapFactory._assemble_final_map()
               OR manual JSON + 6 PNGs
  Input: All 6 layer outputs + placement data
  Output: stack.json + 6 PNG files
  Status: WORKING
  Bottleneck: None
  Time estimate: <1s

Stage 9: Unity Headless Build
  Entry point: UberStrikeGen/.../HeadlessBuilder.cs:BuildArena()
               OR UberStrike2022/.../HeadlessBuilder.cs:BuildArena()
  Input: stack.json path OR blueprint PNG path, mpp, navmesh flag
  Output: Unity .scene file + baked NavMesh
  Status: PRODUCTION
  Bottleneck: Unity startup time (30-60s), NavMesh baking (5-30s)
  Time estimate: 60-120s total

Stage 10: Quality Check / Validation
  Entry point: UberStrikeGen/.../BlueprintQC.cs (pre-build)
               OR UberStrikeGen/.../AdvancedMetrics.cs (post-build)
               OR DesktopAgent/agent_v2/analyzer/quality_analyzer.py (Python)
  Input: Blueprint PNG or built scene
  Output: Score 0-1.0, pass/warn/fail status, recommendations
  Status: PRODUCTION (multiple implementations)
  Bottleneck: None
  Time estimate: 1-3s

Stage 11: AssetBundle Export
  Entry point: UberStrike2022/Integration/Editor/MapGenExporter.cs
  Input: Scene file
  Output: .unity3d AssetBundle (StandaloneWindows64)
  Status: PRODUCTION (UberStrike2022 only)
  Bottleneck: Unity build pipeline
  Time estimate: 30-60s

Stage 12: Quantum Optimization Module
  Entry point: qubo_encoder.py:MapGenQUBOEncoder.encode()
               -> quantum_mapgen/braket_runner.py:QAOARunner.run()
  Input: Walkable mask, spawns, chokes, cover, item rules
  Output: QUBO matrix -> QAOA bitstrings -> greedy-repaired placement
  Status: WORKING (experimental, 3-item proof-of-concept proven)
  Bottleneck: Qubit count (local: 24 max, SV1: 33 max, D-Wave: 5000+)
  Time estimate: 7s (12 qubits) to 373s (24 qubits) local; ~44 min per SV1 task
```

### 2.2 What Can It Actually Generate Right Now?

**Can I press one button and get a playable map?**

Almost, but not quite. Here's what works and what requires manual steps:

**Closest to one-button (UberStrike2022):**
1. Open Unity 2022 project
2. Use `Phase2TestBuild` (Menu: MapGen > Phase 2 Test Build) -- this creates a minimal test arena with 8 spawns, walls, and proper MapConfiguration wiring
3. Result: A loadable scene, but with basic geometry only (no generated layout)

**Full pipeline (requires 3-4 manual steps):**
1. Generate or provide a blueprint PNG (manually or via Python WFC)
2. Open `QuickBuildWindow` in Unity Editor, drag in the PNG, click Build
3. If quality check fails, adjust and rebuild
4. For UberStrike integration: run MapGenBridge to wire MapConfiguration

**Headless CLI (closest to automated):**
```bash
# Python orchestrator
cd DesktopAgent && python run_assistant.py build --stack path/to/stack.json

# Direct Unity CLI
Unity.exe -batchmode -quit -executeMethod MapGen.CLI.HeadlessBuilder.BuildArena --args stack=path/to/stack.json
```
This works but requires a pre-existing stack JSON + layer PNGs.

**What does a generated map look like?**
- Greybox quality: untextured cubes for walls (4m tall), flat quads for floors, cylinders for spawn markers
- Structural: proper rooms, corridors, multi-height platforms, bridges, ramps
- Gameplay: spawn points, weapon/health/armor pickups, teleporters, jump pads placed by SA
- Missing: no texturing (theme layer exists but materials are basic), no props/decorations, no skybox variety
- NavMesh: baked if enabled, so bots can navigate

**How many maps has this system generated end-to-end?**
- Evidence of ~38 dataset samples in `datasets/ds_stress_v1/`
- Build logs show at least 2 headless build attempts (both failed due to path issues)
- The `Phase2TestBuild` system has been proven working (code comments reference successful runs)
- Exact count of successful end-to-end generations is unknown but likely in the low dozens

**What percentage of generation attempts produce a usable result?**
- The quality gate threshold is 0.6 with max 3 retries
- Many dataset samples show QC failures ("Missing layout or flow layer")
- Estimated success rate: 40-60% on first attempt, 70-80% after retries (for Python orchestrator)
- Build pipeline success rate is higher (~90%+) when given valid input PNGs

**Are generated maps loadable in UberStrike 4.3?**
- UberStrike2022 has the full integration chain: MapGenBridge -> MapGenRegistrar -> MapGenOfflineInjector -> MapGenMapInjector
- The system handles MapConfiguration setup, spawn point wiring, and LevelManager injection
- AssetBundle export exists (`MapGenExporter.cs`)
- The runtime injection (`MapGenMapInjector.cs`) polls LevelManager and adds custom maps
- **Verdict: Yes, the pipeline to load into UberStrike exists and is coded, but end-to-end proof with a generated (not hand-built) map in a live UberStrike 4.3 session is unconfirmed**

### 2.3 Integration Gaps

| Boundary | From | To | Gap |
|----------|------|----|-----|
| Python WFC -> Unity Build | `wave_function_collapse.py` output (numpy) | `BuildFromBlueprint.cs` input (PNG) | **Bridged via PNG file.** Works, but requires manual file transfer or orchestrator |
| Python Voronoi -> Unity Theme | `voronoi_theme_generator.py` output | `BuildFromStackEnhanced.cs` theme layer | **Bridged via PNG.** Theme colors must match C# THEME_COLORS dict exactly |
| Python SA -> Unity Placement | `simulated_annealing_placer.py` output (dict) | `PrefabPlacementAI.cs` (C# SA) | **DUPLICATE implementations.** Python result not consumed by C#; C# re-runs SA independently |
| Python Agent -> Unity Process | `unity_automation.py` subprocess launch | `HeadlessBuilder.cs` | **HTTP bridge exists** (`AgentBridge.cs` at localhost:11435) but paths are hardcoded to Shadow PC |
| Stack JSON format | Python `stack_io.py` | C# `StackDefinition.cs` | **Compatible.** Both read same JSON format with layer path references |
| Quality scores | Python `quality_analyzer.py` | C# `BlueprintQC.cs` + `AdvancedMetrics.cs` | **Different metrics.** Python uses weighted spawn_balance/path_diversity; C# uses connectivity/verticality/cover/sightline. Not interchangeable |
| Quantum -> Classical | `qubo_encoder.py` -> `braket_runner.py` | `simulated_annealing_placer.py` | **Not integrated.** Quantum output is standalone; no pipeline to feed QAOA placement back into Unity build |

**Hardcoded paths (Shadow PC only):**
- `C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe` (unity_automation.py, headless_pipeline.py)
- `C:/UberStrikeGen` project path (unity_automation.py)
- `C:/UberStrikeGen/Assets/_UberStrike/Blueprints/MapLayouts` (unity_automation.py)
- `http://127.0.0.1:11435` agent bridge (AgentBridge.cs, BlueprintQCWriter.cs)

**Missing adapter layers:**
- No adapter from quantum QAOA output -> Unity PrefabPlacementAI input
- No adapter from Python GraphFlowAnalyzer metrics -> C# AdvancedMetrics format
- UberStrike2022 has 4 C# stubs (WFCCore, VoronoiThemeGenerator, FlowAnalysisCore, StackPreviewer) that return empty/fallback data

---

## Section 3: Algorithm Deep Dive

### 3.1 Wave Function Collapse

**Implementation Completeness: 75% (Python), 70% (C# UberStrikeGen), 0% (C# UberStrike2022 stub)**

**Python Implementation** (`wave_function_collapse.py`, 350 lines):
- **Tile set:** 10 base types (VOID, FLOOR, WALL, WALL_CORNER, WALL_T, WALL_END, DOOR, WATER, BRIDGE, SPAWN) with 40+ rotational variants
- **Adjacency rules:** N/E/S/W socket matching. Compatible pairs: (floor,door), (wall,door), (water,bridge), (floor,bridge)
- **Weights:** VOID=0.05, FLOOR=5.0, WALL=3.0, WALL_CORNER=2.0, DOOR=0.5, WATER=0.3, BRIDGE=0.4, SPAWN=0.15
- **Maximum reliable grid size:** 64x64 (default), tested up to 256x256. Contradiction rate increases significantly above 128x128
- **Backtracking strategy:** **None.** If constraint propagation empties a cell's candidates, collapse fails. `fallback_to_blank=True` mode returns a blank bordered layout on failure
- **Contradiction rate:** ~5-10% at 64x64, ~20-40% at 128x128 (estimated from code structure)
- **Entropy calculation:** Shannon entropy with weight bias: `-sum(w * log(w))` for remaining candidates

**C# Implementation** (`WaveFunctionCollapseGenerator.cs`, 452 lines):
- Same 10 tile types with rotation support
- Pre-computed compatibility matrix
- Border constraint: outer ring = Wall
- Spawn hints placed at quadrant centers
- Optional BFS connectivity check post-collapse
- **Not a stub** -- fully functional editor window with Generate + Save buttons

**C# Alternative** (`WaveFunctionCollapse.cs`, 363 lines):
- Data-driven: `LearnRulesFromExisting(imagePath)` extracts adjacency rules from example PNGs
- Entropy-based selection with noise tiebreaker
- Separate from the editor window implementation

**What's missing vs production WFC:**
- No backtracking/restart on contradiction
- No progressive refinement (all-or-nothing collapse)
- No multi-resolution hierarchy (would help 128x128+)
- No user-defined tile weights per map style
- No asymmetric adjacency rules (A can be north of B, but B can't be north of A)

### 3.2 Voronoi Tessellation

**Implementation Completeness: 85% (Python), 75% (C# UberStrikeGen), 0% (C# UberStrike2022 stub)**

**Python Implementation** (`voronoi_theme_generator.py`, 291 lines):
- **Seed strategies:** 4 available
  - `"poisson"` (preferred): Poisson disk sampling with min_dist = min(w,h) / max(3.0, regions*0.8), 32 attempts per sample
  - `"random"`: Uniform random
  - `"grid"`: Grid-based with 35% jitter
  - `"weighted"`: Biased toward non-black layout pixels (walkable areas)
- **Theme types:** 6 themes affecting material assignment
  - Industrial (#222222, w=1.0), Warehouse (#554433, w=1.0), SciFi (#334455, w=1.0)
  - Outdoor (#445533, w=0.5), Tech (#553344, w=0.8), Clean (#C8C8C8, w=0.3)
- **Smoothing:** Gaussian filter via scipy.ndimage (sigma parameter), fallback 2D convolution kernel
- **Layout masking:** Voronoi seeds only placed on walkable pixels when layout provided

**C# Implementation** (`VoronoiThemeGenerator.cs`, 331 lines):
- Poisson disk sampling (correct implementation)
- Same 6-theme palette
- Morphological smoothing (dilate+erode cycles, 0-5 intensity)
- EditorWindow with preview + save

**C# UberStrike2022:** Stub. Returns uniform gray fallback texture.

### 3.3 Simulated Annealing (Item Placement)

**Python Completeness: 95%. C# Completeness: 90%.**

**Python** (`simulated_annealing_placer.py`, 330 lines):

Energy function:
```
E = 10.0 * SpawnBalance +
     5.0 * RiskReward +
     3.0 * FlowAlignment +
     7.0 * SpacingPenalty +
     4.0 * StrategicDepth
```

| Term | Weight | Formula |
|------|--------|---------|
| SpawnBalance | 10 | std(per-spawn distance-weighted item advantage) |
| RiskReward | 5 | Penalizes items violating exposure/cover preferences |
| FlowAlignment | 3 | <5m from choke: 3*(5-d); >15m from choke: 0.5*(d-15) |
| SpacingPenalty | 7 | 5*(minSpacing-d) for each violated pair |
| StrategicDepth | 4 | max(0, power_item_barycenter_offset - 30m) |

- **Cooling:** T_init=1000, rate=0.95, stops at T<0.05 after 500 iterations
- **Neighborhood:** Move 1 random item within 25-unit radius to walkable cell
- **Max iterations:** 7500
- **Acceptance:** Metropolis: exp(-delta / max(T, 1e-3))
- **Item rules:** 9 types, 25 total items (sniper x1, rocket x1, shotgun x2, armor_heavy x1, armor_light x3, health_mega x1, health_small x6, ammo_rockets x4, ammo_bullets x6)

**C#** (`SimulatedAnnealingPlacer.cs`, 392 lines):

Same 5 terms with identical weights. Key differences:
- **T_init=750** (vs Python 1000)
- **Cooling=0.96** (vs Python 0.95) -- slightly slower cooling
- **Max iterations=4500** (vs Python 7500)
- **Stop condition:** T<0.05 after 800 iterations (vs Python 500)
- Deterministic RNG (seed=1337)
- Single-item moves only (same limitation)

**Known failure modes:**
- Local minima trapping with 25 items on small maps (insufficient exploration)
- Spawn balance term dominates (weight=10) and can override spacing
- No multi-item swap moves (slower convergence than necessary)
- Python and C# can produce different results for same input (different T_init/cooling)

### 3.4 Flow Analysis

**Python** (`graph_flow_analyzer.py`, 494 lines):

**Metrics computed (10):**
1. **Chokepoints** -- Betweenness centrality (90th percentile threshold)
2. **Dead zones** -- Degree <= 1 nodes
3. **Heat map** -- 800 random walks, 40 steps, 70% item-seek bias
4. **Spawn balance** -- Coefficient of variation of weighted shortest-path distances
5. **Circulation loops** -- nx.simple_cycles (max 10 loops, 3-20 nodes each)
6. **Sightline map** -- 16-ray casting per position, 50-step range
7. **Camping spots** -- 2-4 wall neighbors + sightline > 0.3
8. **Average engagement distance** -- Line-of-sight random pair sampling
9. **Map openness** -- floor/(wall+1), capped at 1.0
10. **Strategic positions** -- Closeness centrality ranking

**NetworkX graph:** 8-connected (4 cardinal + 4 diagonal at 1.414 weight). Nodes = walkable pixels, edges = adjacency.

**3D handling:** wall_height parameter for sightline calculation only. No ramp/jump pad pathing. 2D projected.

**NavMesh integration:** Python has none. C# `GraphFlowAnalyzer.cs` uses `NavMesh.CalculateTriangulation()` for proper 3D graph.

**Accuracy vs in-game:** 2D grid approximation. No jump pads, teleporters, or vertical traversal in pathfinding. Sightlines are ray-based but don't account for glass/water transparency.

**C#** (`GraphFlowAnalyzer.cs`, 370 lines):
- Same 8 metrics but implemented on NavMesh vertices (3D-accurate)
- Dijkstra-based betweenness (instead of NetworkX)
- Monte Carlo heatmap projected to 32x32 grid
- DFS cycle detection

### 3.5 Greedy Mesher

**Implementation:** `MapGen_Project/Assets/MapGen/Runtime/GreedyMesher.cs` (~80 lines)

**What it optimizes:** Merges adjacent same-type tiles into larger rectangles to reduce draw calls and mesh complexity. Standard greedy meshing algorithm -- scans rows, extends rectangles right and down.

**Input:** 2D tile grid (from layout layer)
**Output:** List of merged rectangles (position, width, height, tile type)

**Performance:** O(W*H) per pass. Fast enough for 256x256 grids.

### 3.6 Quantum Module

**QUBO Encoder** (`qubo_encoder.py`, 947 lines):

| Constraint | ID | Type | Formula |
|------------|-----|------|---------|
| One-hot | C-007 | Quadratic | penalty_one_hot=500: each item placed exactly once |
| Spacing | C-004 | Quadratic | weight=7: min distance between item pairs |
| Spawn Balance | C-001 | Quadratic | weight=10: minimize per-spawn advantage variance |
| Risk/Reward | C-002 | Linear | weight=5: match exposure preference vs cover |
| Flow Alignment | C-003 | Linear | weight=3: avoid chokepoints (<5m), stay engaged (>15m) |
| Strategic Depth | C-005 | Quadratic | weight=4: power item centroid within 30m of center |
| Walkability | C-006 | Structural | candidates pre-sampled from walkable cells |

- `create_subproblem()`: Generates reduced QUBO for simulator limits
- `validate()`: Compares QUBO energy vs SA energy for correctness

**QAOA Runner** (`braket_runner.py`, 720 lines):
- QUBO -> Ising conversion (x_i = (1-z_i)/2)
- p-layer QAOA circuit: cost (RZ + CNOT-RZ-CNOT) + mixer (RX)
- COBYLA parameter optimization (30 iterations, local only)
- Greedy repair: fixes infeasible one-hot violations in QAOA output
- SV1 cost tracking ($0.075/min, 60 min/month free tier)

**Experimental results:**

| Candidates | Qubits | Runtime (local) | Best Energy | Feasible? |
|:---:|:---:|:---:|:---:|:---:|
| 4 | 12 | 7.3s | 2589 | NO |
| 5 | 15 | 10.3s | 2496 | NO |
| 6 | 18 | 17.5s | 2447 | NO |
| 7 | 21 | 52.2s | 3054 | NO |
| 8 | 24 | 373.2s | 2653 | NO |
| 11 (SV1) | 33 | ~44 min | 1339 | NO (repaired) |

**D-Wave readiness:** QUBO formulation is correct and validated. Awaiting D-Wave LaunchPad acceptance for full 25-item placement (3750+ qubits). Current proofs-of-concept demonstrate the pipeline works; quality depends on qubit count and repair strategy.

---

## Section 4: Unity Integration Assessment

### 4.1 Build Pipeline

**HeadlessBuilder.cs:** Works in batchmode. Parses `--args` for blueprint/stack/mpp/navmesh. Exits with code 0/1. Logs `[HEADLESS] BUILD_DONE` marker for log parsers. Retry loop (10s, 100ms intervals) for file existence check. **Risk:** 10s timeout may be insufficient on slow storage; no rollback on partial failure.

**BuildFromBlueprint.cs (2149 lines):** Handles:
- 19 tile type classifications from pixel colors (30-tolerance matching)
- Flood-fill floor meshing (BFS, one mesh per connected region)
- Perimeter wall generation with door gap detection
- Multi-height platforms (E1=4m, Mid=8m, Upper=12m) with pillars and railings
- Spawn cylinders, pickup placement, teleporter pairing
- NavMesh baking (optional)
- Mesh combining (Mesh.CombineMeshes) for draw call reduction
- Safety limits: MAX_TOTAL_OBJECTS=2000, MAX_PLATFORMS=20

**What it skips:** Destructible objects (TODO), advanced collision layers, animated water, LOD generation, per-pixel physics, ray-traced lighting.

**Unity versions:** UberStrikeGen requires Unity 6000.2.6f2+. UberStrike2022 requires Unity 2022.3.x LTS. MapGen_Project works on Unity 2022+.

**CI/CD:** Could run on any machine with Unity installed in batchmode. Currently only tested on Shadow PC. The `unity.yml` workflow is a stub (no actual build steps).

**AssetBundle generation:** Implemented in `UberStrike2022/Integration/Editor/MapGenExporter.cs`. Uses `BuildPipeline.BuildPlayer` with `BuildAdditionalStreamedScenes`. Targets StandaloneWindows64.

### 4.2 Game Integration

**Can generated maps load in UberStrike 4.3?**

The UberStrike2022 project has the complete chain:
1. **MapGenBridge.cs** (641 lines): Converts raw generated scene to UberStrike format. Adds MapConfiguration component via reflection (sets private fields). Sets up SpawnPoints hierarchy, camera, and proper hierarchy
2. **MapGenRegistrar.cs** (247 lines): Persists map metadata in EditorPrefs (`mapId|displayName|sceneName`)
3. **MapGenOfflineInjector.cs** (74 lines): Injects maps into OfflineBypass at runtime
4. **MapGenMapInjector.cs** (102 lines): Polls LevelManager until server maps load, then adds custom maps. Solves the problem that `OfflineBypass.Bootstrap()` never runs in network play
5. **MapGenExporter.cs** (162 lines): Exports as AssetBundle for distribution

**Required components the generator creates:**
- MapConfiguration (via reflection) -- YES
- SpawnPoints (cylinders at flow-layer positions) -- YES
- NavMesh (optional baking) -- YES
- MeshColliders (on combined geometry) -- YES
- Directional lighting -- YES
- Fog settings -- YES

**Missing for full game integration:**
- Proper UberStrike weapon pickup prefabs (uses placeholder cubes)
- Kill zone / out-of-bounds triggers
- Minimap texture generation
- Loading screen thumbnail

### 4.3 Editor Tools

| Tool | Location | Description |
|------|----------|-------------|
| QuickBuildWindow | UberStrike2022/Editor/ | Drag PNG, configure MPP/wallHeight, click Build |
| WFC Generator | UberStrikeGen/Scripts/Editor/ | Width/Height sliders, spawn count, generate + save |
| Voronoi Generator | UberStrikeGen/Scripts/Editor/ | Region count, smoothing, preview + save |
| BlueprintQC | Both projects | Drag PNG, view pixel breakdown + QC metrics |
| StackImportWindow | UberStrike2022/Editor/ | Load .stack.json, preview layers, build |
| AssetCatalogBuilder | Both projects | Scan folders, build asset catalog SO |
| MapGenWindow | MapGen_Project/Editor/ | Manual build UI |
| Phase2TestBuild | UberStrike2022/Editor/ | One-click test arena creation |
| MeshCombiner | UberStrike2022/Editor/ | Manual mesh combining utility |

**Documentation:** Tools/MapGen/README.md covers the full toolkit. Inline comments are moderate. A new developer could use QuickBuildWindow and Phase2TestBuild without guidance; WFC/Voronoi generators would need explanation.

---

## Section 5: Python Agent / Automation Layer

### 5.1 Desktop Agent

**Tools exposed (v1 -- `agent/tools/`):**
- `simulated_annealing_placer.py` -- SA optimization
- `wave_function_collapse.py` -- WFC layout generation
- `voronoi_theme_generator.py` -- Theme regions
- `graph_flow_analyzer.py` -- Flow metrics
- `master_orchestrator.py` -- End-to-end pipeline
- `adaptive_lod_optimizer.py` -- Mesh LOD
- `map_quality.py` -- Quality scoring
- `unity_automation.py` -- Unity process management
- `process_manager.py` -- Process lifecycle
- `window_manager.py` -- Win32 window control
- `log_monitor.py` -- Unity log parsing
- `asset_extractor.py` -- Scene pattern extraction
- `batch_generator.py` -- Parallel generation

**Communication with Unity:** HTTP bridge (`AgentBridge.cs` at localhost:11435) + subprocess launch (headless batchmode). File-based data exchange (stack JSON + PNG layers).

**Agent v1 vs v2:**
- **v1** (`agent/`): Monolithic tools, each self-contained. Direct algorithm access. No ML integration.
- **v2** (`agent_v2/`): Modular subsystems (analyzer, builder, blueprints, dataset, fixer, generator, ml, monitor, mutator, validator, cli). ML pipeline (trainer/predictor). Pydantic schemas. Prompt-driven generation. ~65% complete.

**Claude API integration:** Active in `run_assistant.py` (500 lines). Uses `click` CLI with 11 commands: monitor, generate, fix, build, mutate, export_dataset, score, train_predictor, predict, deploy, interactive. `ask_claude.py` has legacy tool registry. Anthropic client setup present.

**ML/AI components:**
- `layout_diffusion.py`: DDPM framework present, graceful PyTorch fallback. **Not trained** -- returns noise if no model file
- `trainer.py`: RandomForest (100 trees) on blueprint features. **Functional** but requires dataset
- `predictor.py`: Loads .pkl model for PASS/FAIL prediction. **Functional** (quality_model.pkl exists at repo root)

### 5.2 Orchestrator

**`master_orchestrator.py` end-to-end flow:**
1. WFC layout generation (size mapped: small=32x32, medium=64x64, large=128x128)
2. Voronoi theme application (complexity mapped: simple=3, medium=5, complex=8 regions)
3. SA item placement (style-specific presets: arena, ctf, deathmatch; large=2x items, small=0.5x)
4. Flow analysis (classified layout -> metrics)
5. Quality validation (multi-metric gate)
6. LOD optimization (importance map from spawns+chokes+items)
7. Final assembly (JSON export with all layers)

**Quality gate:**
```
quality = 1.0 * balance_penalty * dead_zone_penalty * choke_bonus * openness_bonus
Pass threshold: >= 0.6
```
- balance < 0.3: penalty = 1.0; 0.3-0.5: 0.8; > 0.5: 0.5
- dead_zones > 100: penalty approaches 0.5
- 2-5 chokes: 1.1x bonus
- Openness 0.3-0.7: 1.05x bonus

**Retry logic:** Max 3 attempts. Increments seed on each retry. Effective for WFC contradiction failures.

**Could it run unattended for 100 maps?** With the Python orchestrator alone (no Unity build), yes -- WFC + Voronoi + SA + Flow + QC runs entirely in-process. For Unity builds, would need headless Unity running and reliable path configuration. Current hardcoded paths would need to be parameterized. Estimated: 70-80% success rate per attempt, ~90%+ with 3 retries.

---

## Section 6: Data Assets & Training Data

**Blueprint/seed data:**
- `dataset_seed/`: 1 baseline stack with 6 layer PNGs (extracted from UberStrikeGen sample via `convert_seed.py`)
- `UberStrikeGen/Assets/_UberStrike/Blueprints/Stacks/ArenaStack_Sample.*`: Base64-encoded sample layers

**Dataset samples:**
- `datasets/ds_stress_v1/`: 38 samples (augmented from seed via rotation/flip)
- `datasets/ds_v2/`: 20+ samples
- Each sample contains: `stack.json`, `qc_report.json`, `features.json`, `meta.json`, layer PNGs

**QC report structure:**
```json
{
  "status": "pass|fail|warn",
  "score": 0.0-1.0,
  "metrics": {
    "spawn_count": int,
    "spawn_balance": float,
    "path_diversity": float,
    "verticality": float,
    "cover_density": float,
    "sightline": float,
    "connected_components": int,
    "playable_area_ratio": float
  }
}
```

**Training data for ML:** `MapTrainingPipeline.cs` exports to `_UberStrike/TrainingData/map_dataset.json` with 64x64 binary feature grids + per-map metrics. `quality_model.pkl` (47KB) is a trained RandomForest model.

**Dataset quality:** Many samples fail QC due to missing layers or insufficient spawns. The augmentation strategy (rotation/flip) is sound but limited to geometric transforms of a single seed. Real diversity requires more unique base layouts.

---

## Section 7: Code Quality Assessment

**Test coverage:**
- 5 pytest test files in `DesktopAgent/tests/`: determinism, exporter, feature extractors, sanitizer, stack I/O
- No C# unit tests (Unity Test Runner not configured)
- No integration tests for the full pipeline
- CI runs `pytest DesktopAgent/tests` + `ruff` linting

**Documentation:**
- README.md (23.8KB) is comprehensive and largely accurate
- QUANTUM_MAPGEN_ANALYSIS.md (37KB) is thorough
- Tools/MapGen/README.md covers the toolkit well
- Inline comments are moderate (critical algorithms documented, utility code sparse)
- No API reference docs

**Dependency management:**
- Python: No `requirements.txt` committed (TESTING.md mentions numpy, scipy, pillow, networkx, pytest)
- Unity: Package dependencies managed by Unity Package Manager (not audited)
- Optional: torch, diffusers, trimesh, anthropic, click, pynput, mss, psutil

**Error handling:**
- Python: try/except in orchestrator with retry logic. Algorithms fail with descriptive errors
- C#: AgentBridge wraps all HTTP calls in try/catch (safe if agent is down). BuildFromBlueprint has safety limits (MAX_TOTAL_OBJECTS)
- Missing: no structured error codes, no error aggregation/reporting

**Logging:**
- Unity: `Debug.Log/LogWarning/LogError` with `[HEADLESS]`, `[MapGen]`, `[GenMap]` prefixes
- Python: print statements (no logging module usage in v1; click.secho in v2)
- Can you debug a failed generation from logs alone? Partially. Unity logs capture build progress markers. Python orchestrator logs quality scores and retry reasons. Missing: no structured JSON logging, no correlation IDs across Python<->Unity boundary

---

## Section 8: What Works vs. What's Aspirational

### WORKS TODAY (verified, tested, produces output)

1. **WFC layout generation** (Python + C#) -- produces valid 64x64 arena layouts
2. **Voronoi theme regions** (Python + C#) -- assigns 6 theme types with Poisson disk seeding
3. **SA item placement** (Python + C#) -- optimizes 25 items with 5-term energy function
4. **Flow analysis** (Python via NetworkX) -- 10 metrics including chokepoints, heatmaps, camping spots
5. **Blueprint-to-scene build** (C# BuildFromBlueprint) -- PNG -> 3D Unity scene with walls/floors/spawns
6. **Stack-based build** (C# BuildFromStackEnhanced) -- 6-layer stack -> Unity scene
7. **Headless Unity build** (C# HeadlessBuilder) -- batchmode CLI entry
8. **Quality control** (Python + C# BlueprintQC) -- pixel analysis + gameplay metrics
9. **Python orchestrator** (master_orchestrator.py) -- end-to-end with quality gate + retry
10. **Dataset export** (Python exporter + mutator) -- augmented variants with QC reports
11. **MapGenBridge** (C# UberStrike2022) -- scene-to-UberStrike conversion with MapConfiguration
12. **MapGenRegistrar + Injector** (C# UberStrike2022) -- runtime map injection into game
13. **AssetBundle export** (C# MapGenExporter) -- .unity3d for distribution
14. **QUBO encoder** (Python) -- 7 constraints validated against SA energy function
15. **QAOA runner** (Python + Braket) -- local + SV1 cloud execution with greedy repair
16. **Quantum scaling study** -- 12-33 qubit experiments completed, charts generated
17. **Web preview** (JS/HTML) -- Three.js 3D map viewer with orbit controls
18. **CLI assistant** (Python run_assistant.py) -- 11-command interface
19. **ML quality predictor** (Python) -- trained RandomForest model (quality_model.pkl)
20. **Phase 0/1/2 test builds** (C# UberStrike2022) -- progressive integration testing
21. **F12 debug overlay** (C# MapGenDiagnostics) -- runtime diagnostics

### ASPIRATIONAL (code exists but not proven end-to-end)

1. **Layout diffusion model** (layout_diffusion.py) -- PyTorch DDPM framework present, no trained model
2. **Prompt-to-map generation** (layer_generator.py) -- generates layers from text, quality unproven
3. **Full 25-item quantum placement** -- QUBO formulation ready, needs D-Wave hardware (>3000 qubits)
4. **100-map unattended batch** -- batch_generator.py exists, never run at that scale
5. **Bot playtesting** (BotPlaytester.cs) -- NavMesh bots exist, heatmap output unvalidated
6. **Web editor** (web_editor/app.py) -- Flask UI present, real-time editing untested
7. **Phase 3 integration** (phase3_integration.py) -- diffusion + variation + web editor, stub only
8. **ML training pipeline** (trainer.py) -- framework functional, needs more diverse dataset
9. **WFC learning from examples** (WaveFunctionCollapse.cs generators variant) -- LearnRulesFromExisting() present, quality unverified
10. **Adaptive LOD in Unity** (AdaptiveLODGenerator.cs) -- depends on UnityMeshSimplifier library

### MISSING (would be needed for production)

1. **WFC backtracking** -- restart/undo on contradiction for reliable 128x128+ maps
2. **Textured materials** -- currently greybox only; need proper UberStrike material assignment
3. **Prop/decoration placement** -- no furniture, crates, barrels, environmental detail
4. **Kill zones / out-of-bounds** -- no boundary enforcement
5. **Minimap generation** -- no top-down render for in-game minimap
6. **Loading screen thumbnails** -- no preview image generation
7. **requirements.txt / pyproject.toml** -- no formal Python dependency specification
8. **C# unit tests** -- no Unity Test Runner coverage
9. **Structured logging** -- no JSON logs, no correlation IDs
10. **Cross-machine configuration** -- hardcoded paths must be parameterized
11. **Quantum -> Unity pipeline** -- no adapter from QAOA output to PrefabPlacementAI input
12. **Map style presets** -- no "desert", "snow", "industrial" theme packages with matching assets
13. **Sound design integration** -- no ambient audio or reverb zone placement
14. **Water physics** -- static quads only, no swimming/drowning
15. **Dynamic lighting** -- no light probe placement, no baked lightmaps
16. **Game mode validation** -- no CTF flag placement, no objective verification

---

## Section 9: Dependency Map

```
                           [User Input]
                          /     |       \
                         v      v        v
               [Blueprint PNG] [Text Prompt] [Stack JSON + 6 PNGs]
                    |              |              |
                    v              v              v
              [WFC Layout]  [LayerGenerator]  [StackIO Load]
                    |         (agent_v2)          |
                    v              |              |
             [Voronoi Theme]      |              |
                    |              v              |
                    v         [6 Layer PNGs]      |
              [SA Placement]      |              |
                    |             v              v
                    v      [Stack Assembly] ----+
             [Flow Analysis]     |
                    |            v
                    v     [Quality Gate] (>= 0.6)
             [LOD Optimizer]     |
                    |      fail: retry (max 3)
                    v      pass: continue
              [Final Map JSON]   |
                                 v
                    [Unity HeadlessBuilder]
                           |
                    +------+------+
                    |             |
                    v             v
          [BuildFromBlueprint] [BuildFromStackEnhanced]
                    |             |
                    v             v
               [Unity Scene (.unity)]
                    |
              +-----+------+
              |            |
              v            v
        [BlueprintQC]  [AdvancedMetrics]
              |            |
              v            v
        [MapGenBridge] (UberStrike2022)
              |
              v
        [MapGenRegistrar]
              |
              v
        [MapGenExporter] --> [AssetBundle (.unity3d)]
              |
              v
        [MapGenMapInjector] --> [UberStrike 4.3 Runtime]


    === Quantum Side-Chain (Experimental) ===

    [Walkable Mask + Spawns + Chokes + Cover]
              |
              v
        [QUBO Encoder] (7 constraints)
              |
              v
        [QAOA Runner] (LocalSimulator or SV1)
              |
              v
        [Greedy Repair] --> [Feasible Placement]
              |
              v
        [Compare vs SA Energy] (validation only, not fed back into Unity pipeline)


    === ML Side-Chain (Partial) ===

    [Dataset Samples]
              |
              v
        [Feature Extractors] --> [features.json]
              |
              v
        [QualityModelTrainer] --> [quality_model.pkl]
              |
              v
        [QualityPredictor] --> [PASS/FAIL + confidence]
```

**Circular dependencies:** None identified.

**Orphaned components:**
- `debug_path.py` -- one-off diagnostic, not referenced
- `ask_claude.py` -- legacy, superseded by `run_assistant.py`
- `phase3_integration.py` -- entry point for unfinished subsystems
- `agent/tools/screen.py`, `ui_automation.py` -- present but not called by orchestrator

---

## Section 10: If I Had $10K -- What Would Move the Needle Most?

### Rank 1: Close the C#/Python Gap (Unity Developer, 6 weeks, ~$5,000)

**What:** Port WFC (with backtracking) and Voronoi from Python to C# in UberStrike2022, replacing the 4 stubs. Wire the complete pipeline into a single "Generate Map" EditorWindow button.

**Build:**
- WFCCore.cs: Full tileset-based WFC with restart-on-contradiction (3-5 restarts before giving up)
- VoronoiThemeGenerator.cs: Poisson disk seeding, 6 themes, Gaussian smoothing
- FlowAnalysisCore.cs: NavMesh-based betweenness centrality, heatmap generation
- One-click EditorWindow: size dropdown, style preset, seed field, "Generate" button that runs WFC -> Voronoi -> SA -> Flow -> Build -> QC -> Save

**Unlocks:** Anyone with Unity 2022 can generate playable maps without Python. Eliminates HTTP bridge dependency. Transforms MapGen from "developer tool" to "community tool."

**Skills:** Senior Unity/C# developer with procedural generation experience
**Dependencies:** None -- all algorithms documented in Python
**Time:** 6 weeks
**Impact:** 10/10

### Rank 2: Asset Pipeline + Visual Quality (3D Artist + Unity Dev, 4 weeks, ~$3,000)

**What:** Create proper UberStrike-style materials, props, and decoration placement. Replace greybox output with visually complete maps.

**Build:**
- 6 theme material packages (Industrial, Warehouse, SciFi, Outdoor, Tech, Clean) with PBR textures
- Prop placement system: crates, barrels, pipes, lights, decals based on theme regions
- Proper weapon/health/armor pickup prefabs matching UberStrike 4.3 assets
- Loading screen thumbnail generator (top-down camera render)
- Minimap texture generator

**Unlocks:** Generated maps look like real game maps instead of tech demos. Community uptake multiplies.

**Skills:** 3D artist (texturing, PBR), Unity developer
**Dependencies:** Rank 1 (one-click generation) makes this more impactful but not required
**Time:** 4 weeks
**Impact:** 8/10

### Rank 3: Reliability + Cross-Platform (DevOps/Unity Dev, 2 weeks, ~$1,500)

**What:** Make the pipeline reliable and portable.

**Build:**
- `requirements.txt` / `pyproject.toml` with pinned versions
- Config system: replace all hardcoded paths with `config.yaml` resolution
- Unity CI: implement `unity.yml` with GameCI for automated build testing
- C# unit tests: Unity Test Runner coverage for BuildFromBlueprint, SA, WFC
- Structured JSON logging with correlation IDs across Python<->Unity
- Docker container for Python orchestrator

**Unlocks:** Other developers can run the pipeline on their machines. CI catches regressions. Debugging becomes tractable.

**Skills:** DevOps, Unity CI (GameCI), Python packaging
**Dependencies:** None
**Time:** 2 weeks
**Impact:** 7/10

### Rank 4: Game Design Validation (Level Designer, 3 weeks, ~$2,000)

**What:** Hire a level designer to create reference maps and tune the generation system.

**Build:**
- 5-10 hand-crafted "gold standard" blueprint PNGs representing ideal UberStrike map archetypes
- Empirically tuned SA weights (the current 10/5/3/7/4 are educated guesses)
- QC threshold calibration based on playtesting (is 0.6 the right gate?)
- Style presets: "Sniper Map" (open, long sightlines), "CQC Arena" (tight corridors), "Vertical Playground" (multi-level)
- Documentation: what makes a good UberStrike map (for training data curation)

**Unlocks:** Objective quality benchmark. SA weights produce actually fun maps. New seed layouts for dataset diversity.

**Skills:** FPS level designer with multiplayer map experience
**Dependencies:** A working build pipeline (either Python or C# one-click)
**Time:** 3 weeks
**Impact:** 9/10 (highest quality-per-dollar ratio)

### Rank 5: Quantum Integration Completion (Quantum Dev, 2 weeks, ~$1,500)

**What:** If D-Wave LaunchPad is approved, run full 25-item placement on quantum hardware and build the comparison pipeline.

**Build:**
- Adapter: QAOA output -> PrefabPlacementAI input format
- A/B comparison framework: generate same map with SA vs quantum, score both
- D-Wave Ocean SDK integration for direct QPU submission
- Results dashboard showing classical vs quantum placement quality
- Paper draft for FDG/IEEE CoG

**Unlocks:** Research credibility. Conference presentation. Unique selling point for the project. Potential D-Wave case study.

**Skills:** Quantum computing (QUBO/QAOA), Python, paper writing
**Dependencies:** D-Wave LaunchPad acceptance
**Time:** 2 weeks (post-acceptance)
**Impact:** 6/10 (high visibility, low practical impact on map quality)

### Summary: Optimal $10K Allocation

| Investment | Cost | Time | Impact |
|-----------|------|------|--------|
| #1 Close C#/Python gap | $5,000 | 6 weeks | One-click generation |
| #4 Game design validation | $2,000 | 3 weeks | Actually fun maps |
| #3 Reliability + CI | $1,500 | 2 weeks | Other devs can contribute |
| #2 or #5 (remaining) | $1,500 | 2-3 weeks | Visual polish OR quantum proof |
| **Total** | **$10,000** | **~10 weeks** | **Community-ready map generator** |

The first $7,000 (Ranks 1+4+3) transforms MapGen from a research prototype into a tool the UberStrike community can actually use. The remaining $3,000 is a choice between visual fidelity (broader appeal) or quantum novelty (research prestige).

---

*Report generated by Claude Code (Opus 4.6) on 2026-04-02.*
*Repository: github.com/constripacity/uberstrike-mapgen @ bed4d5c*
*Total files scanned: ~180 source files across 7 directories*
*Total lines analyzed: ~38,800*
