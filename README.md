# 🎯 UberStrike Map Generation System

![Unity 6000.2.6f2](https://img.shields.io/badge/Unity-6000.2.6f2-black?logo=unity)
![Python 3.11](https://img.shields.io/badge/Python-3.11-blue?logo=python)
[![Python CI](https://github.com/constripacity/uberstrike-mapgen/actions/workflows/python.yml/badge.svg)](https://github.com/constripacity/uberstrike-mapgen/actions/workflows/python.yml)
[![Unity CI](https://github.com/constripacity/uberstrike-mapgen/actions/workflows/unity.yml/badge.svg)](https://github.com/constripacity/uberstrike-mapgen/actions/workflows/unity.yml)
![Status v0.1-dev](https://img.shields.io/badge/Status-v0.1--dev-orange)

An open-source Unity tool designed to automate the creation of **3D FPS arena maps from 2D blueprints**, built for the legendary **UberStrike**, the fast-paced multiplayer shooter originally developed by **CMUNE**.

This project aims to accelerate the **revival and expansion** of UberStrike's content ecosystem, starting with procedural map generation, and later extending to **weapon models, skins, and textures.**

---

## 🕹️ About UberStrike

**UberStrike** was a popular free-to-play multiplayer FPS originally released on Facebook and Steam.  
In **2016**, official support and servers were discontinued.

However, in the years following **2022**, a dedicated group of community developers with permission from the original UberStrike Dev Team began **rebuilding the game**, restoring its servers and bringing it back to life for fans around the world.

- 🎮 **UberStrike Steam Info:** [https://steamdb.info/app/291210/info/](https://steamdb.info/app/291210/info/)  
  *(requires patched servers, join the Discord server for details)*  
- 💬 **UberStrike Discord:** [https://discord.gg/hhxZCBamRT](https://discord.gg/hhxZCBamRT)

This repository contributes to that community revival effort by providing a **modern toolchain** for generating game assets faster and more efficiently.

---

## ⚛️ Quantum-Assisted Map Optimization

This repository includes an experimental quantum computing module that explores using **quantum annealing** to optimize item placement in procedurally generated maps.

### The Problem

Placing 25 gameplay items (weapons, health, armor, ammo) on a generated map requires satisfying multiple competing constraints simultaneously — spawn balance, minimum spacing, risk/reward exposure, flow alignment, and strategic depth. Our classical Simulated Annealing optimizer gets trapped in local minima where these constraints conflict.

### The Approach

We formulated the item placement problem as a **QUBO** (Quadratic Unconstrained Binary Optimization) — the native input format for quantum annealers — and tested it using QAOA circuits on Amazon Braket simulators.

### Results

| Scale | Qubits | Runtime | Best Energy |
|:------|:------:|:-------:|:-----------:|
| Local (4-8 candidates) | 12-24 | 7s - 6 min | 2,447 |
| Cloud SV1 (11 candidates) | 33 | ~44 min | 1,339 |
| D-Wave target (50+ candidates) | 150-750+ | Seconds (projected) | TBD |

The exponential runtime wall of classical simulation (each +3 qubits = 3-7x slower) motivates testing on D-Wave's 5,000+ qubit quantum annealer, where our 1,250-3,750 variable QUBO fits naturally.

### Quantum Module Structure
```
qubo_encoder.py                    # Constraint → QUBO matrix conversion
quantum_mapgen/
├── braket_runner.py               # QAOA circuit builder (local + SV1 cloud)
├── exp1_basic_connectivity.py     # Experiment 1: 3 power items proof-of-concept
├── exp1_scaling_quick.py          # Scaling study across qubit counts
├── visualize_results.py           # Result charts
```

### Setup
```bash
pip install amazon-braket-sdk amazon-braket-default-simulator
export BRAKET_S3_BUCKET=your-braket-bucket-name
python -m quantum_mapgen.exp1_basic_connectivity          # local simulator
python -m quantum_mapgen.exp1_basic_connectivity --cloud   # SV1 (uses free tier)
```

---

## ⚡ Quick Start

```bash
git clone https://github.com/constripacity/uberstrike-mapgen.git && cd uberstrike-mapgen
# Open the UberStrikeGen project with Unity 6000.2.6f2
# In Unity: Tools → UnityAI → Quick Build Settings (adjust MPP/Wall/Max)
# Then run Tools → UnityAI → Quick Build ► From Latest PNG & Save Scene
# Find the exported scene under Assets/_UberStrike/Maps/Playable
```

## 🧭 MapGen Legend Pipeline (Unity 6)

1. In the Unity editor, open **Tools → UberStrike → MapGen → Open Prefab Catalog** and assign prefabs/materials for spawns, pads, teleporters, health, armor, water, and glass.
2. Launch **Tools → UberStrike → MapGen → Generator Window** to author legend PNGs, then use **Build Scene from Selected Legend** to instantiate greybox geometry and gameplay markers under `Assets/_Generated/Maps/`.
3. Use **Tools → UberStrike → MapGen → Export Dataset (Current Scene)** or **Export All Maps** to capture `legend.png`, `height.png`, and `map.json` bundles for downstream tooling.
4. Generated assets live outside of shipping content under `_Generated/Maps` so you can iterate safely without touching curated scenes.
5. Run **Tools → UberStrike → MapGen → Stack Generator v0.6** to auto-fill missing stack layers (height, flow, lighting, collision) and assign clustered themes. The upgraded JSON + PNG bundle lands in `auto_layers/` next to the source stack.
6. Launch **Tools → UberStrike → MapGen → Extract Patterns (All Scenes)** after you curate a batch. The dataset stored under `Assets/_Generated/Patterns/` feeds both DesktopAgent automation and the web preview portal.

### 🌐 Web Preview & Voting

`Tools/WebPreview/index.html` hosts a lightweight Three.js viewer (serve locally with `npx serve Tools/WebPreview`). Drop any `map.json + legend.png` pair to explore the greybox, copy a share token, and log local votes for community feedback.

## 🚀 MapGen v0.6 Highlights

- **AssetIntegrationSystem** discovers prefabs/materials under `Assets/UberStrike/`, respects the original `MapGameplaySet`, and drives the new heuristics so automatic builds feel like classic UberStrike out of the box.
- **PrefabPlacementAI** analyses layout flow, spawn distances, and walkable surfaces to place weapons, pickups, teleporter pairs, and jump pads while balancing team spawns when the builder skipped them.
- **MapPatternExtractor** adds Tools → UberStrike → MapGen menu items to analyze `.unity` scenes, gather spawn patterns, chokepoint widths, navmesh area, and export JSON style profiles for ML training.
- **StackGeneratorV6** synthesizes missing six-layer stack data (height, flow, theme, lighting, collision) with theme clustering and smart lighting placement, writing upgraded `_v0p6` assets.
- **DesktopAgent MapGenOrchestrator** (Python) orchestrates CLI runs, pattern extraction, and dataset scoring, producing ten themed variants plus QC metrics per blueprint run.
- **Adaptive LOD + Batch Factory** adds in-editor LOD generation with importance-mapped falloffs, offline mesh decimation scripts, a batch generator for tournament sets, and a master orchestrator that stitches WFC layouts, Voronoi theming, SA item placement, flow analysis, and LOD metadata into reusable map bundles.

### 🔁 Batchmode CLI

Automate the pipeline with PowerShell: `.\Tools\MapGen\Gen.ps1 -seed 42 -size 128 -t 2` (runs Unity 6000.0.56f1 in `-batchmode`, emits PNG + scene, exits on completion).

---

## ⚙️ Project Overview

**UberStrike-MapGen** is a Unity-based map generator that converts **pixel blueprints (PNG)** into fully functional 3D arena layouts.  
Each pixel corresponds to a gameplay element : floor, wall, water, spawn point, etc.

> 🧩 Think of it as "Minecraft logic meets professional FPS level design."

---

## 🧠 Core Goals

- ⚡ Speed up the creation of new UberStrike maps  
- 🧱 Automate repetitive geometry generation  
- 🎨 Enable rapid prototyping of level ideas  
- 🌐 Build towards an open UberStrike asset pipeline  

---

## 🚧 Current Status

**🛠️ Development Phase: Asset Integration & Pattern Learning**

Core geometry bugs (flat walls, cyan borders, combine stalls) are fixed in v0.6. We're currently focused on higher-level systems: prefab intelligence, stack auto-generation, pattern exports, and community preview tooling. We're actively seeking **volunteer Unity + Python developers** who want to expand the ML/data hooks and polish the new toolchain *no paid contracts, just passion and credit.*

---

## 🐛 Known Issues

| Status | Description | Location | Impact |
|--------|-------------|----------|--------|
| 🟢 Fixed (v0.6) | Walls now stay 4 m tall thanks to chunked combines plus vertex snapping. | `BuildFromBlueprint.cs` | Restores collision fidelity |
| 🟢 Fixed (v0.6) | Cyan border pixels are filtered inside `ClassifyPixel()` so they never spawn geometry. | `ClassifyPixel()` | Eliminates ghost objects |
| 🟢 Fixed (v0.6) | Mesh combines now chunk at 50 instances via `ProgressiveCombiner` to avoid stalls. | `BuildFromBlueprint.cs` | 3–5× faster processing |
| 🟡 Tracking | Materials & scale consistency need tuning | — | Visual mismatch |

---

## 📂 Project Structure
```text
uberstrike-mapgen/
├── UberStrikeGen/                      # Unity project core
│   ├── Assets/_UberStrike/
│   │   ├── Scripts/Editor/             # Main map generator (20+ .cs files)
│   │   └── Blueprints/MapLayouts/      # PNG blueprints
│   ├── ProjectSettings/                # Unity config
│   └── Packages/                       # Dependencies
│
├── DesktopAgent/                       # Python desktop automation tools
│   ├── agent/tools/                    # Unity process automation
│   ├── ask_claude.py / claude_helper.py
│   └── requirements.txt
│
└── README.md / .gitignore
```

## 🔧 Technical Highlights

### 🧩 Unity (C#)
- **BuildFromBlueprint.cs** → Main map builder
- **HeadlessBuilder.cs** → CLI & batch automation
- **QuickBuildWindow.cs** → In-Editor build UI
- **WorkingBlueprintLoader.cs** → Live preview
- **BlueprintQC.cs** → Validation & quality checks

## 🎮 Gameplay Prefabs & Themes

- `Assets/_UberStrike/Gameplay/DefaultGameplaySet.asset` defines spawn, pickup, jump pad, and teleporter prefabs plus placement spacing rules.
- `Assets/_UberStrike/Theme/DefaultTheme.asset` controls base materials and optional gameplay tint overrides applied when prefabs have empty slots.
- Blueprint sidecar overrides (`*.png.meta.json`) can tweak scale, wall height, max objects, theme, gameplay set, and free-form notes per build.
- QC gizmos draw spawn/jump spheres and teleporter links when selecting generated arenas; console logs summarize counts, floor area, and warnings.
- NavMesh generation is now optional in both Quick Build (toggle stored in EditorPrefs) and headless mode via `-navmesh=true|false`.
- Large blueprints combine meshes progressively with chunk logging to reduce memory spikes.

### 🐍 Python (Automation Layer)
- **DesktopAgent/run_assistant.py** → Unified CLI entry point
- **agent_v2/monitor/unity_monitor.py** → Real-time log monitoring with auto-fix hints
- **agent_v2/generator/layer_generator.py** → Prompt-driven six-layer stack generator
- **agent_v2/validator/stack_validator.py** → Stack validation & auto-correction helpers
- **agent_v2/analyzer/quality_analyzer.py** → Gameplay quality metrics & heatmaps
- **agent_v2/cli/assistant_cli.py** → Rich-powered interactive dashboard for hotkeys

---

## 🤖 DesktopAgent v2.0 Automation Toolkit

The **DesktopAgent** folder ships a Python companion app that keeps multi-layer
builds healthy and accelerates iteration when you are away from the Unity
Editor.

| Capability | Location | What it does |
|------------|----------|--------------|
| Real-time monitoring | `agent_v2/monitor/unity_monitor.py` | Watches `Editor.log`, classifies issues like flat walls, cyan pixels, mesh combine failures and surfaces remediation hints. |
| Automated fixes | `agent_v2/fixer/auto_fixer.py` | Patches Unity scripts (wall height, cyan guard) or augments blueprint layers (spawns, lighting) before triggering recompiles. |
| Prompt → stack generation | `agent_v2/generator/layer_generator.py` | Converts natural language requests (e.g. "4 room arena with central courtyard") into six PNG layers plus `.stack.json`. |
| Stack validation | `agent_v2/validator/stack_validator.py` | Confirms layer presence/dimensions, counts spawns & lights, and writes `_fixed` stacks when auto-fixes are applied. |
| Gameplay analysis | `agent_v2/analyzer/quality_analyzer.py` | Produces spawn balance/path diversity/verticality scores and exports heatmaps + JSON reports. |
| Interactive CLI | `agent_v2/cli/assistant_cli.py` | Rich dashboard with hotkeys: **F**ix, **A**nalyse, **G**enerate, **R**ebuild, **Q**uit. |
| Variant orchestration | `agent/tools/mapgen_orchestrator.py` | Runs MapGen CLI ten times with themed seeds, calls `MapPatternExtractor` in batchmode, and scores each export via `map.json`. |

### Installing DesktopAgent

```bash
cd DesktopAgent
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate
pip install -r requirements.txt
```

The `install.bat` helper creates the virtual environment, installs dependencies
and copies `config.yaml.template` when you are setting up on Windows.

### Common CLI commands

```bash
# Monitor the Unity editor log and stream issues in real time
python run_assistant.py monitor --real-time

# Generate a six-layer stack from a natural language description
python run_assistant.py generate "symmetrical ctf arena with twin bases"

# Auto-apply known fixes (flat walls, cyan processing, sparse spawns, lights)
python run_assistant.py fix

# Launch the interactive dashboard (Rich-based UI)
python run_assistant.py interactive
```

Generated stacks are written under the `unity.stacks_path` configured in
`DesktopAgent/config.yaml` (defaults to
`C:/UberStrikeGen/Assets/_UberStrike/Blueprints/Stacks`). The analyzer and
validator write JSON/PNG reports alongside the stack assets for quick review.

---

## 🎨 Color Legend (Blueprint Mapping)

| Color | RGB | Meaning |
|-------|-----|---------|
| ⬛ Black | (0, 0, 0) | Wall (4 m tall) |
| ⬜ Gray | (128, 128, 128) | Floor |
| 🔷 Cyan | (0, 255, 255) | Border (ignore) |
| 🔵 Blue | (0, 0, 255) | Water |
| 🔴 Red | (255, 0, 0) | Spawn – Red team |
| 🟢 Green | (0, 255, 0) | Spawn – Green team |
| 🟡 Yellow | (255, 255, 0) | Spawn – Neutral |
| 🟣 Purple | (128, 0, 128) | Bridge / Walkway |

---

## 🆕 Multi-Layer Blueprint Stack (v0.5)

The v0.5 toolchain introduces **multi-layer blueprint stacks**: a coordinated set of six PNG layers plus a JSON sidecar that describes generation parameters. Stack assets live under `Assets/_UberStrike/Blueprints/Stacks/` and can be imported via **Tools → UnityAI → Build From Layer Stack…**.

### Layer Breakdown

| Layer | File suffix | Purpose | Accepted keys |
|-------|-------------|---------|----------------|
| Layout | `.layout.png` | Core geometry (walls, bridges, floors, perimeter) | Black = walls, Gray = floors, Purple = bridges, Cyan = border, White = empty |
| Height | `.height.png` | Grayscale heightmap applied to each tile | 0–255 grayscale (converted via `heightScale`) |
| Flow | `.flow.png` | Gameplay flow markers | Spawn (Yellow/Red/Green), choke (Orange), cover (Gray), arrow (Cyan) |
| Theme | `.theme.png` | Region material tags | Any color defined in `themeMap` |
| Lighting | `.lighting.png` | Light placement & atmosphere hints | Point (`lighting.pointColor`), Spot (`lighting.spotColor`) |
| Collision | `.collision.png` | Collider masks | Walkable, Blocked, Climbable, Destructible |

Each stack also includes `{name}.stack.json` which binds the files and exposes tuning knobs:

```json
{
  "metersPerPixel": 1.0,
  "wallHeight": 4.0,
  "heightScale": 0.05,
  "stairsRise": 0.25,
  "rampMaxSlopeDeg": 25,
  "doorWidthMeters": 2.0,
  "bridgeWidthMeters": 3.0,
  "pairTeleporters": true,
  "navmesh": true,
  "themeDefault": "DefaultTheme",
  "themeMap": {
    "#222222": "Industrial",
    "#554433": "Warehouse",
    "#334455": "BlueSteel"
  },
  "flow": {
    "spawnColorYellow": "#FFFF00",
    "spawnColorRed": "#FF0000",
    "spawnColorGreen": "#00FF00",
    "chokeColor": "#FFA500",
    "coverColor": "#808080",
    "arrowColor": "#00FFFF"
  },
  "lighting": {
    "pointColor": "#FFFFFF",
    "spotColor": "#FFD080",
    "sunDirDeg": [50, -30, 0],
    "fogDensity": 0.02
  },
  "collision": {
    "walkable": "#FFFFFF",
    "blocked": "#000000",
    "climbable": "#00AAFF",
    "destructible": "#FF00FF"
  }
}
```

### Importing & Previewing

1. Open **Tools → UnityAI → Build From Layer Stack…**.
2. Select the `.stack.json` file – the window previews the detected layers and tunable values.
3. Use **Preview** to open a disposable scene with gizmos (bounds, walls, door cuts, height legend).
4. Press **Build** to generate the full arena (floors, walls, ramps, spawns, themes, lights, colliders, navmesh).

The Quick Build utility now includes **Tools → UnityAI → Quick Build ► From Layer Stack…** which remembers the last path in `EditorPrefs`. For CI/headless usage pass `-stack "path/to/Stack.stack.json"` to the Unity command line; the old single-PNG flow is still supported.

Sample content: `Assets/_UberStrike/Blueprints/Stacks/ArenaStack_Sample.*` provides a ready-to-use stack for smoke testing.
To keep the repository text-only for PR tooling, the layer images are stored as Base64 blobs alongside the JSON (files end in
`.png.txt`).
`StackLoader` transparently decodes these into textures, and real projects can continue to reference standard `.png` assets
without any changes.

---

## 🚀 Setup

### Requirements
- Unity 6000.2.6f2
- Python 3.11+
- Windows 10/11
- 8 GB RAM minimum

### 🧩 Installation Steps

#### 1️⃣ Clone the repository
```bash
# Clone repo
git clone https://github.com/constripacity/uberstrike-mapgen.git
cd uberstrike-mapgen
```

#### 2️⃣ Unity Setup
- Open Unity Hub → Add Project → select UberStrikeGen
- Use Unity 6000.2.6f2
- Wait for scripts to compile

#### 3️⃣ Python Setup
```bash
cd DesktopAgent
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

(Optional) Copy `.env.example` → `.env` and add your Anthropic API key if using AI automation.

---

## 🧭 How It Works

1. Load PNG blueprint (1024 × 1024 px)
2. Classify pixels → assign to wall, floor, etc.
3. Generate 3D geometry procedurally
4. Merge meshes & materials for performance
5. Output playable Unity scene (.unity)

---

## 🗓️ Development Roadmap

| Phase | Focus |
|-------|-------|
| 1. Stabilization | Fix wall/cyan bugs, optimize CombineMeshes |
| 2. Core Expansion | Material system, prefabs, texture mapping |
| 3. Gameplay Assets | Generate pickups, jump pads, teleporters |
| 4. Full Ecosystem | Weapons, skins, decorative props |
| 5. Automation | Web blueprint editor + cloud builds |

---

## 🤖 Phase 3 – AI-Assisted Generation & Testing

Phase 3 extends the toolchain with machine-learning workflows, automated bot
playtests, and a collaborative browser-based editor.

### Key Components

| Feature | Location | Summary |
|---------|----------|---------|
| Training data exporter | `Assets/_UberStrike/Scripts/AI/MapTrainingPipeline.cs` | Scans stack blueprints, records wall ratios, spawn balance and verticality metrics, and writes a ML friendly dataset. |
| Diffusion layout model | `DesktopAgent/agent_v2/ai/layout_diffusion.py` | Optional PyTorch/diffusers powered generator that learns layout patterns and can interpolate between existing arenas. |
| Bot playtesting | `Assets/_UberStrike/Scripts/Testing/BotPlaytester.cs` | Spawns NavMesh agents, tracks their movement, and emits heatmaps plus JSON reports for balance reviews. |
| Wave Function Collapse generator | `Assets/_UberStrike/Scripts/Generators/WaveFunctionCollapse.cs` | Learns adjacency rules from sample layouts and produces new wall/floor masks. |
| Advanced metrics | `Assets/_UberStrike/Scripts/Analysis/AdvancedMetrics.cs` | Computes connectivity, spawn safety, sightlines, cover density, and exports analytic heatmaps. |
| Optimization pipeline | `Assets/_UberStrike/Scripts/Optimization/MapOptimizer.cs` | Combines meshes, generates LODs, simplifies colliders and emits optimization summaries. |
| Tournament validator | `Assets/_UberStrike/Scripts/Tournament/TournamentValidator.cs` | Runs competitive checks and stores validation reports for tournament-ready arenas. |
| Variation generator | `DesktopAgent/agent_v2/generator/variation_generator.py` | Mutates existing stacks, applies validator auto-fixes, and archives new variants. |
| Web editor | `DesktopAgent/web_editor/app.py` | Flask + Socket.IO collaborative layer viewer with real-time pixel edits and Three.js preview. |
| Integration entry point | `phase3_integration.py` | Boots the diffusion model, variation generator, and launches the collaborative editor. |

### Getting Started

1. **Export training data** inside Unity via `Tools → UberStrike → Export Training Data`.
2. **Train the layout diffusion model** (optional) with `python phase3_integration.py` – the script trains when a dataset exists and no model checkpoint is present.
3. **Launch the web editor** by running `python phase3_integration.py` or manually executing `python DesktopAgent/web_editor/app.py`.
4. **Run automated bot tests** using `Tools → UberStrike → Test with Bots` to produce heatmaps and reports under `Assets/_UberStrike/Testing/Reports`.
5. **Validate competitive readiness** with `Tools → UberStrike → Validate For Tournament` – JSON summaries land in `Assets/_UberStrike/TournamentReports`.

### GPU Notes

The diffusion model automatically switches between PyTorch/DDPM training and a
statistical fallback depending on your environment. When an RTX-class GPU is
available the trainer leverages CUDA; otherwise the fallback still produces
varied procedural layouts.


## 🤝 Contributing

### We Need Volunteer Developers! 💪

If you love UberStrike, Unity, or retro FPS design, your help is welcome.  
No contracts, no obligations, just community collaboration and shared credit.

### Ideal Skills
- Unity Editor scripting
- Procedural mesh generation
- 3D geometry optimization
- Shader / material systems

### How to Join
- Fork the repo
- Fix or improve something
- Submit a pull request
- Join the community discussion

---

## 📢 Contact

For contributions or collaboration:
- Open a GitHub Issue tagged **[Volunteer-Dev]**
- Mention your Unity experience
- Or share ideas for improving generation logic

---

## ⚖️ Disclaimer

This project is community-driven and not affiliated with CMUNE.  
All original UberStrike assets remain property of their respective owners.  
This repository contains no proprietary assets, only open-source tools that assist in the fan-made revival.

---

## 🕹️ Credits

- **UberStrike Community Developers** (2022 – Present) – for reviving the servers
- **Original CMUNE Team** – for allowing the community to rebuild
- **Constripacity** – for founding this project and architecting the map generation pipeline
