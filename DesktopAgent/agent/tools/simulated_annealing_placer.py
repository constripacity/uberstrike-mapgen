"""Simulated annealing-based item placement for UberStrike maps.

The optimiser balances spawn fairness, risk/reward exposure, spacing,
flow adherence, and strategic depth. It operates on a binary walkable mask
plus optional choke/cover metadata and exports placements consumable by Unity.
"""
from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np

from ..utils.seed import set_global_seed


@dataclass
class PlacementConstraints:
    """Constraints and metadata required for placement optimisation."""

    spawn_points: List[Tuple[float, float]]
    walkable_areas: np.ndarray  # binary mask (H, W)
    choke_points: List[Tuple[float, float]]
    cover_positions: List[Tuple[float, float]]
    existing_items: Dict[str, List[Tuple[float, float]]]


class SimulatedAnnealingPlacer:
    """Optimises item placement using simulated annealing with heuristic scoring."""

    ITEM_RULES: Dict[str, Dict[str, object]] = {
        "weapon_sniper": {"count": 1, "min_spacing": 50.0, "prefer_exposed": True, "respawn_time": 60},
        "weapon_rocket": {"count": 1, "min_spacing": 40.0, "prefer_center": True, "prefer_exposed": True, "respawn_time": 45},
        "weapon_shotgun": {"count": 2, "min_spacing": 30.0, "prefer_enclosed": True, "prefer_cover": True, "respawn_time": 30},
        "armor_heavy": {"count": 1, "min_spacing": 35.0, "prefer_exposed": True, "prefer_center": True, "respawn_time": 45},
        "armor_light": {"count": 3, "min_spacing": 20.0, "prefer_cover": False, "respawn_time": 20},
        "health_mega": {"count": 1, "min_spacing": 40.0, "prefer_low_ground": True, "respawn_time": 35},
        "health_small": {"count": 6, "min_spacing": 15.0, "prefer_paths": True, "respawn_time": 15},
        "ammo_rockets": {"count": 4, "min_spacing": 10.0, "near_weapon": "weapon_rocket", "respawn_time": 20},
        "ammo_bullets": {"count": 6, "min_spacing": 8.0, "scatter": True, "respawn_time": 15},
    }

    def __init__(self, temperature: float = 1000.0, cooling_rate: float = 0.95, seed: Optional[int] = None):
        set_global_seed(seed)
        self.initial_temp = temperature
        self.cooling_rate = cooling_rate
        self.iteration_count = 0
        self.score_history: List[float] = []
        self.rng = np.random.default_rng(seed)

    def optimise(
        self,
        constraints: PlacementConstraints,
        items_to_place: Optional[Dict[str, int]] = None,
        max_iterations: int = 7500,
    ) -> Dict[str, List[Tuple[float, float]]]:
        """Run the annealer; returns a mapping item_type -> positions."""

        if items_to_place is None:
            items_to_place = {k: int(v.get("count", 1)) for k, v in self.ITEM_RULES.items()}

        if constraints.walkable_areas is None or not np.any(constraints.walkable_areas):
            raise ValueError("No walkable cells available for placement.")

        current = self._initialise(constraints, items_to_place)
        current_score = self._evaluate(current, constraints)

        best = {k: list(v) for k, v in current.items()}
        best_score = current_score

        temp = self.initial_temp
        for iteration in range(max_iterations):
            self.iteration_count = iteration
            neighbor = self._neighbor(current, constraints)
            neighbor_score = self._evaluate(neighbor, constraints)

            delta = neighbor_score - current_score
            accept = delta < 0 or self.rng.random() < np.exp(-delta / max(temp, 1e-3))
            if accept:
                current = neighbor
                current_score = neighbor_score
                if current_score < best_score:
                    best = {k: list(v) for k, v in current.items()}
                    best_score = current_score

            temp *= self.cooling_rate
            self.score_history.append(current_score)
            if temp < 0.05 and iteration > 500:
                break

        return best

    # ---- internal helpers -------------------------------------------------

    def _initialise(self, constraints: PlacementConstraints, items: Dict[str, int]) -> Dict[str, List[Tuple[float, float]]]:
        placement: Dict[str, List[Tuple[float, float]]] = {}
        walkable_points = self._walkable_points(constraints.walkable_areas)
        used: List[Tuple[float, float]] = []

        for item_type, count in items.items():
            rule = self.ITEM_RULES.get(item_type, {})
            min_spacing = float(rule.get("min_spacing", 10.0))
            positions: List[Tuple[float, float]] = []
            attempts = 0
            while len(positions) < count and attempts < 2000 and walkable_points:
                idx = int(self.rng.integers(0, len(walkable_points)))
                candidate = tuple(float(c) for c in walkable_points[idx])
                if self._ok_spacing(candidate, used, min_spacing):
                    positions.append(candidate)
                    used.append(candidate)
                attempts += 1
            placement[item_type] = positions
        return placement

    def _neighbor(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> Dict[str, List[Tuple[float, float]]]:
        neighbor = {k: list(v) for k, v in placement.items()}
        keys = [k for k, v in neighbor.items() if v]
        if not keys:
            return neighbor
        item_type = self.rng.choice(keys)
        item_positions = neighbor[item_type]
        idx = int(self.rng.integers(0, len(item_positions)))
        current_pos = item_positions[idx]

        candidates = self._walkable_points(constraints.walkable_areas)
        if len(candidates) == 0:
            return neighbor

        rule = self.ITEM_RULES.get(item_type, {})
        min_spacing = float(rule.get("min_spacing", 10.0))

        # limit candidate search nearby for local refinement
        nearby = [c for c in candidates if 0 < np.linalg.norm(np.array(c) - np.array(current_pos)) < 25.0]
        if not nearby:
            nearby = candidates
        new_pos = tuple(float(v) for v in nearby[int(self.rng.integers(0, len(nearby)))])

        # spacing check against everything except the moved item
        all_other = [p for k, vals in neighbor.items() for p in vals if not (k == item_type and p == current_pos)]
        if self._ok_spacing(new_pos, all_other, min_spacing):
            neighbor[item_type][idx] = new_pos
        return neighbor

    def _evaluate(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> float:
        score = 0.0
        score += self._spawn_balance(placement, constraints) * 10.0
        score += self._risk_reward(placement, constraints) * 5.0
        score += self._flow_alignment(placement, constraints) * 3.0
        score += self._spacing_penalty(placement) * 7.0
        score += self._strategic_depth(placement, constraints) * 4.0
        return score

    def _spawn_balance(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> float:
        if not constraints.spawn_points:
            return 0.0
        advantages: List[float] = []
        for spawn in constraints.spawn_points:
            total = 0.0
            for item, positions in placement.items():
                if not positions:
                    continue
                rule = self.ITEM_RULES.get(item, {})
                value = float(rule.get("respawn_time", 30.0)) / 10.0
                dists = [np.linalg.norm(np.array(spawn) - np.array(p)) for p in positions]
                min_dist = min(dists) if dists else 100.0
                total += value / (min_dist + 1.0)
            advantages.append(total)
        return float(np.std(advantages)) if advantages else 0.0

    def _risk_reward(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> float:
        score = 0.0
        for item, positions in placement.items():
            rule = self.ITEM_RULES.get(item, {})
            if not positions:
                continue
            if rule.get("prefer_exposed"):
                for pos in positions:
                    if constraints.cover_positions:
                        cover_dists = [np.linalg.norm(np.array(pos) - np.array(c)) for c in constraints.cover_positions]
                        if cover_dists:
                            min_cover = min(cover_dists)
                            if min_cover < 10.0:
                                score += (10.0 - min_cover) * 2.0
            if rule.get("prefer_cover"):
                for pos in positions:
                    if constraints.cover_positions:
                        cover_dists = [np.linalg.norm(np.array(pos) - np.array(c)) for c in constraints.cover_positions]
                        if cover_dists:
                            min_cover = min(cover_dists)
                            if min_cover > 15.0:
                                score += (min_cover - 15.0)
        return score

    def _flow_alignment(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> float:
        if not constraints.choke_points:
            return 0.0
        score = 0.0
        for positions in placement.values():
            for pos in positions:
                choke_dists = [np.linalg.norm(np.array(pos) - np.array(c)) for c in constraints.choke_points]
                if not choke_dists:
                    continue
                min_choke = min(choke_dists)
                if min_choke < 5.0:
                    score += (5.0 - min_choke) * 3.0
                elif min_choke > 15.0:
                    score += (min_choke - 15.0) * 0.5
        return score

    def _spacing_penalty(self, placement: Dict[str, List[Tuple[float, float]]]) -> float:
        score = 0.0
        all_positions: List[Tuple[str, Tuple[float, float]]] = []
        for item, positions in placement.items():
            rule = self.ITEM_RULES.get(item, {})
            min_spacing = float(rule.get("min_spacing", 10.0))
            for pos in positions:
                for other_item, other_pos in all_positions:
                    dist = np.linalg.norm(np.array(pos) - np.array(other_pos))
                    if dist < min_spacing:
                        score += (min_spacing - dist) * 5.0
                all_positions.append((item, pos))
        return score

    def _strategic_depth(self, placement: Dict[str, List[Tuple[float, float]]], constraints: PlacementConstraints) -> float:
        power_items = []
        for key in ("weapon_sniper", "weapon_rocket", "armor_heavy"):
            power_items.extend(placement.get(key, []))
        if not power_items:
            return 0.0
        center = np.mean(np.array(power_items), axis=0)
        h, w = constraints.walkable_areas.shape
        map_center = np.array([w * 0.5, h * 0.5])
        offset = np.linalg.norm(center - map_center)
        return max(0.0, offset - 30.0)

    # utility helpers -------------------------------------------------------

    @staticmethod
    def _walkable_points(mask: np.ndarray, sample_rate: int = 2) -> List[Tuple[float, float]]:
        ys, xs = np.nonzero(mask)
        points = list(zip(xs.astype(float), ys.astype(float)))
        if sample_rate > 1:
            points = points[::sample_rate]
        return points

    @staticmethod
    def _ok_spacing(point: Tuple[float, float], others: Sequence[Tuple[float, float]], min_spacing: float) -> bool:
        if not others:
            return True
        p = np.array(point)
        return all(np.linalg.norm(p - np.array(o)) >= min_spacing for o in others)

    # exports ---------------------------------------------------------------

    def export_to_json(self, placement: Dict[str, List[Tuple[float, float]]], output_path: str) -> None:
        data = {
            "version": "1.0",
            "algorithm": "simulated_annealing",
            "temperature": self.initial_temp,
            "cooling_rate": self.cooling_rate,
            "iterations": self.iteration_count,
            "final_score": self.score_history[-1] if self.score_history else 0,
            "items": {},
        }
        for item, positions in placement.items():
            data["items"][item] = [{"x": p[0], "y": 0, "z": p[1]} for p in positions]
        Path(output_path).write_text(json.dumps(data, indent=2))


# ------------------------- CLI --------------------------------------------

def _load_walkable_mask(path: Path) -> np.ndarray:
    from PIL import Image

    img = Image.open(path).convert("L")
    arr = np.array(img, dtype=np.uint8)
    return arr > 128


def _load_layout_points(mask: np.ndarray) -> PlacementConstraints:
    h, w = mask.shape
    spawn_points: List[Tuple[float, float]] = [(w * 0.25, h * 0.25), (w * 0.75, h * 0.75)]
    return PlacementConstraints(
        spawn_points=spawn_points,
        walkable_areas=mask,
        choke_points=[(w * 0.5, h * 0.4), (w * 0.5, h * 0.6)],
        cover_positions=[],
        existing_items={},
    )


def main() -> None:
    parser = argparse.ArgumentParser(description="Optimise item placement using simulated annealing")
    parser.add_argument("--map", required=True, help="Path to layout PNG (bright=walkable)")
    parser.add_argument("--output", default="placement.json", help="Output JSON path")
    parser.add_argument("--visualize", action="store_true", help="Show matplotlib plot")
    parser.add_argument("--temp", type=float, default=1000.0, help="Initial temperature")
    parser.add_argument("--cooling", type=float, default=0.95, help="Cooling rate")
    args = parser.parse_args()

    mask = _load_walkable_mask(Path(args.map))
    constraints = _load_layout_points(mask)
    optimiser = SimulatedAnnealingPlacer(args.temp, args.cooling)
    result = optimiser.optimise(constraints)
    optimiser.export_to_json(result, args.output)

    if args.visualize:
        try:
            import matplotlib.pyplot as plt

            plt.imshow(constraints.walkable_areas, cmap="gray", alpha=0.3)
            colors = plt.cm.tab10(np.linspace(0, 1, len(result)))
            for idx, (item, positions) in enumerate(result.items()):
                if positions:
                    xs = [p[0] for p in positions]
                    ys = [p[1] for p in positions]
                    plt.scatter(xs, ys, c=[colors[idx]], s=80, label=item)
            plt.legend()
            plt.title("Simulated Annealing Placement")
            plt.show()
        except Exception:
            print("Visualization skipped (matplotlib not available)")


if __name__ == "__main__":
    main()
