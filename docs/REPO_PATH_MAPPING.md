# Repo Path Mapping: Public Repo and Local Dev Environment

> **Purpose:** Document the path translation between this repo
> (`uberstrike-mapgen`) and a local Unity 2022 dev environment that
> consumes the MapGen sources as an Unity `Assets/`-rooted overlay,
> so syncs in either direction land in the right tree.
>
> **Last verified:** 2026-05-02 against the Session A WFC port
> (`WFCCore.cs` byte-identical on both sides).

---

## The two roles

| Tree | Role | Layout |
|---|---|---|
| This repo | Source of truth. Where commits land and get pushed. Multi-target: `UberStrike2022/`, `UberStrikeGen/`, `MapGen_Project/`, `DesktopAgent/`, `quantum_mapgen/`. | Sibling top-level dirs per target. |
| Local Unity 2022 dev environment | Compile + play surface. Where Unity 2022.3.x actually opens, builds, and tests. Built by combining an upstream UberStrike Unity 2022 project with the MapGen sources from this repo as an `Assets/`-rooted overlay. | Standard Unity project: `Assets/`, `ProjectSettings/`, `Packages/`. |

**Default direction of work:** edit in the dev environment (because that is where Unity opens), mirror back to this repo with translated paths, commit and push here. Edits made only in the dev environment are versioned by whatever git project owns that environment, not by this repo, and will not reach this public mirror until they are mirrored back.

---

## Path translation table

For files in this repo's `UberStrike2022/` subtree, the dev-env equivalents live under `Assets/`. The dev env also has `.meta` files alongside every `.cs`; those are Unity-generated and stay in the dev env.

| This repo | Dev env |
|---|---|
| `UberStrike2022/Editor/<file>.cs` | `Assets/MapGen/Editor/<file>.cs` (+ `.meta`) |
| `UberStrike2022/Editor/Stubs/<file>.cs` | `Assets/MapGen/Editor/Stubs/<file>.cs` (+ `.meta`) |
| `UberStrike2022/Integration/Editor/<file>.cs` | `Assets/MapGen/Integration/Editor/<file>.cs` (+ `.meta`) |
| `UberStrike2022/Runtime/<file>.cs` | `Assets/MapGen/Runtime/<file>.cs` (+ `.meta`) |

### Two cross-folder exceptions

These two files do not sit under the `Assets/MapGen/` overlay in the dev env. They live in the host project's own `Scripts/Scene/` directory so the runtime scene system can load them:

| This repo | Dev env |
|---|---|
| `UberStrike2022/Runtime/MapGenDiagnostics.cs` | `Assets/Scripts/Scene/MapGenDiagnostics.cs` (+ `.meta`) |
| `UberStrike2022/Runtime/MapGenMapInjector.cs` | `Assets/Scripts/Scene/MapGenMapInjector.cs` (+ `.meta`) |

A flat `cp -r UberStrike2022/Runtime/* <dev-env>/Assets/MapGen/Runtime/` is wrong: these two need separate handling.

---

## Other relevant trees (no cross-tree counterpart in this repo)

| Tree | Where it lives | Notes |
|---|---|---|
| MapGen blueprint + map content | Dev env only, under `Assets/_UberStrike/` | Not in this repo. Depends on host runtime types. |
| `Assets/MapGen/Blueprints/`, `Generated/`, `Resources/` | Dev env only | Authored / generated content, not source code. |
| Python `DesktopAgent/` | This repo only | Runs out-of-process; communicates with Unity via the HTTP `AgentBridge`. |
| Quantum module (`quantum_mapgen/`) | This repo only | Standalone; not consumed by Unity. |

---

## Sync workflow

**Dev env to this repo (the common direction)**

1. Identify changed file paths in the dev env (only `.cs` under `Assets/MapGen/...` or `Assets/Scripts/Scene/MapGen*`; ignore `.meta`, `Library/`, `Generated/`).
2. Translate each path using the table above.
3. Copy file contents into this repo at the translated path.
4. Verify with `git diff` that only intended files changed.
5. Commit and push.

**This repo to dev env (less common, typically only on overlay refresh)**

1. From this repo, identify changed `.cs` files under `UberStrike2022/`.
2. Translate to dev-env paths via the table.
3. Copy. The dev env's existing `.meta` files stay; do not overwrite them with anything from this repo (this repo does not carry `.meta`).
4. Open Unity 2022 in the dev env; let it recompile and re-resolve references.

**What never crosses:**
- `.meta` files (Unity-managed in the dev env, absent here).
- `Library/`, `Temp/`, `Logs/` (Unity-generated, dev-env only).
- Python (`DesktopAgent/`, `quantum_mapgen/`); this repo only.
- `Assets/_UberStrike/` content; dev env only.

---

## Known divergences as of 2026-05-02

- `WFCCore.cs`: identical, 664 lines on both sides since the Session A graft on 2026-04-26 (commit `0660297` here).
- `VoronoiThemeGenerator.cs`, `FlowAnalysisCore.cs`, `StackPreviewer.cs`: both sides still on the original stub bodies. Will diverge during Sessions B and C; those ports should land in the dev env first, then mirror here.

---

## Why this doc exists

The two trees use different folder structures because the dev env is a Unity 2022 project that consumes MapGen as an `Assets/`-rooted overlay, while this repo is a multi-target source repo with several sibling top-level directories. A flat copy in either direction lands files in the wrong place. Writing the mapping down once removes the per-session re-derivation cost and the risk of silently grafting a file into the wrong path.
