# Quantum MapGen — Session Log

## Project Status (2026-03-10)

### Package Built
- `qubo_encoder.py` (repo root) — QUBO formulation with 7 constraints, encode/decode/validate
- `quantum_mapgen/braket_runner.py` — QAOA circuit builder, LocalSimulator + SV1 modes, greedy repair, cost tracking
- `quantum_mapgen/exp1_basic_connectivity.py` — 3 power items proof-of-concept (local + cloud)
- `quantum_mapgen/exp1_scaling.py` / `exp1_scaling_quick.py` — Candidate scaling study
- `quantum_mapgen/exp1_scaling_results.json` — Local scaling data (candidates 4-8)
- `quantum_mapgen/sv1_usage_log.json` — SV1 budget tracker (manual ledger)
- `quantum_mapgen/sv1_task1_results.json`, `sv1_task2_results.json` — Raw SV1 33-qubit measurement results

### AWS Configuration
- **Account:** <AWS_ACCOUNT_ID>
- **IAM User:** braket-dev (least-privilege, Braket + S3 only)
- **S3 Bucket:** Set via `BRAKET_S3_BUCKET` env var
- **Region:** us-east-1
- **Credentials:** `~/.aws/credentials` (not committed)
- **QPU spending:** All QPUs locked to $0 spending limits across us-east-1 and eu-north-1

### Environment
- Shadow Cloud PC, Windows 11, Python 3.11
- Braket SDK v1.113.0 + amazon-braket-default-simulator v1.34.1
- AWS CLI v2.34.5

---

## Local Scaling Results

Source: `exp1_scaling_results.json`

| Candidates | Qubits | QAOA Gates | Runtime | Best Energy | Violations | Feasible |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| 4 | 12 | 456 | 7.3s | 2589 | 2 | NO |
| 5 | 15 | 705 | 10.3s | 2496 | 2 | NO |
| 6 | 18 | 1008 | 17.5s | 2447 | 1 | NO |
| 7 | 21 | 1365 | 52.2s | 3054 | 2 | NO |
| 8 | 24 | 1776 | 373.2s | 2653 | 1 | NO |
| 9 | 27 | ~2200 | >3600s (killed) | --- | --- | --- |

Key observations:
- Runtime roughly 3-7x per +3 qubits: textbook exponential blowup
- Energy trend: slight improvement with more candidates (2589 -> 2447 at 6 cands)
- Violations reduced from 2 to 1 with more candidates (more well-spaced options)

---

## SV1 Cloud Results (33 qubits, 11 candidates)

3 tasks submitted, all 3 completed successfully (500 total shots):

| Task | Created | Ended | Shots | Wall Time |
|------|---------|-------|-------|-----------|
| Task 1 | 20:38 UTC | 21:22 UTC | 200 | 43.8 min |
| Task 2 | 21:22 UTC | 22:06 UTC | 200 | 43.9 min |
| Task 3 | 21:51 UTC | 22:35 UTC | 100 | 44.1 min |

**Best repaired placement (from 500 measurements, top-50 repair):**
- sniper -> (32.0, 48.0)
- rocket -> (48.0, 24.0)
- armor_heavy -> (8.0, 24.0)

**Spacing results:**
- rocket <-> armor_heavy: 40.0m (req: 40m) **OK**
- sniper <-> rocket: 28.8m (req: 50m) VIOLATION
- sniper <-> armor_heavy: 33.9m (req: 50m) VIOLATION

**SA weighted energy: 1339.5** (vs local best 2447 at 6 candidates)

### SV1 Budget
- 3 completed tasks, 500 total shots (200 + 200 + 100), all 33-qubit circuits
- Estimated ~15-20 min SV1 sim time consumed (wall time != billed sim time)
- ~40-45 min remaining of 60 min/month free tier
- Billing verification pending (24h delay from AWS)
- Check: `console.aws.amazon.com/costmanagement/home#/freetier`

---

## CRITICAL LESSONS — DO NOT REPEAT

### 1. NEVER run optimization loops on SV1
Each 33-qubit circuit takes ~44 min wall time per task. COBYLA with 30 iterations would submit 30+ tasks = potentially 150+ min of SV1 time.

**Correct approach:** Optimize QAOA parameters locally on a smaller problem (e.g., 8 candidates / 24 qubits), then submit ONE circuit to SV1 with those fixed parameters and more shots.

### 2. SV1 free tier budget
1 hour simulation time per month for 12 months. Track every cloud run in `sv1_usage_log.json`. Wall time is NOT billed time — only actual SV1 compute is billed.

### 3. Local simulator ceiling
Shadow PC (24GB VRAM, ~16GB system RAM for Python) tops out at ~24 qubits (8 candidates) before runtime becomes impractical. 27 qubits ran >1 hour before being killed.

### 4. Windows console encoding
Windows cp1252 console cannot print unicode characters. Use ASCII only in all `print()` statements. Unicode in comments/docstrings is fine.

### 5. S3 bucket format for Braket
Pass bucket name WITHOUT `s3://` prefix to the Braket SDK:
```python
s3_destination_folder=(bucket_name, prefix)  # NOT f"s3://{bucket_name}"
```

### 6. One-hot penalty strength
penalty_one_hot=500 is not strong enough for QAOA to find feasible solutions directly. The greedy repair (`repair_solution()` / `repair_top_k()`) is essential for projecting infeasible QAOA outputs to valid placements.

---

## Architecture

### Pipeline
```
WFC Layout -> Voronoi Themes -> QUBO Encoder -> Quantum Solver -> Decode -> Flow Analysis -> Unity Build
                                     |                |
                                [Constraint      [Classical SA
                                 Catalog]          Fallback]
```

### QUBO Formulation
6 active constraints encoded:
- **C-001 Spawn Balance** (w=10): quadratic pairwise advantage differences
- **C-002 Risk/Reward** (w=5): linear exposure penalties
- **C-003 Flow Alignment** (w=3): linear choke proximity
- **C-004 Spacing** (w=7): quadratic, natural QUBO fit
- **C-005 Strategic Depth** (w=4): quadratic centroid offset
- **C-006 Walkability**: structural (candidates are walkable by construction)
- **C-007 One-Hot** (A=500): quadratic, textbook QUBO

### Problem Sizes
- Minimum viable: 3 items x 7 candidates = 21 qubits (local)
- SV1 limit: 3 items x 11 candidates = 33 qubits
- Realistic: 25 items x 50 candidates = 1,250 variables -> D-Wave Advantage
- Full resolution: 25 items x 150 candidates = 3,750 variables -> D-Wave Advantage

### Classical SA Reference
- mapgen-environment: `SimulatedAnnealingPlacer.cs` (T=750, cooling=0.96, 4500 iters)
- Python port: `DesktopAgent/agent/tools/simulated_annealing_placer.py` (T=1000, cooling=0.95, 7500 iters)
- Aggressive cooling (alpha=0.95) creates effective optimization window of ~160-200 iterations
- Local minima are a known issue, especially for spawn balance with asymmetric spawn points

---

## 2026-04-02 — Full Capability Audit

Completed full MapGen capability audit (38,800 LOC, 180 files). Produced `MAPGEN_CAPABILITY_REPORT.md` with 10-section analysis covering every source file in the repo. Created theoretical $10K investment plan mapping development priorities. Decision: execute all coding work via Claude Code sessions instead of hiring developers.

**Key quantum-relevant findings:**
- QUBO encoder (947 lines) and QAOA runner (720 lines) are PRODUCTION status
- Greedy repair is essential — one-hot penalty=500 still insufficient for direct QAOA feasibility
- Local ceiling: ~24 qubits. SV1 ceiling: 33 qubits. D-Wave target: 3750+ qubits
- Next quantum step: submit D-Wave LaunchPad application, then build quantum->Unity adapter

## Next Steps

1. **Submit D-Wave LaunchPad application** (draft ready in local-only files)
2. **Session A:** Port WFC to C# with backtracking (replace WFCCore.cs stub)
3. **Build quantum->Unity adapter** after D-Wave acceptance
4. **Full 25-item placement** on D-Wave QPU (3750+ qubits)

---

## File Index

```
uberstrike-mapgen/
├── qubo_encoder.py                           # QUBO formulation (repo root)
├── braket_test.py                            # Braket connectivity test
├── QUANTUM_MAPGEN_ANALYSIS.md                # Full 4-phase analysis
├── quantum_mapgen/
│   ├── __init__.py
│   ├── braket_runner.py                      # QAOA runner (local + cloud)
│   ├── exp1_basic_connectivity.py            # Experiment 1: 3 power items
│   ├── exp1_scaling.py                       # Full scaling study (slow)
│   ├── exp1_scaling_quick.py                 # Quick scaling (4-8 candidates)
│   ├── exp1_scaling_results.json             # Local scaling data
│   ├── sv1_usage_log.json                    # SV1 budget tracker
│   ├── sv1_task1_results.json                # SV1 raw results (task 1)
│   ├── sv1_task2_results.json                # SV1 raw results (task 2)
│   ├── SESSION_LOG.md                        # This file
│   ├── visualize_results.py                  # Result charts
│   ├── chart_a_runtime_vs_qubits.png         # Chart A
│   └── chart_b_energy_vs_candidates.png      # Chart B
```

### Local-only files (gitignored, backed up to Downloads)
```
C:\Users\Shadow\Downloads\quantum_mapgen_secrets\
├── braket-dev_accessKeys.csv                 # AWS IAM access keys
├── sv1_usage_log.json                        # SV1 budget tracker (has task ARNs + account ID)
├── sv1_task1_results.json                    # SV1 raw results (has account ID in ARN)
├── sv1_task2_results.json                    # SV1 raw results (has account ID in ARN)
├── exp1_scaling_results.json                 # Local scaling data
└── DWAVE_APPLICATION.md                      # D-Wave LaunchPad application draft
```
