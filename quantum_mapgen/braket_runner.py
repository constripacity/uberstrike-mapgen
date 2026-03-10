"""
braket_runner.py — QAOA solver for MapGen QUBO on Amazon Braket

Takes a QUBO matrix from qubo_encoder.py, converts to a QAOA circuit,
and runs it on either LocalSimulator (free) or SV1 (cloud).

Modes:
    --local   Braket LocalSimulator, free, practical limit ~25 qubits
    --cloud   Amazon Braket SV1 (34 qubits max), uses free-tier minutes

Usage from experiment scripts:
    from quantum_mapgen.braket_runner import QAOARunner
    runner = QAOARunner(Q, n_vars, encoder=encoder, mode="local")
    result = runner.run(shots=1000, p=1)

Author: Culo / Quantum MapGen Project
"""

import numpy as np
from typing import Dict, Tuple, List, Optional, Any
from collections import defaultdict
import time
import sys
import json
import os

from braket.circuits import Circuit
from braket.devices import LocalSimulator


# ─────────────────────────────────────────────────────────
# SV1 Usage Ledger
# ─────────────────────────────────────────────────────────

_USAGE_LOG_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "sv1_usage_log.json"
)


def _load_usage_log() -> dict:
    """Load the SV1 usage log from disk."""
    if os.path.exists(_USAGE_LOG_PATH):
        with open(_USAGE_LOG_PATH) as f:
            return json.load(f)
    return {"runs": [], "total_minutes": 0.0}


def _save_usage_entry(experiment: str, minutes: float, details: dict = None):
    """Append an entry to the SV1 usage log and print cumulative total."""
    log = _load_usage_log()
    entry = {
        "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        "experiment": experiment,
        "minutes": round(minutes, 4),
        "details": details or {},
    }
    log["runs"].append(entry)
    log["total_minutes"] = round(sum(r["minutes"] for r in log["runs"]), 4)

    with open(_USAGE_LOG_PATH, "w") as f:
        json.dump(log, f, indent=2)

    print(f"\n  [SV1 USAGE] This run: {minutes:.2f} min | "
          f"Total this month: {log['total_minutes']:.2f} / 60.00 min")
    return log["total_minutes"]


def get_sv1_usage() -> dict:
    """Get current SV1 usage summary."""
    log = _load_usage_log()
    return {
        "total_minutes": log.get("total_minutes", 0.0),
        "remaining_minutes": 60.0 - log.get("total_minutes", 0.0),
        "n_runs": len(log.get("runs", [])),
    }


# ─────────────────────────────────────────────────────────
# QUBO ↔ Ising conversion
# ─────────────────────────────────────────────────────────

def qubo_to_ising(
    Q: Dict[Tuple[int, int], float], n_vars: int
) -> Tuple[np.ndarray, Dict[Tuple[int, int], float], float]:
    """Convert QUBO to Ising model.

    QUBO: E = Σ Q_{ij} x_i x_j   where x ∈ {0, 1}
    Ising: E = Σ h_i z_i + Σ J_{ij} z_i z_j + offset   where z ∈ {-1, +1}

    Substitution: x_i = (1 - z_i) / 2

    Returns (h, J, offset).
    """
    h = np.zeros(n_vars)
    J: Dict[Tuple[int, int], float] = {}
    offset = 0.0

    for (i, j), w in Q.items():
        if i == j:
            # Q_{ii} * x_i  = Q_{ii}/2 - (Q_{ii}/2) * z_i
            h[i] -= w / 2.0
            offset += w / 2.0
        else:
            # Q_{ij} * x_i * x_j = Q_{ij}/4 * (1 - z_i - z_j + z_i z_j)
            key = (min(i, j), max(i, j))
            J[key] = J.get(key, 0.0) + w / 4.0
            h[i] -= w / 4.0
            h[j] -= w / 4.0
            offset += w / 4.0

    return h, J, offset


def ising_to_qubo_bitstring(z_bitstring: str) -> List[int]:
    """Convert Braket measurement string ('0'/'1') to QUBO x-values.

    Braket measures in computational basis: |0⟩ -> +1, |1⟩ -> -1 in Z basis.
    So x_i = (1 - z_i) / 2  where z_i = +1 if measured '0', -1 if measured '1'.
    Therefore: measured '0' -> x=0, measured '1' -> x=1.

    Wait — that's the standard convention:
    z = 1 - 2x  ->  x = (1-z)/2.
    |0⟩ eigenvalue z=+1 -> x=0.  |1⟩ eigenvalue z=-1 -> x=1.
    """
    return [int(b) for b in z_bitstring]


# ─────────────────────────────────────────────────────────
# QAOA circuit construction
# ─────────────────────────────────────────────────────────

def build_qaoa_circuit(
    h: np.ndarray,
    J: Dict[Tuple[int, int], float],
    gammas: List[float],
    betas: List[float],
    n_qubits: int,
) -> Circuit:
    """Build a p-layer QAOA circuit for the Ising Hamiltonian.

    Cost unitary:  exp(-i γ H_C) where H_C = Σ h_i Z_i + Σ J_ij Z_i Z_j
    Mixer unitary: exp(-i β H_M) where H_M = Σ X_i

    Gate decompositions:
        exp(-i γ h_i Z_i)       = Rz(2 γ h_i)
        exp(-i γ J_ij Z_i Z_j)  = CNOT(i,j) · Rz(2 γ J_ij, j) · CNOT(i,j)
        exp(-i β X_i)           = Rx(2 β)
    """
    p = len(gammas)
    circ = Circuit()

    # Initial equal superposition
    for q in range(n_qubits):
        circ.h(q)

    # Precompute non-zero terms for efficiency
    linear_terms = [(i, h[i]) for i in range(n_qubits) if abs(h[i]) > 1e-12]
    quad_terms = [((i, j), w) for (i, j), w in J.items() if abs(w) > 1e-12]

    for layer in range(p):
        gamma = gammas[layer]
        beta = betas[layer]

        # ── Cost unitary ──
        # Single-qubit Z rotations (linear Ising terms)
        for i, hi in linear_terms:
            circ.rz(i, 2.0 * gamma * hi)

        # Two-qubit ZZ interactions (quadratic Ising terms)
        for (i, j), jij in quad_terms:
            circ.cnot(i, j)
            circ.rz(j, 2.0 * gamma * jij)
            circ.cnot(i, j)

        # ── Mixer unitary ──
        for q in range(n_qubits):
            circ.rx(q, 2.0 * beta)

    return circ


# ─────────────────────────────────────────────────────────
# QAOA Runner
# ─────────────────────────────────────────────────────────

class QAOARunner:
    """Runs QAOA on a QUBO problem via Amazon Braket.

    Args:
        Q: QUBO matrix dict {(i,j): weight}
        n_vars: number of binary variables (= number of qubits)
        encoder: optional MapGenQUBOEncoder for decoding solutions
        mode: "local" (LocalSimulator) or "cloud" (SV1)
        s3_bucket: S3 bucket for SV1 results (cloud mode only)
        s3_prefix: S3 key prefix for results
    """

    def __init__(
        self,
        Q: Dict[Tuple[int, int], float],
        n_vars: int,
        encoder=None,
        mode: str = "local",
        s3_bucket: str = None,
        s3_prefix: str = "qaoa-results",
    ):
        self.Q = Q
        self.n_vars = n_vars
        self.encoder = encoder
        self.mode = mode
        self.s3_bucket = s3_bucket or os.environ.get("BRAKET_S3_BUCKET")
        if self.mode == "cloud" and not self.s3_bucket:
            raise ValueError(
                "S3 bucket required for cloud mode. Set BRAKET_S3_BUCKET env var "
                "or pass s3_bucket parameter."
            )
        self.s3_prefix = s3_prefix

        # Convert QUBO -> Ising
        self.h, self.J, self.offset = qubo_to_ising(Q, n_vars)

        # Ising stats
        self.n_linear = sum(1 for x in self.h if abs(x) > 1e-12)
        self.n_quadratic = len([w for w in self.J.values() if abs(w) > 1e-12])

        # Set up device
        if mode == "local":
            self.device = LocalSimulator()
        elif mode == "cloud":
            from braket.aws import AwsDevice
            self.device = AwsDevice(
                "arn:aws:braket:::device/quantum-simulator/amazon/sv1"
            )
        else:
            raise ValueError(f"Unknown mode: {mode!r}. Use 'local' or 'cloud'.")

        # Cost tracking
        self._tracker = None
        self._total_shots = 0
        self._total_tasks = 0

    # ─────────────────────────────────────────────────
    # Energy computation
    # ─────────────────────────────────────────────────

    def qubo_energy(self, x: List[int]) -> float:
        """Compute QUBO energy for a binary assignment vector."""
        energy = 0.0
        for (i, j), w in self.Q.items():
            energy += w * x[i] * x[j]
        return energy

    # ─────────────────────────────────────────────────
    # Circuit execution
    # ─────────────────────────────────────────────────

    def _run_circuit(self, circuit: Circuit, shots: int):
        """Submit circuit to the configured device and return result."""
        if self.mode == "cloud":
            task = self.device.run(
                circuit,
                s3_destination_folder=(
                    self.s3_bucket,
                    self.s3_prefix,
                ),
                shots=shots,
            )
            result = task.result()
        else:
            result = self.device.run(circuit, shots=shots).result()

        self._total_shots += shots
        self._total_tasks += 1
        return result

    # ─────────────────────────────────────────────────
    # Parameter optimization
    # ─────────────────────────────────────────────────

    def _evaluate(self, params: np.ndarray, p: int, shots_per_eval: int) -> float:
        """Run QAOA circuit at given parameters, return expected QUBO energy."""
        gammas = list(params[:p])
        betas = list(params[p:])

        circuit = build_qaoa_circuit(self.h, self.J, gammas, betas, self.n_vars)
        result = self._run_circuit(circuit, shots_per_eval)

        counts = result.measurement_counts
        total = sum(counts.values())
        expected = 0.0
        for bitstring, count in counts.items():
            x = ising_to_qubo_bitstring(bitstring)
            expected += self.qubo_energy(x) * count / total
        return expected

    # ─────────────────────────────────────────────────
    # Main entry point
    # ─────────────────────────────────────────────────

    def run(
        self,
        shots: int = 1000,
        p: int = 1,
        optimize_iters: int = 30,
        shots_per_eval: int = 200,
        seed: int = 42,
        verbose: bool = True,
    ) -> Dict[str, Any]:
        """Run QAOA optimization and collect results.

        1. Optimize γ, β parameters via COBYLA (minimize expected energy).
        2. Run final circuit with best parameters at full shot count.
        3. Decode all unique solutions and rank by energy.

        Args:
            shots: shot count for final measurement
            p: number of QAOA layers (circuit depth)
            optimize_iters: max COBYLA iterations for parameter search
            shots_per_eval: shots per circuit during optimization
            seed: random seed for reproducibility
            verbose: print progress to stdout

        Returns:
            Dict with solutions, energies, timing, cost info.
        """
        from scipy.optimize import minimize as sp_minimize

        # Start cost tracker for cloud mode
        if self.mode == "cloud":
            try:
                from braket.tracking import Tracker
                self._tracker = Tracker().start()
            except ImportError:
                self._tracker = None

        t_start = time.time()

        if verbose:
            print(f"\n{'='*60}")
            print(f"  QAOA Runner -- {self.mode} mode")
            print(f"{'='*60}")
            print(f"  Qubits:          {self.n_vars}")
            print(f"  QUBO terms:      {len(self.Q)}")
            print(f"  Ising linear:    {self.n_linear}")
            print(f"  Ising quadratic: {self.n_quadratic}")
            print(f"  QAOA layers (p): {p}")
            print(f"  Optimize iters:  {optimize_iters}")
            print(f"  Shots/eval:      {shots_per_eval}")
            print(f"  Final shots:     {shots}")
            print()

        # ── Parameter optimization ──
        np.random.seed(seed)
        init_params = np.random.uniform(0.1, np.pi / 2, size=2 * p)

        best_energy = float("inf")
        best_params = init_params.copy()
        eval_count = [0]

        def objective(params):
            eval_count[0] += 1
            energy = self._evaluate(params, p, shots_per_eval)
            nonlocal best_energy, best_params
            if energy < best_energy:
                best_energy = energy
                best_params = params.copy()
            if verbose and eval_count[0] % 5 == 0:
                print(f"    eval {eval_count[0]:3d}: E={energy:10.2f}  (best={best_energy:.2f})")
            return energy

        if verbose:
            print("  Optimizing parameters...")

        opt_result = sp_minimize(
            objective,
            init_params,
            method="COBYLA",
            options={"maxiter": optimize_iters, "rhobeg": 0.5},
        )

        t_opt = time.time() - t_start
        if verbose:
            print(f"  Optimization: {eval_count[0]} evals in {t_opt:.1f}s")
            print(f"  Best energy found: {best_energy:.2f}")

        # ── Final measurement ──
        if verbose:
            print(f"\n  Running final circuit ({shots} shots)...")

        gammas = list(best_params[:p])
        betas = list(best_params[p:])
        final_circuit = build_qaoa_circuit(
            self.h, self.J, gammas, betas, self.n_vars
        )
        final_result = self._run_circuit(final_circuit, shots)

        # ── Decode all unique measurement outcomes ──
        counts = final_result.measurement_counts
        solutions = []
        for bitstring, count in counts.items():
            x = ising_to_qubo_bitstring(bitstring)
            energy = self.qubo_energy(x)
            solutions.append(
                {
                    "bitstring": bitstring,
                    "x": x,
                    "energy": energy,
                    "count": count,
                    "probability": count / shots,
                }
            )

        solutions.sort(key=lambda s: s["energy"])

        # ── Feasibility analysis ──
        feasibility = self._analyze_feasibility(solutions[:10])

        # ── Decode best solution to item placement ──
        placement = None
        placement_violations = []
        if self.encoder and solutions:
            placement, placement_violations = self.encoder.decode(solutions[0]["x"])

        # ── Repair top-K solutions (hybrid quantum-classical) ──
        repaired_placement = None
        repaired_energy = None
        repaired_violations = []
        if self.encoder and solutions:
            best_repair = self.repair_top_k(solutions, k=min(50, len(solutions)))
            if best_repair:
                repaired_energy = best_repair["energy"]
                repaired_placement = best_repair["placement"]
                if best_repair["x"]:
                    _, repaired_violations = self.encoder.decode(best_repair["x"])

        # ── Cost tracking ──
        cost_info = self._get_cost_info()

        # ── SV1 usage ledger (cloud mode only) ──
        if self.mode == "cloud":
            sv1_minutes = cost_info.get("sv1_estimated_minutes", 0.0)
            # Try to get actual time from Braket Tracker
            if self._tracker:
                try:
                    tracker_cost = self._tracker.simulator_tasks_cost()
                    # Braket tracker returns cost in USD; SV1 = $0.075/min
                    if tracker_cost and tracker_cost > 0:
                        sv1_minutes = tracker_cost / 0.075
                except Exception:
                    pass
            _save_usage_entry(
                experiment=f"qaoa_p{len(gammas)}_q{self.n_vars}",
                minutes=sv1_minutes,
                details={
                    "qubits": self.n_vars,
                    "total_shots": self._total_shots,
                    "total_tasks": self._total_tasks,
                },
            )

        t_total = time.time() - t_start

        result = {
            "solutions": solutions,
            "best_energy": solutions[0]["energy"] if solutions else float("inf"),
            "best_bitstring": solutions[0]["bitstring"] if solutions else "",
            "best_x": solutions[0]["x"] if solutions else [],
            "optimal_params": {"gammas": gammas, "betas": betas},
            "n_unique_solutions": len(solutions),
            "feasibility": feasibility,
            "placement": placement,
            "placement_violations": placement_violations,
            "repaired_placement": repaired_placement,
            "repaired_energy": repaired_energy,
            "repaired_violations": repaired_violations,
            "ising_offset": self.offset,
            "total_shots": self._total_shots,
            "total_tasks": self._total_tasks,
            "optimization_time_s": t_opt,
            "total_time_s": t_total,
            "cost": cost_info,
        }

        if verbose:
            self._print_summary(result)

        return result

    # ─────────────────────────────────────────────────
    # Feasibility checking
    # ─────────────────────────────────────────────────

    def _analyze_feasibility(self, top_solutions: List[Dict]) -> Dict[str, Any]:
        """Check one-hot constraint satisfaction for top solutions."""
        if not self.encoder or not top_solutions:
            return {"checked": False}

        n_items = self.encoder.n_items
        n_cands = self.encoder.n_candidates
        feasible_count = 0
        violation_details = []

        for sol in top_solutions:
            x = sol["x"]
            violations = 0
            for item_idx in range(n_items):
                assigned = sum(
                    x[self.encoder.var(item_idx, j)] for j in range(n_cands)
                )
                if assigned != 1:
                    violations += 1
            if violations == 0:
                feasible_count += 1
            violation_details.append(violations)

        return {
            "checked": True,
            "top_n": len(top_solutions),
            "feasible_count": feasible_count,
            "feasibility_rate": feasible_count / len(top_solutions),
            "one_hot_violations": violation_details,
        }

    # ─────────────────────────────────────────────────
    # Feasibility repair
    # ─────────────────────────────────────────────────

    def repair_solution(self, x: List[int]) -> List[int]:
        """Project an infeasible bitstring to the nearest feasible one.

        Greedy repair that respects exclusivity (no two items at same candidate):
          1. Score each (item, candidate) pair using the raw bitstring as a hint.
          2. For items with exactly 1 selection, lock them in first.
          3. For remaining items, greedily pick the best unused candidate,
             preferring candidates that were selected in the raw solution.

        Returns a new bitstring satisfying all one-hot constraints.
        """
        if not self.encoder:
            return x

        x_rep = [0] * len(x)
        n_cands = self.encoder.n_candidates
        used_candidates = set()

        # Phase 1: lock in items that already have exactly 1 selection
        locked = set()
        for item_idx in range(self.encoder.n_items):
            selected_cands = [
                j for j in range(n_cands)
                if x[self.encoder.var(item_idx, j)] == 1
            ]
            if len(selected_cands) == 1:
                cand = selected_cands[0]
                if cand not in used_candidates:
                    x_rep[self.encoder.var(item_idx, cand)] = 1
                    used_candidates.add(cand)
                    locked.add(item_idx)

        # Phase 2: greedily assign remaining items
        for item_idx in range(self.encoder.n_items):
            if item_idx in locked:
                continue

            # Score candidates: prefer those selected in raw solution,
            # break ties by diagonal QUBO cost
            selected_cands = [
                j for j in range(n_cands)
                if x[self.encoder.var(item_idx, j)] == 1
            ]

            available = [j for j in range(n_cands) if j not in used_candidates]
            if not available:
                # All candidates taken — pick any (shouldn't happen with
                # more candidates than items, but be safe)
                available = list(range(n_cands))

            # Prefer selected candidates, then sort by diagonal cost
            def score(j):
                was_selected = 1 if j in selected_cands else 0
                diag_cost = self.Q.get(
                    (self.encoder.var(item_idx, j), self.encoder.var(item_idx, j)), 0.0
                )
                return (-was_selected, diag_cost)  # prefer selected, then low cost

            best_cand = min(available, key=score)
            x_rep[self.encoder.var(item_idx, best_cand)] = 1
            used_candidates.add(best_cand)

        return x_rep

    def repair_top_k(self, solutions: List[Dict], k: int = 20) -> Dict[str, Any]:
        """Repair top-K solutions and return the best feasible one.

        This is the core hybrid quantum-classical strategy: QAOA provides
        hints about the energy landscape, classical repair projects to feasible.
        """
        if not self.encoder:
            return None

        best_energy = float("inf")
        best_x = None
        best_placement = None

        for sol in solutions[:k]:
            repaired_x = self.repair_solution(sol["x"])
            energy = self.qubo_energy(repaired_x)
            if energy < best_energy:
                best_energy = energy
                best_x = repaired_x
                best_placement, _ = self.encoder.decode(repaired_x)

        return {
            "x": best_x,
            "energy": best_energy,
            "placement": best_placement,
        }

    # ─────────────────────────────────────────────────
    # Cost tracking
    # ─────────────────────────────────────────────────

    def _get_cost_info(self) -> Dict[str, Any]:
        """Retrieve cost info from Braket Tracker (cloud mode)."""
        info = {
            "mode": self.mode,
            "total_shots": self._total_shots,
            "total_tasks": self._total_tasks,
        }

        if self._tracker:
            try:
                self._tracker.stop()
                summary = self._tracker.quantum_tasks_statistics()
                info["tracker_summary"] = str(summary)
                info["estimated_cost_usd"] = self._tracker.simulator_tasks_cost()
            except Exception as e:
                info["tracker_error"] = str(e)

        if self.mode == "cloud":
            # SV1 pricing: $0.075 per minute of simulation
            # Rough estimate based on qubit count and shots
            est_minutes = (self.n_vars / 20.0) * (self._total_shots / 1000.0) * 0.5
            info["sv1_estimated_minutes"] = round(est_minutes, 2)
            info["sv1_estimated_cost_usd"] = round(est_minutes * 0.075, 4)

        return info

    # ─────────────────────────────────────────────────
    # Pretty printing
    # ─────────────────────────────────────────────────

    def _print_summary(self, result: Dict[str, Any]):
        """Print a human-readable summary of QAOA results."""
        print(f"\n{'='*60}")
        print(f"  QAOA Results Summary")
        print(f"{'='*60}")

        print(f"\n  Best QUBO energy:    {result['best_energy']:.2f}")
        print(f"  Ising offset:        {result['ising_offset']:.2f}")
        print(f"  Unique solutions:    {result['n_unique_solutions']}")
        print(f"  Total shots:         {result['total_shots']}")
        print(f"  Total time:          {result['total_time_s']:.1f}s")

        # Feasibility
        feas = result["feasibility"]
        if feas.get("checked"):
            print(f"\n  Feasibility (top {feas['top_n']} solutions):")
            print(f"    Feasible:          {feas['feasible_count']}/{feas['top_n']}")
            print(f"    Rate:              {feas['feasibility_rate']:.0%}")
            print(f"    One-hot violations: {feas['one_hot_violations']}")

        # Top 5 solutions
        print(f"\n  Top 5 solutions by energy:")
        print(f"  {'Rank':<6} {'Energy':>10} {'Prob':>8} {'Count':>6}")
        print(f"  {'-'*32}")
        for i, sol in enumerate(result["solutions"][:5]):
            print(
                f"  {i+1:<6} {sol['energy']:10.2f} {sol['probability']:8.3f} {sol['count']:6d}"
            )

        # Optimal parameters
        params = result["optimal_params"]
        print(f"\n  Optimal QAOA parameters:")
        print(f"    gamma = {params['gammas']}")
        print(f"    beta  = {params['betas']}")

        # Raw placement (may be infeasible)
        if result["placement"]:
            print(f"\n  Raw placement (from best bitstring):")
            for item_type, positions in result["placement"].items():
                for pos in positions:
                    print(f"    {item_type:<15} -> ({pos[0]:.1f}, {pos[1]:.1f})")
            if result["placement_violations"]:
                print(f"  Violations:")
                for v in result["placement_violations"]:
                    print(f"    WARNING: {v}")

        # Repaired placement (always feasible)
        if result.get("repaired_placement"):
            print(f"\n  Repaired placement (projected to feasible):")
            print(f"    Repaired energy: {result['repaired_energy']:.2f}")
            for item_type, positions in result["repaired_placement"].items():
                for pos in positions:
                    print(f"    {item_type:<15} -> ({pos[0]:.1f}, {pos[1]:.1f})")
            if result.get("repaired_violations"):
                for v in result["repaired_violations"]:
                    print(f"    WARNING: {v}")

        # Cost info
        cost = result["cost"]
        if cost["mode"] == "cloud":
            print(f"\n  Cost tracking (SV1):")
            if "estimated_cost_usd" in cost:
                print(f"    Braket cost:  ${cost['estimated_cost_usd']:.4f}")
            print(f"    Est. minutes: {cost.get('sv1_estimated_minutes', '?')}")
            print(f"    Est. cost:    ${cost.get('sv1_estimated_cost_usd', '?')}")

        print(f"\n{'='*60}\n")
