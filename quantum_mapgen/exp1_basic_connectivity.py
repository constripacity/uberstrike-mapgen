"""
exp1_basic_connectivity.py — Experiment 1: Proof of Concept

Place 3 power items (sniper, rocket, armor_heavy) on a synthetic arena
using QAOA via Amazon Braket.

From QUANTUM_MAPGEN_ANALYSIS.md:
    Problem:     3 items, reduced candidate set
    Constraints: One-hot (3 items), spacing, strategic depth
    Local:       3 × 7 candidates = 21 qubits (LocalSimulator)
    Cloud:       3 × 11 candidates = 33 qubits (SV1, within 34-qubit limit)
    Metric:      All 3 items placed in valid walkable positions with spacing

Usage:
    python -m quantum_mapgen.exp1_basic_connectivity           # local (default)
    python -m quantum_mapgen.exp1_basic_connectivity --cloud    # SV1

Author: Culo / Quantum MapGen Project
"""

import numpy as np
import sys
import os
import time
import argparse

# Add repo root to path so we can import qubo_encoder
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from qubo_encoder import MapGenQUBOEncoder, UBERSTRIKE_ITEM_RULES
from quantum_mapgen.braket_runner import QAOARunner


# ─────────────────────────────────────────────────────────
# Experiment configuration
# ─────────────────────────────────────────────────────────

# 3 power items only — the minimum viable quantum problem
EXP1_ITEM_RULES = {
    "sniper": UBERSTRIKE_ITEM_RULES["sniper"],        # count=1, spacing=50m
    "rocket": UBERSTRIKE_ITEM_RULES["rocket"],         # count=1, spacing=40m
    "armor_heavy": UBERSTRIKE_ITEM_RULES["armor_heavy"],  # count=1, spacing=35m
}

# Qubit budget per mode
LOCAL_MAX_CANDIDATES = 7    # 3 items × 7 = 21 qubits
CLOUD_MAX_CANDIDATES = 11   # 3 items × 11 = 33 qubits (SV1 limit: 34)


def create_synthetic_arena(size: int = 64):
    """Create a synthetic 64×64 arena with walkable interior.

    Layout (roughly matches a small UberStrike arena):
        - Outer walls (4-cell border)
        - Interior floor with some obstacles
        - 4 symmetric spawn points
        - 4 choke points at corridor entrances
        - Cover positions near walls and obstacles
    """
    mask = np.zeros((size, size), dtype=bool)

    # Walkable interior (leaving 4-cell border)
    mask[4:60, 4:60] = True

    # Cut some obstacles to create structure
    # Central pillar
    mask[28:36, 28:36] = False
    # Corner rooms with doorways
    mask[8:16, 8:16] = False      # NW room (solid block)
    mask[8:16, 48:56] = False     # NE room
    mask[48:56, 8:16] = False     # SW room
    mask[48:56, 48:56] = False    # SE room
    # Corridors remain walkable (the spaces between obstacles)

    # Spawn points — 4 corners, just outside the room blocks
    spawns = [
        (18.0, 18.0),   # NW
        (46.0, 18.0),   # NE
        (18.0, 46.0),   # SW
        (46.0, 46.0),   # SE
    ]

    # Choke points — corridor entrances between quadrants
    chokes = [
        (32.0, 18.0),   # N corridor
        (18.0, 32.0),   # W corridor
        (46.0, 32.0),   # E corridor
        (32.0, 46.0),   # S corridor
    ]

    # Cover positions — near walls and obstacles
    covers = [
        # Near central pillar
        (26.0, 32.0), (38.0, 32.0), (32.0, 26.0), (32.0, 38.0),
        # Near corner blocks
        (17.0, 10.0), (10.0, 17.0),
        (47.0, 10.0), (54.0, 17.0),
        (10.0, 47.0), (17.0, 54.0),
        (47.0, 54.0), (54.0, 47.0),
        # Mid-wall positions
        (4.0, 32.0), (59.0, 32.0), (32.0, 4.0), (32.0, 59.0),
    ]

    return mask, spawns, chokes, covers


def run_experiment(mode: str = "local", p: int = 1, optimize_iters: int = 30,
                   shots: int = 1000, shots_per_eval: int = 200, seed: int = 42):
    """Run Experiment 1: 3 power items on a synthetic arena."""

    print("=" * 60)
    print("  Experiment 1 -- Basic Connectivity (Proof of Concept)")
    print("=" * 60)

    # ── Step 1: Create arena ──
    print("\n[1/5] Creating synthetic arena...")
    mask, spawns, chokes, covers = create_synthetic_arena(64)
    walkable_cells = int(mask.sum())
    print(f"  Arena: 64x64, {walkable_cells} walkable cells")
    print(f"  Spawns: {len(spawns)}, Chokes: {len(chokes)}, Covers: {len(covers)}")

    # ── Step 2: Build encoder ──
    max_candidates = CLOUD_MAX_CANDIDATES if mode == "cloud" else LOCAL_MAX_CANDIDATES
    n_items = sum(r["count"] for r in EXP1_ITEM_RULES.values())  # = 3
    n_qubits = n_items * max_candidates

    print(f"\n[2/5] Building QUBO encoder...")
    print(f"  Items: {n_items} ({', '.join(EXP1_ITEM_RULES.keys())})")
    print(f"  Max candidates: {max_candidates} (-> {n_qubits} qubits)")
    print(f"  Mode: {mode}")

    # Create full encoder first, then subsample candidates
    # Use a large stride to get a manageable number of candidates
    # We want exactly max_candidates, so find the right stride
    # With a 56×56 walkable area minus obstacles ≈ 2800 cells
    # stride=8 → ~44 candidates, stride=10 → ~28, stride=12 → ~20

    # Start with stride that gives us enough candidates, then subsample
    encoder_full = MapGenQUBOEncoder(
        walkable_mask=mask,
        spawn_points=spawns,
        choke_points=chokes,
        cover_positions=covers,
        item_rules=EXP1_ITEM_RULES,
        candidate_stride=8,  # coarse sampling
    )

    # Subsample to exact candidate count via create_subproblem
    if encoder_full.n_candidates > max_candidates:
        encoder = encoder_full.create_subproblem(
            list(EXP1_ITEM_RULES.keys()),
            max_candidates=max_candidates,
        )
    else:
        encoder = encoder_full

    stats = encoder.get_stats()
    print(f"  Final candidates: {stats['n_candidates']}")
    print(f"  Final variables:  {stats['n_variables']}")
    print(f"  Final qubits:     {stats['n_variables']}")

    # Print candidate positions
    print(f"\n  Candidate positions:")
    for i, (cx, cz) in enumerate(encoder.candidates):
        print(f"    [{i:2d}] ({cx:5.1f}, {cz:5.1f})")

    # ── Step 3: Encode QUBO ──
    # High one-hot penalty so QAOA respects the constraint
    # Default 100 is too weak for this problem density
    one_hot_penalty = 500.0
    print(f"\n[3/5] Encoding QUBO matrix (one-hot penalty={one_hot_penalty})...")
    Q = encoder.encode(penalty_one_hot=one_hot_penalty)
    n_linear = sum(1 for (i, j) in Q if i == j)
    n_quad = sum(1 for (i, j) in Q if i != j)
    print(f"  QUBO terms: {len(Q)} ({n_linear} linear + {n_quad} quadratic)")
    print(f"  Encode time: {encoder._encode_time:.3f}s")

    # Sanity check: verify QUBO with a hand-picked valid placement
    print(f"\n  Sanity check -- hand-picked placement energy:")
    # Pick 3 well-spaced candidates
    if len(encoder.candidates) >= 3:
        test_placement = {
            "sniper": [encoder.candidates[0]],
            "rocket": [encoder.candidates[len(encoder.candidates) // 2]],
            "armor_heavy": [encoder.candidates[-1]],
        }
        test_report = encoder.validate(Q, test_placement)
        print(f"    QUBO energy: {test_report['qubo_energy']:.2f}")
        print(f"    SA energy:   {test_report['sa_weighted_total']:.2f}")
        print(f"    One-hot OK:  {test_report['one_hot_valid']}")

    # ── Step 4: Run QAOA ──
    print(f"\n[4/5] Running QAOA ({mode})...")
    runner = QAOARunner(
        Q=Q,
        n_vars=encoder.n_vars,
        encoder=encoder,
        mode=mode,
        # s3_bucket from BRAKET_S3_BUCKET env var (set in braket_runner.py)
    )

    result = runner.run(
        shots=shots,
        p=p,
        optimize_iters=optimize_iters,
        shots_per_eval=shots_per_eval,
        seed=seed,
        verbose=True,
    )

    # ── Step 5: Detailed analysis ──
    print(f"\n[5/5] Detailed analysis...")

    # Use repaired placement for analysis (always feasible)
    placement = result.get("repaired_placement") or result.get("placement")
    placement_label = "repaired" if result.get("repaired_placement") else "raw"

    if placement:
        sa_energy = encoder.compute_sa_energy(placement)
        print(f"\n  SA-equivalent energy breakdown ({placement_label}):")
        for key, val in sa_energy.items():
            print(f"    {key:<20s}: {val:.2f}")

        # Check spacing between placed items
        print(f"\n  Inter-item distances:")
        items = []
        for itype, positions in placement.items():
            for pos in positions:
                items.append((itype, pos))
        for i in range(len(items)):
            for j in range(i + 1, len(items)):
                t1, p1 = items[i]
                t2, p2 = items[j]
                dist = np.sqrt((p1[0] - p2[0]) ** 2 + (p1[1] - p2[1]) ** 2)
                req = max(
                    EXP1_ITEM_RULES[t1]["min_spacing"],
                    EXP1_ITEM_RULES[t2]["min_spacing"],
                )
                status = "OK" if dist >= req else "VIOLATION"
                print(f"    {t1} <-> {t2}: {dist:.1f}m (req: {req:.0f}m) [{status}]")

    # Energy landscape summary
    if result["solutions"]:
        energies = [s["energy"] for s in result["solutions"]]
        print(f"\n  Energy landscape ({len(energies)} unique solutions):")
        print(f"    Min:    {min(energies):.2f}")
        print(f"    Max:    {max(energies):.2f}")
        print(f"    Mean:   {np.mean(energies):.2f}")
        print(f"    Median: {np.median(energies):.2f}")
        print(f"    Std:    {np.std(energies):.2f}")

    print(f"\n{'='*60}")
    print(f"  Experiment 1 complete. Total time: {result['total_time_s']:.1f}s")
    print(f"{'='*60}\n")

    return result


# ─────────────────────────────────────────────────────────
# CLI
# ─────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Exp 1: QAOA proof-of-concept with 3 power items"
    )
    parser.add_argument(
        "--cloud", action="store_true",
        help="Use SV1 cloud simulator instead of LocalSimulator"
    )
    parser.add_argument(
        "--p", type=int, default=1,
        help="Number of QAOA layers (default: 1)"
    )
    parser.add_argument(
        "--optimize-iters", type=int, default=30,
        help="Max COBYLA optimization iterations (default: 30)"
    )
    parser.add_argument(
        "--shots", type=int, default=1000,
        help="Final measurement shot count (default: 1000)"
    )
    parser.add_argument(
        "--shots-per-eval", type=int, default=200,
        help="Shots per optimization evaluation (default: 200)"
    )
    parser.add_argument(
        "--seed", type=int, default=42,
        help="Random seed (default: 42)"
    )

    args = parser.parse_args()
    mode = "cloud" if args.cloud else "local"

    run_experiment(
        mode=mode,
        p=args.p,
        optimize_iters=args.optimize_iters,
        shots=args.shots,
        shots_per_eval=args.shots_per_eval,
        seed=args.seed,
    )


if __name__ == "__main__":
    main()
