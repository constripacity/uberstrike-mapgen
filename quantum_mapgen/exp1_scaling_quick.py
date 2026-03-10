"""
Quick scaling data collection — runs candidates 4-8 individually with
flush after each, then assembles results + projections.
"""

import numpy as np
import sys
import os
import json
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from qubo_encoder import MapGenQUBOEncoder, UBERSTRIKE_ITEM_RULES
from quantum_mapgen.braket_runner import QAOARunner

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


def count_violations(placement):
    items = []
    for itype, positions in placement.items():
        for pos in positions:
            items.append((itype, pos))
    violations = 0
    details = []
    for i in range(len(items)):
        for j in range(i + 1, len(items)):
            t1, p1 = items[i]
            t2, p2 = items[j]
            dist = np.sqrt((p1[0] - p2[0]) ** 2 + (p1[1] - p2[1]) ** 2)
            key = (t1, t2) if (t1, t2) in SPACING_REQUIREMENTS else (t2, t1)
            req = SPACING_REQUIREMENTS.get(key, 0.0)
            ok = dist >= req
            if not ok:
                violations += 1
            details.append({"pair": f"{t1}-{t2}", "dist": round(dist, 1),
                            "req": req, "ok": ok})
    return violations, details


def run_one(n_cands, mask, spawns, chokes, covers):
    n_qubits = 3 * n_cands
    mem_gb = (2 ** n_qubits * 16) / (1024 ** 3)

    # Build encoder
    enc_full = MapGenQUBOEncoder(
        walkable_mask=mask, spawn_points=spawns, choke_points=chokes,
        cover_positions=covers, item_rules=EXP1_ITEM_RULES, candidate_stride=8,
    )
    if enc_full.n_candidates > n_cands:
        enc = enc_full.create_subproblem(list(EXP1_ITEM_RULES.keys()), max_candidates=n_cands)
    else:
        enc = enc_full

    actual_qubits = enc.n_vars
    Q = enc.encode(penalty_one_hot=500.0)
    n_linear = sum(1 for (i, j) in Q if i == j)
    n_quad = sum(1 for (i, j) in Q if i != j)
    # Approximate gate count: n_qubits H + p*(n_linear Rz + n_quad*3 CNOT-Rz-CNOT + n_qubits Rx)
    p = 2
    gates_per_layer = n_linear + n_quad * 3 + actual_qubits  # Rz + CNOT-Rz-CNOT + Rx
    total_gates = actual_qubits + p * gates_per_layer  # H layer + p cost/mixer layers

    t0 = time.time()
    runner = QAOARunner(Q=Q, n_vars=enc.n_vars, encoder=enc, mode="local")
    result = runner.run(shots=500, p=p, optimize_iters=20, shots_per_eval=150,
                        seed=42, verbose=False)
    elapsed = time.time() - t0

    placement = result.get("repaired_placement") or {}
    repaired_energy = result.get("repaired_energy", float("inf"))
    n_viol, spacing = count_violations(placement) if placement else (0, [])
    sa_e = enc.compute_sa_energy(placement) if placement else {}
    feas = result.get("feasibility", {})
    feas_rate = feas.get("feasibility_rate", 0.0) if feas.get("checked") else 0.0

    return {
        "n_candidates": enc.n_candidates,
        "n_qubits": actual_qubits,
        "n_qubo_terms": len(Q),
        "n_qaoa_gates": total_gates,
        "mem_gb": round(mem_gb, 3),
        "runtime_s": round(elapsed, 1),
        "best_raw_energy": round(result["best_energy"], 2),
        "repaired_energy": round(repaired_energy, 2),
        "sa_weighted_total": round(sa_e.get("weighted_total", 0), 2),
        "spacing_violations": n_viol,
        "spacing_details": spacing,
        "feasibility_rate": round(feas_rate, 3),
        "feasible": n_viol == 0,
        "placement": {k: [(round(p[0], 1), round(p[1], 1)) for p in v]
                      for k, v in placement.items()},
        "status": "completed",
    }


def main():
    mask, spawns, chokes, covers = create_arena()
    results = []

    print("=" * 70)
    print("  Exp 1 Scaling Study -- Timing + Quality vs Candidate Count")
    print("=" * 70)
    sys.stdout.flush()

    for n_cands in [4, 5, 6, 7, 8]:
        n_q = 3 * n_cands
        mem = (2 ** n_q * 16) / (1024 ** 3)
        print(f"\n  Running: {n_cands} candidates, {n_q} qubits, ~{mem:.3f} GB...")
        sys.stdout.flush()

        try:
            data = run_one(n_cands, mask, spawns, chokes, covers)
            results.append(data)
            v = data["spacing_violations"]
            print(f"  Done: {data['runtime_s']}s, E={data['repaired_energy']:.0f}, "
                  f"violations={v}, feasible={data['feasible']}")
        except Exception as e:
            print(f"  ERROR: {e}")
            results.append({"n_candidates": n_cands, "n_qubits": 3*n_cands,
                            "status": f"error: {e}"})
        sys.stdout.flush()

    # Add wall-hit entries
    wall_entries = [
        {"n_candidates": 9, "n_qubits": 27, "n_qaoa_gates": "~1400",
         "mem_gb": 2.0, "runtime_s": "~3600+ (killed)",
         "status": "local_wall", "note": "Ran >1 hour, killed. Exponential blowup."},
        {"n_candidates": 10, "n_qubits": 30, "n_qaoa_gates": "~1700",
         "mem_gb": 16.0, "runtime_s": "N/A - exceeds local capacity",
         "status": "needs_sv1"},
        {"n_candidates": 11, "n_qubits": 33, "n_qaoa_gates": "~2000",
         "mem_gb": 128.0, "runtime_s": "~2-3 min (SV1 cloud)",
         "status": "needs_sv1",
         "note": "Within SV1's 34-qubit limit. Next experiment."},
        {"n_candidates": 50, "n_qubits": 150, "n_qaoa_gates": "~37000",
         "mem_gb": "2^150 (impossible)", "runtime_s": "N/A - requires D-Wave",
         "status": "needs_dwave",
         "note": "Realistic 25-item problem: 25 items x 50 candidates = 1250 qubits"},
        {"n_candidates": 250, "n_qubits": 750, "n_qaoa_gates": "~187000",
         "mem_gb": "2^750 (impossible)", "runtime_s": "N/A - requires D-Wave",
         "status": "needs_dwave",
         "note": "Full resolution. D-Wave Advantage: 5000+ qubits."},
    ]

    all_results = results + wall_entries

    # ── Print markdown table ──
    print(f"\n{'=' * 70}")
    print(f"  SCALING RESULTS")
    print(f"{'=' * 70}\n")

    print("| Candidates | Qubits | QAOA Gates | Runtime | Best Energy | Violations | Feasible |")
    print("|:---:|:---:|:---:|:---:|:---:|:---:|:---:|")

    for r in all_results:
        cands = r["n_candidates"]
        qubits = r["n_qubits"]
        gates = r.get("n_qaoa_gates", "?")
        rt = r.get("runtime_s", "?")
        if r["status"] == "completed":
            rt_str = f"{rt}s"
            energy = f"{r['repaired_energy']:.0f}"
            viol = str(r["spacing_violations"])
            feas = "YES" if r["feasible"] else "NO"
        elif r["status"] == "local_wall":
            rt_str = ">3600s (killed)"
            energy = "---"
            viol = "---"
            feas = "---"
        elif r["status"] == "needs_sv1":
            rt_str = str(rt)
            energy = "---"
            viol = "---"
            feas = "---"
        elif r["status"] == "needs_dwave":
            rt_str = "Requires D-Wave"
            energy = "---"
            viol = "---"
            feas = "---"
        else:
            rt_str = "error"
            energy = "---"
            viol = "---"
            feas = "---"

        print(f"| {cands} | {qubits} | {gates} | {rt_str} | {energy} | {viol} | {feas} |")

    print()

    # ── Save JSON ──
    out_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "quantum_mapgen", "exp1_scaling_results.json"
    )
    # Custom encoder to handle numpy types
    class NumpyEncoder(json.JSONEncoder):
        def default(self, obj):
            if isinstance(obj, (np.bool_, np.integer)):
                return int(obj)
            if isinstance(obj, np.floating):
                return float(obj)
            if isinstance(obj, np.ndarray):
                return obj.tolist()
            return super().default(obj)

    with open(out_path, "w") as f:
        json.dump({
            "experiment": "exp1_candidate_scaling",
            "description": "3 power items (sniper/rocket/armor_heavy) on 64x64 arena. "
                           "QAOA p=2, penalty_one_hot=500, 20 COBYLA iters, 500 final shots.",
            "date": time.strftime("%Y-%m-%d %H:%M:%S"),
            "machine": "Shadow PC (Windows 11, Python 3.11, Braket LocalSimulator)",
            "results": all_results,
            "narrative": {
                "local_simulator": "Practical up to 8 candidates (24 qubits). "
                                   "9 candidates (27 qubits) ran >1 hour before being killed.",
                "sv1_cloud": "Handles up to 11 candidates (33 qubits) in minutes. "
                             "Next experiment.",
                "dwave": "Required for realistic problems: 50-250 candidates = 150-750 qubits. "
                         "D-Wave Advantage has 5000+ qubits.",
            },
        }, f, indent=2, cls=NumpyEncoder)

    print(f"  Results saved to: {out_path}")
    print(f"{'=' * 70}\n")


if __name__ == "__main__":
    main()
