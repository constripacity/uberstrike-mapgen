"""Map quality analysis utilities for DesktopAgent v2.0."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Tuple

import networkx as nx
import numpy as np
from PIL import Image
from skimage.morphology import distance_transform_edt
import yaml


@dataclass
class AnalysisMetrics:
    spawn_balance: float
    path_diversity: float
    verticality: float
    cover_density: float
    sightline: float

    def aggregate_score(self) -> float:
        weights = np.array([0.25, 0.25, 0.2, 0.15, 0.15])
        values = np.array([
            self.spawn_balance,
            self.path_diversity,
            self.verticality,
            self.cover_density,
            self.sightline,
        ])
        return float(np.clip(np.dot(weights, values), 0.0, 1.0) * 100.0)


class MapQualityAnalyzer:
    """Provides gameplay-centric analysis for generated maps."""

    def __init__(self, config_path: Path | None = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        self.config = self._load_config(self.config_path)
        self.stacks_path = Path(self.config["unity"]["stacks_path"])

    # ------------------------------------------------------------------
    def analyze_map(self, stack_name: str) -> Dict[str, object]:
        stack_path = Path(stack_name)
        if not stack_path.is_absolute():
            stack_path = self.stacks_path / f"{stack_name}.stack.json"
        if not stack_path.exists():
            raise FileNotFoundError(stack_path)
        base = stack_path.with_suffix("")

        layout = Image.open(f"{base}.layout.png").convert("RGB")
        height = Image.open(f"{base}.height.png").convert("L")
        flow = Image.open(f"{base}.flow.png").convert("RGB")

        metrics = self._compute_metrics(layout, height, flow)
        heatmaps = self._generate_heatmaps(layout, metrics, base.name)

        analysis = {
            "spawn_balance": metrics.spawn_balance,
            "path_diversity": metrics.path_diversity,
            "verticality": metrics.verticality,
            "cover_density": metrics.cover_density,
            "sightline": metrics.sightline,
        }
        score = metrics.aggregate_score()
        recommendations = self._generate_recommendations(metrics)

        report = {
            "map": base.name,
            "score": round(score, 1),
            "analysis": analysis,
            "heatmaps": heatmaps,
            "recommendations": recommendations,
        }

        analysis_json = self.stacks_path / f"{base.name}_analysis.json"
        analysis_json.write_text(json.dumps(report, indent=2), encoding="utf-8")
        return report

    # ------------------------------------------------------------------
    def _compute_metrics(self, layout: Image.Image, height: Image.Image, flow: Image.Image) -> AnalysisMetrics:
        layout_np = np.array(layout)
        walkable = np.logical_not(np.all(layout_np == [0, 0, 0], axis=-1))
        height_np = np.array(height, dtype=np.float32) / 255.0
        flow_np = np.array(flow)

        spawn_mask = np.logical_or.reduce(
            [
                np.all(flow_np == [255, 0, 0], axis=-1),
                np.all(flow_np == [0, 255, 0], axis=-1),
                np.all(flow_np == [255, 255, 0], axis=-1),
            ]
        )
        spawn_indices = np.argwhere(spawn_mask)
        center = np.array([layout_np.shape[0] / 2, layout_np.shape[1] / 2])
        if spawn_indices.size == 0:
            spawn_balance = 0.0
        else:
            distances = np.linalg.norm(spawn_indices - center, axis=1)
            spawn_balance = float(1.0 - np.std(distances) / (np.max(distances) + 1e-5))

        # Build navigation graph for path diversity
        graph = nx.grid_2d_graph(layout_np.shape[0], layout_np.shape[1])
        blocked = np.argwhere(np.logical_not(walkable))
        graph.remove_nodes_from([tuple(node) for node in blocked])
        path_scores = []
        spawn_list = [tuple(coord) for coord in spawn_indices.tolist()]
        for i in range(len(spawn_list)):
            for j in range(i + 1, len(spawn_list)):
                try:
                    length = nx.shortest_path_length(graph, spawn_list[i], spawn_list[j])
                    path_scores.append(length)
                except (nx.NetworkXNoPath, nx.NodeNotFound):
                    continue
        path_diversity = float(np.clip(np.mean(path_scores) / 100.0 if path_scores else 0.0, 0.0, 1.0))

        verticality = float(np.clip(np.std(height_np) * 2.5, 0.0, 1.0))

        cover_mask = np.all(flow_np == [128, 128, 128], axis=-1)
        cover_density = float(np.clip(np.mean(cover_mask) * 4.0, 0.0, 1.0))

        distance_map = distance_transform_edt(walkable)
        sightline = float(np.clip(np.mean(distance_map) / 50.0, 0.0, 1.0))

        return AnalysisMetrics(
            spawn_balance=spawn_balance,
            path_diversity=path_diversity,
            verticality=verticality,
            cover_density=cover_density,
            sightline=sightline,
        )

    def _generate_heatmaps(self, layout: Image.Image, metrics: AnalysisMetrics, map_name: str) -> Dict[str, str]:
        layout_np = np.array(layout)
        walkable = np.logical_not(np.all(layout_np == [0, 0, 0], axis=-1))
        size = layout_np.shape[0]

        traffic = distance_transform_edt(walkable)
        combat = np.flipud(traffic)
        camping = 1.0 / (traffic + 1.0)

        def to_image(array: np.ndarray) -> Image.Image:
            normalised = (array - array.min()) / (array.max() - array.min() + 1e-5)
            heat = (normalised * 255).astype(np.uint8)
            return Image.fromarray(heat, mode="L").convert("RGB")

        traffic_img = to_image(traffic)
        combat_img = to_image(combat)
        camping_img = to_image(camping)

        outputs = {}
        for name, img in {"traffic": traffic_img, "combat": combat_img, "camping": camping_img}.items():
            path = self.stacks_path / f"{map_name}_{name}.png"
            img.save(path)
            outputs[name] = str(path)
        return outputs

    def _generate_recommendations(self, metrics: AnalysisMetrics) -> List[str]:
        recs: List[str] = []
        if metrics.spawn_balance < 0.6:
            recs.append("Reposition spawns to equalise travel time to centre.")
        if metrics.path_diversity < 0.4:
            recs.append("Add additional corridors between key areas.")
        if metrics.verticality < 0.3:
            recs.append("Increase elevation variance with ramps or platforms.")
        if metrics.cover_density < 0.3:
            recs.append("Scatter more cover props in open zones.")
        if metrics.sightline > 0.8:
            recs.append("Break up long sightlines with obstacles or pillars.")
        return recs

    # ------------------------------------------------------------------
    @staticmethod
    def _load_config(path: Path) -> Dict[str, Dict[str, object]]:
        if not path.exists():
            return {
                "unity": {"stacks_path": str(Path.cwd() / "generated_stacks")},
            }
        with path.open("r", encoding="utf-8") as handle:
            return yaml.safe_load(handle)  # type: ignore[return-value]


__all__ = ["MapQualityAnalyzer"]
