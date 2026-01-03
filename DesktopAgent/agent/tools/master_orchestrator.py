import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional

import numpy as np

from .adaptive_lod_optimizer import AdaptiveLODOptimizer
from .graph_flow_analyzer import GraphFlowAnalyzer
from .simulated_annealing_placer import PlacementConstraints, SimulatedAnnealingPlacer
from .voronoi_theme_generator import VoronoiThemeGenerator
from .wave_function_collapse import WaveFunctionCollapse
from ..utils.seed import set_global_seed


@dataclass
class MapGenerationRequest:
    """Complete map generation request."""

    name: str
    style: str = "arena"
    size: str = "medium"
    player_count: int = 8
    theme_preset: str = "mixed"
    complexity: str = "medium"
    seed: Optional[int] = None


class UberStrikeMapFactory:
    """Master orchestrator for complete map generation."""

    def __init__(self, base_seed: Optional[int] = None):
        self.generation_stats: List[Dict[str, Any]] = []
        self.base_seed = base_seed

    def generate_complete_map(self, request: MapGenerationRequest, max_attempts: int = 3) -> Dict[str, Any]:
        attempt = 0
        seed = request.seed if request.seed is not None else self.base_seed
        start_time = time.time()
        last_quality = 0.0

        while attempt < max_attempts:
            active_seed = seed if seed is not None else int(time.time() * 1000) % 1_000_000
            set_global_seed(active_seed)

            print("\n" + "=" * 60)
            print(f"Generating Map: {request.name} (attempt {attempt + 1}/{max_attempts}, seed={active_seed})")
            print(f"Style: {request.style} | Size: {request.size} | Players: {request.player_count}")
            print("=" * 60 + "\n")

            layout = self._generate_layout(request, active_seed)
            themes = self._apply_themes(layout, request, active_seed)
            items = self._place_items(layout, request, active_seed)
            flow_metrics = self._analyze_flow(layout, items, request, active_seed)
            last_quality = self._validate_quality(flow_metrics)

            if last_quality >= 0.6:
                break

            print(f"Quality too low ({last_quality:.2f}); retrying with new seed.")
            seed = (active_seed or 0) + 1
            attempt += 1

        if attempt >= max_attempts and last_quality < 0.6:
            raise RuntimeError("Failed to reach quality threshold after maximum attempts.")

        optimized = self._optimize_geometry(layout, flow_metrics)
        final_map = self._assemble_final_map(layout, themes, items, flow_metrics, optimized)
        generation_time = time.time() - start_time
        final_map["metadata"] = {
            "name": request.name,
            "style": request.style,
            "size": request.size,
            "player_count": request.player_count,
            "generation_time": generation_time,
            "quality_score": last_quality,
            "seed": seed,
            "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        self.generation_stats.append(
            {"name": request.name, "time": generation_time, "quality": last_quality, "attempts": attempt + 1}
        )
        print("\n" + "=" * 60)
        print("✓ Map Generation Complete!")
        print(f"Time: {generation_time:.2f}s | Quality: {last_quality:.2%}")
        print("=" * 60 + "\n")
        return final_map

    def _generate_layout(self, request: MapGenerationRequest, seed: Optional[int]) -> np.ndarray:
        size_map = {"small": (32, 32), "medium": (64, 64), "large": (128, 128)}
        width, height = size_map.get(request.size, (64, 64))
        spawn_count = min(request.player_count, 4) if request.style == "arena" else 2
        wfc = WaveFunctionCollapse(width, height, seed)
        try:
            return wfc.generate_arena_layout(spawn_count=spawn_count, ensure_connected=True)
        except RuntimeError:
            return self._generate_simple_layout(width, height)

    def _apply_themes(self, layout: np.ndarray, request: MapGenerationRequest, seed: Optional[int]) -> np.ndarray:
        generator = VoronoiThemeGenerator()
        region_counts = {"simple": 3, "medium": 5, "complex": 8}
        num_regions = region_counts.get(request.complexity, 5)
        result = generator.generate(
            layout.shape[1], layout.shape[0], num_regions=num_regions, seed=seed
        )
        return result["array"]

    def _place_items(self, layout: np.ndarray, request: MapGenerationRequest, seed: Optional[int]) -> Dict[str, List[tuple]]:
        placer = SimulatedAnnealingPlacer(seed=seed)
        if request.style == "arena":
            items_to_place = {
                "weapon_sniper": 1,
                "weapon_rocket": 1,
                "weapon_shotgun": 2,
                "armor_heavy": 1,
                "armor_light": 3,
                "health_mega": 1,
                "health_small": 6,
            }
        elif request.style == "ctf":
            items_to_place = {
                "flag_red": 1,
                "flag_blue": 1,
                "weapon_rocket": 2,
                "armor_heavy": 2,
                "health_mega": 2,
            }
        else:
            items_to_place = {
                "weapon_sniper": 2,
                "weapon_rocket": 2,
                "weapon_shotgun": 3,
                "armor_heavy": 2,
                "health_mega": 2,
            }
        if request.size == "large":
            items_to_place = {k: v * 2 for k, v in items_to_place.items()}
        elif request.size == "small":
            items_to_place = {k: max(1, v // 2) for k, v in items_to_place.items()}
        walkable = (layout[:, :, 0] > 100) if layout.ndim == 3 else (layout > 0)
        constraints = PlacementConstraints(
            spawn_points=[(10, 10), (layout.shape[1] - 10, layout.shape[0] - 10)],
            walkable_areas=walkable,
            choke_points=[],
            cover_positions=[],
            existing_items={},
        )
        return placer.optimise(constraints, items_to_place)

    def _analyze_flow(self, layout: np.ndarray, items: Dict[str, List[tuple]], request: MapGenerationRequest, seed: Optional[int]) -> Any:
        analyzer = GraphFlowAnalyzer(seed=seed)
        layout_classified = np.zeros(layout.shape[:2])
        layout_classified[layout[..., 0] > 100] = 1
        layout_classified[layout[..., 0] < 50] = 2
        spawn_points = [(10, 10), (layout.shape[1] - 10, layout.shape[0] - 10)]
        return analyzer.analyze_map(layout_classified, spawn_points, items)

    def _validate_quality(self, flow_metrics: Any) -> float:
        quality = 1.0
        balance = getattr(flow_metrics, "spawn_balance", 0.0)
        if balance < 0.3:
            quality *= 1.0
        elif balance < 0.5:
            quality *= 0.8
        else:
            quality *= 0.5
        dead_zones = len(getattr(flow_metrics, "dead_zones", []))
        quality *= max(0.5, 1.0 - dead_zones / 100)
        choke_count = len(getattr(flow_metrics, "chokepoints", []))
        if 2 <= choke_count <= 5:
            quality *= 1.1
        openness = getattr(flow_metrics, "map_openness", 0.5)
        if 0.3 <= openness <= 0.7:
            quality *= 1.05
        return min(1.0, quality)

    def _optimize_geometry(self, layout: np.ndarray, flow_metrics: Any) -> Dict[str, Any]:
        optimizer = AdaptiveLODOptimizer()
        importance = optimizer.create_importance_map(
            layout.shape[1],
            layout.shape[0],
            [],
            getattr(flow_metrics, "chokepoints", []),
            {},
        )
        return {"importance_map": importance.tolist(), "lod_settings": {"levels": 4, "distances": [0, 20, 50, 100]}}

    def _assemble_final_map(
        self, layout: np.ndarray, themes: np.ndarray, items: Dict[str, List[tuple]], flow_metrics: Any, optimized: Dict
    ) -> Dict[str, Any]:
        return {
            "layout": layout.tolist(),
            "themes": themes.tolist(),
            "items": items,
            "flow_metrics": {
                "spawn_balance": getattr(flow_metrics, "spawn_balance", 0),
                "chokepoint_count": len(getattr(flow_metrics, "chokepoints", [])),
                "dead_zone_count": len(getattr(flow_metrics, "dead_zones", [])),
                "map_openness": getattr(flow_metrics, "map_openness", 0.5),
            },
            "optimization": optimized,
            "export_format": "uberstrike_mapgen_v1",
        }

    def _generate_simple_layout(self, width: int, height: int) -> np.ndarray:
        layout = np.ones((height, width, 3), dtype=np.uint8) * 128
        layout[0, :] = [0, 0, 0]
        layout[-1, :] = [0, 0, 0]
        layout[:, 0] = [0, 0, 0]
        layout[:, -1] = [0, 0, 0]
        return layout

    def generate_batch(self, requests: List[MapGenerationRequest]) -> List[Dict[str, Any]]:
        results = []
        for request in requests:
            result = self.generate_complete_map(request)
            results.append(result)
            self.save_map(result)
        self.print_statistics()
        return results

    def save_map(self, map_data: Dict[str, Any]) -> None:
        name = map_data["metadata"]["name"]
        output_dir = Path(f"Generated_Maps/{name}")
        output_dir.mkdir(parents=True, exist_ok=True)
        with open(output_dir / f"{name}.json", "w", encoding="utf-8") as handle:
            json.dump(map_data, handle, indent=2)
        from PIL import Image

        layout = np.array(map_data["layout"], dtype=np.uint8)
        img = Image.fromarray(layout, mode="RGB")
        img.save(output_dir / f"{name}_layout.png")
        print(f"Saved map to: {output_dir}")

    def print_statistics(self) -> None:
        if not self.generation_stats:
            return
        print("\n" + "=" * 60)
        print("GENERATION STATISTICS")
        print("=" * 60)
        total_time = sum(stat["time"] for stat in self.generation_stats)
        avg_time = total_time / len(self.generation_stats)
        avg_quality = sum(stat["quality"] for stat in self.generation_stats) / len(self.generation_stats)
        print(f"Maps Generated: {len(self.generation_stats)}")
        print(f"Total Time: {total_time:.2f}s")
        print(f"Average Time: {avg_time:.2f}s")
        print(f"Average Quality: {avg_quality:.2%}")
        print("\nPer-Map Stats:")
        for stat in self.generation_stats:
            print(f"  {stat['name']}: {stat['time']:.2f}s, quality: {stat['quality']:.2%}")


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="Master map orchestrator")
    parser.add_argument("--name", required=True, help="Map name")
    parser.add_argument("--style", default="arena", choices=["arena", "ctf", "deathmatch"])
    parser.add_argument("--size", default="medium", choices=["small", "medium", "large"])
    parser.add_argument("--players", type=int, default=8, help="Player count")
    parser.add_argument("--complexity", default="medium", choices=["simple", "medium", "complex"])
    parser.add_argument("--seed", type=int, help="Random seed")
    parser.add_argument("--batch", type=int, help="Generate multiple maps")
    args = parser.parse_args()
    factory = UberStrikeMapFactory()
    if args.batch:
        requests = [
            MapGenerationRequest(
                name=f"{args.name}_{i:03d}",
                style=args.style,
                size=args.size,
                player_count=args.players,
                complexity=args.complexity,
                seed=(args.seed or 0) + i,
            )
            for i in range(args.batch)
        ]
        factory.generate_batch(requests)
    else:
        request = MapGenerationRequest(
            name=args.name,
            style=args.style,
            size=args.size,
            player_count=args.players,
            complexity=args.complexity,
            seed=args.seed,
        )
        result = factory.generate_complete_map(request)
        factory.save_map(result)


if __name__ == "__main__":
    main()
