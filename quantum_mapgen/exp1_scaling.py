"""
exp1_scaling.py -- Candidate scaling study for D-Wave LaunchPad data

Runs Experiment 1 (3 power items) at increasing candidate counts to produce
a scaling curve: solution quality vs. problem size. This data shows where
the local simulator hits its qubit ceiling, motivating D-Wave access.

Memory requirements (state vector = 2^n * 16 bytes):
    15 qubits (5 cands):  512 KB
    21 qubits (7 cands):   32 MB
    24 qubits (8 cands):  256 MB
    27 qubits (9 cands):    2 GB
    30 qubits (10 cands):  16 GB  <-- likely local limit
    33 qubits (11 cands): 128 GB  <-- needs SV1

Usage:
    python -m quantum_mapgen.exp1_scaling
    python -m quantum_mapgen.exp1_scaling --max-candidates 10

Author: Culo / Quantum MapGen Project
"""

import numpy as np
import sys
import os
import json
import time
import traceback
import argparse

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from qubo_encoder import MapGenQUBOEncoder, UBERSTRIKE_ITEM_RULES
from quantum_mapgen.braket_runner import QAOARunner

# Same item rules as exp1
EXP1_ITEM_RULES = {
    "sniper": UBERSTRIKE_ITEM_RULES["sniper"],
    "rocket": UBERSTRIKE_ITEM_RULES["rocket"],
    "armor_heavy": UBERSTRIKE_ITEM_RULES["armor_heavy"],
}

SPACING_REQUIREMENTS = {
    ("sniper", "rocket"): 50.0,
    ("sniper", "armor_heavy"): 50.0,
    ("rocket", "armor_heavy"): 40.0,
}


def create_arena():
    """Same synthetic arena as exp1."""
    mask = np.zeros((64, 64), dtype=bool)
    mask[4:60, 4:60] = True
    mask[28:36, 28:36] = False
    mask[8:16, 8:16] = False
    mask[8:16, 48:56] = False
    mask[48:56, 8:16] = False
    mask[48:56, 48:56] = False

    spawns = [(18.0, 18.0), (46.0, 18.0), (18.0, 46.0), (46.0, 46.0)]
    chokes = [(32.0, 18.0), (18.0, 32.0), (46.0, 32.0), (32.0, 46.0)]
    covers = [
        (26.0, 32.0), (38.0, 32.0), (32.0, 26.0), (32.0, 38.0),
        (17.0, 10.0), (10.0, 17.0), (47.0, 10.0), (54.0, 17.0),
        (10.0, 47.0), (17.0, 54.0), (47.0, 54.0), (54.0, 47.0),
        (4.0, 32.0), (59.0, 32.0), (32.0, 4.0), (32.0, 59.0),
    ]
    return mask, spawns, chokes, covers


def compute_spacing_violations(placement):
    """Count spacing violations and return details."""
    violations = 0
    details = []
    items_list = []
    for itype, positions in placement.items():
        for pos in positions:
            items_list.append((itype, pos))

    for i in range(len(items_list)):
        for j in range(i + 1, len(items_list)):
            t1, p1 = items_list[i]
            t2, p2 = items_list[j]
            dist = np.sqrt((p1[0] - p2[0]) ** 2 + (p1[1] - p2[1]) ** 2)
            key = (t1, t2) if (t1, t2) in SPACING_REQUIREMENTS else (t2, t1)
            req = SPACING_REQUIREMENTS.get(key, 0.0)
            ok = dist >= req
            if not ok:
                violations += 1
            details.append({
                "pair": f"{t1}-{t2}",
                "distance": round(dist, 1),
                "required": req,
                "ok": ok,
            })
    return violations, details


def run_single(n_candidates, mask, spawns, chokes, covers,
               p=2, optimize_iters=25, shots=500, shots_per_eval=150, seed=42):
    """Run one scaling data point. Returns result dict or error."""
    n_items = 3
    n_qubits = n_items * n_candidates
    mem_gb = (2 ** n_qubits * 16) / (1024 ** 3)

    print(f"\n  [{n_candidates} candidates, {n_qubits} qubits, ~{mem_gb:.1f} GB state vector]")

    # Build encoder with exact candidate count
    encoder_full = MapGenQUBOEncoder(
        walkable_mask=mask,
        spawn_points=spawns,
        choke_points=chokes,
        cover_positions=covers,
        item_rules=EXP1_ITEM_RULES,
        candidate_stride=8,
    )

    if encoder_full.n_candidates > n_candidates:
        encoder = encoder_full.create_subproblem(
            list(EXP1_ITEM_RULES.keys()),
            max_candidates=n_candidates,
        )
    else:
        encoder = encoder_full

    actual_cands = encoder.n_candidates
    actual_qubits = encoder.n_vars
    print(f"  Actual: {actual_cands} candidates, {actual_qubits} qubits")

    # Encode QUBO
    Q = encoder.encode(penalty_one_hot=500.0)
    n_terms = len(Q)
    print(f"  QUBO: {n_terms} terms")

    # Run QAOA
    t0 = time.time()
    runner = QAOARunner(Q=Q, n_vars=encoder.n_vars, encoder=encoder, mode="local")

    result = runner.run(
        shots=shots, p=p, optimize_iters=optimize_iters,
        shots_per_eval=shots_per_eval, seed=seed, verbose=False,
    )
    elapsed = time.time() - t0

    # Analyze repaired placement
    placement = result.get("repaired_placement")
    repaired_energy = result.get("repaired_energy", float("inf"))
    n_violations = 0
    spacing_details = []
    sa_energy = {}

    if placement:
        n_violations, spacing_details = compute_spacing_violations(placement)
        sa_energy = encoder.compute_sa_energy(placement)

    feasibility = result.get("feasibility", {})
    feas_rate = feasibility.get("feasibility_rate", 0.0) if feasibility.get("checked") else 0.0

    data = {
        "n_candidates": actual_cands,
        "n_qubits": actual_qubits,
        "n_qubo_terms": n_terms,
        "mem_estimate_gb": round(mem_gb, 2),
        "best_raw_energy": round(result["best_energy"], 2),
        "repaired_energy": round(repaired_energy, 2),
        "sa_weighted_total": round(sa_energy.get("weighted_total", 0), 2),
        "spacing_violations": n_violations,
        "spacing_details": spacing_details,
        "feasibility_rate_top10": round(feas_rate, 3),
        "n_unique_solutions": result["n_unique_solutions"],
        "time_s": round(elapsed, 1),
        "placement": {k: [(round(p[0], 1), round(p[1], 1)) for p in v]
                      for k, v in (placement or {}).items()},
        "optimal_params": result["optimal_params"],
        "status": "ok",
    }

    # Print compact summary
    viol_str = f"{n_violations} violations" if n_violations > 0 else "ALL OK"
    print(f"  -> E={repaired_energy:.0f}, SA={sa_energy.get('weighted_total', 0):.0f}, "
          f"spacing: {viol_str}, feas={feas_rate:.0%}, {elapsed:.0f}s")

    return data


def main():
    parser = argparse.ArgumentParser(description="Exp 1 scaling study")
    parser.add_argument("--max-candidates", type=int, default=10,
                        help="Max candidates to test (default: 10)")
    parser.add_argument("--min-candidates", type=int, default=4,
                        help="Min candidates to test (default: 4)")
    parser.add_argument("--p", type=int, default=2, help="QAOA layers (default: 2)")
    parser.add_argument("--optimize-iters", type=int, default=25,
                        help="COBYLA iterations per run (default: 25)")
    parser.add_argument("--shots", type=int, default=500,
                        help="Final shots per run (default: 500)")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    print("=" * 70)
    print("  Experiment 1 -- Candidate Scaling Study")
    print("  (D-Wave LaunchPad preliminary data)")
    print("=" * 70)

    mask, spawns, chokes, covers = create_arena()

    candidate_counts = list(range(args.min_candidates, args.max_candidates + 1))
    results = []

    for n_cands in candidate_counts:
        n_qubits = 3 * n_cands
        mem_gb = (2 ** n_qubits * 16) / (1024 ** 3)

        # Safety check: skip if estimated memory > 20GB
        if mem_gb > 20.0:
            print(f"\n  [{n_cands} candidates, {n_qubits} qubits, ~{mem_gb:.0f} GB]")
            print(f"  -> SKIPPED (exceeds 20GB memory limit, needs SV1/D-Wave)")
            results.append({
                "n_candidates": n_cands,
                "n_qubits": n_qubits,
                "mem_estimate_gb": round(mem_gb, 2),
                "status": "skipped_memory",
            })
            continue

        try:
            data = run_single(
                n_cands, mask, spawns, chokes, covers,
                p=args.p, optimize_iters=args.optimize_iters,
                shots=args.shots, seed=args.seed,
            )
            results.append(data)
        except MemoryError:
            print(f"  -> MEMORY ERROR (out of RAM at {n_qubits} qubits)")
            results.append({
                "n_candidates": n_cands,
                "n_qubits": n_qubits,
                "status": "memory_error",
            })
            break
        except Exception as e:
            print(f"  -> ERROR: {e}")
            traceback.print_exc()
            results.append({
                "n_candidates": n_cands,
                "n_qubits": n_qubits,
                "status": f"error: {str(e)[:100]}",
            })

    # ── Summary table ──
    print(f"\n{'=' * 70}")
    print(f"  SCALING SUMMARY")
    print(f"{'=' * 70}")
    print(f"  {'Cands':>5} {'Qubits':>6} {'Mem':>7} {'Raw E':>9} {'Repair E':>9} "
          f"{'SA E':>9} {'Viol':>5} {'Feas%':>6} {'Time':>6}  Status")
    print(f"  {'-' * 68}")

    for r in results:
        if r["status"] == "ok":
            print(f"  {r['n_candidates']:>5} {r['n_qubits']:>6} "
                  f"{r['mem_estimate_gb']:>6.1f}G "
                  f"{r['best_raw_energy']:>9.0f} {r['repaired_energy']:>9.0f} "
                  f"{r['sa_weighted_total']:>9.0f} "
                  f"{r['spacing_violations']:>5} "
                  f"{r['feasibility_rate_top10']:>5.0%} "
                  f"{r['time_s']:>5.0f}s  OK")
        else:
            mem = r.get('mem_estimate_gb', '?')
            print(f"  {r['n_candidates']:>5} {r['n_qubits']:>6} "
                  f"{mem if isinstance(mem, str) else f'{mem:>6.0f}G':>7} "
                  f"{'':>9} {'':>9} {'':>9} {'':>5} {'':>6} {'':>6}  {r['status']}")

    # D-Wave projection
    print(f"\n  D-Wave Advantage projection:")
    print(f"  {'50':>5} {'150':>6} {'--':>7}  (realistic problem: 25 items x 50 candidates)")
    print(f"  {'150':>5} {'450':>6} {'--':>7}  (full resolution: 25 items x 150 candidates)")
    print(f"  {'300':>5} {'900':>6} {'--':>7}  (max fidelity: beyond any simulator)")
    print(f"  D-Wave Advantage has 5,000+ qubits -- all of these fit.\n")

    # ── Save results ──
    out_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "quantum_mapgen", "exp1_scaling_results.json"
    )
    with open(out_path, "w") as f:
        json.dump({
            "experiment": "exp1_candidate_scaling",
            "date": time.strftime("%Y-%m-%d %H:%M:%S"),
            "config": {
                "p": args.p,
                "optimize_iters": args.optimize_iters,
                "shots": args.shots,
                "seed": args.seed,
                "one_hot_penalty": 500.0,
                "item_rules": list(EXP1_ITEM_RULES.keys()),
            },
            "results": results,
        }, f, indent=2)
    print(f"  Results saved to: {out_path}")
    print(f"{'=' * 70}\n")


if __name__ == "__main__":
    main()
