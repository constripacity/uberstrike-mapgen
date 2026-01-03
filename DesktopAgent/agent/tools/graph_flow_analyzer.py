"""Graph-based flow analysis for UberStrike map layouts.

This module builds a navigation graph from a 2D layout mask, extracts flow
metrics (chokepoints, dead zones, heat maps, spawn balance, camping spots,
exposure), and exports JSON or visualizations for downstream consumers.
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import networkx as nx
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

from ..utils.seed import set_global_seed

@dataclass
class FlowMetrics:
    chokepoints: List[Tuple[int, int]]
    dead_zones: List[Tuple[int, int]]
    heat_map: np.ndarray
    spawn_balance: float
    circulation_loops: List[List[Tuple[int, int]]]
    sightline_map: np.ndarray
    camping_spots: List[Tuple[int, int]]
    average_engagement_distance: float
    map_openness: float
    strategic_positions: List[Tuple[int, int]]


class GraphFlowAnalyzer:
    """Advanced flow analysis using graph theory and lightweight simulation."""

    def __init__(self, resolution: int = 1, seed: Optional[int] = None) -> None:
        set_global_seed(seed)
        self.resolution = max(1, resolution)
        self.graph: Optional[nx.Graph] = None
        self._layout: Optional[np.ndarray] = None
        self._rng = np.random.default_rng(seed)

    # ---------------------------------------------------------------------
    # Public API
    # ---------------------------------------------------------------------
    def analyze_map(
        self,
        layout: np.ndarray,
        spawn_points: List[Tuple[int, int]],
        item_positions: Optional[Dict[str, List[Tuple[int, int]]]] = None,
        wall_height: float = 4.0,
    ) -> FlowMetrics:
        self._layout = layout
        self.graph = self._build_navigation_graph(layout)

        chokepoints = self._find_chokepoints()
        dead_zones = self._find_dead_zones()
        heat_map = self._generate_heat_map(spawn_points, item_positions)
        spawn_balance = self._calculate_spawn_balance(spawn_points, item_positions)
        circulation_loops = self._find_circulation_loops()
        sightline_map = self._calculate_sightlines(layout, wall_height)
        camping_spots = self._find_camping_spots(layout, sightline_map)
        average_engagement_distance = self._calculate_engagement_distance(layout)
        map_openness = self._calculate_openness(layout)
        strategic_positions = self._find_strategic_positions(item_positions)

        return FlowMetrics(
            chokepoints=chokepoints,
            dead_zones=dead_zones,
            heat_map=heat_map,
            spawn_balance=spawn_balance,
            circulation_loops=circulation_loops,
            sightline_map=sightline_map,
            camping_spots=camping_spots,
            average_engagement_distance=average_engagement_distance,
            map_openness=map_openness,
            strategic_positions=strategic_positions,
        )

    def export_metrics(self, metrics: FlowMetrics, output_path: str) -> None:
        data = {
            "version": "1.0",
            "analysis_type": "graph_flow",
            "metrics": {
                "spawn_balance": float(metrics.spawn_balance),
                "map_openness": float(metrics.map_openness),
                "average_engagement_distance": float(metrics.average_engagement_distance),
                "num_chokepoints": len(metrics.chokepoints),
                "num_dead_zones": len(metrics.dead_zones),
                "num_camping_spots": len(metrics.camping_spots),
                "num_circulation_loops": len(metrics.circulation_loops),
                "num_strategic_positions": len(metrics.strategic_positions),
            },
            "positions": {
                "chokepoints": [{"x": p[0], "y": p[1]} for p in metrics.chokepoints],
                "strategic": [{"x": p[0], "y": p[1]} for p in metrics.strategic_positions],
                "camping": [{"x": p[0], "y": p[1]} for p in metrics.camping_spots],
            },
        }
        Path(output_path).write_text(json.dumps(data, indent=2))

    # ------------------------------------------------------------------
    # Graph construction & helpers
    # ------------------------------------------------------------------
    def _build_navigation_graph(self, layout: np.ndarray) -> nx.Graph:
        walkable = (layout == 1) | (layout == 9)
        g = nx.Graph()
        node_map: Dict[Tuple[int, int], int] = {}
        node_id = 0
        h, w = layout.shape

        for y in range(0, h, self.resolution):
            for x in range(0, w, self.resolution):
                if walkable[y, x]:
                    node_map[(x, y)] = node_id
                    g.add_node(node_id, pos=(x, y))
                    node_id += 1

        for (x, y), nid in node_map.items():
            for dx, dy in [
                (1, 0),
                (-1, 0),
                (0, 1),
                (0, -1),
                (1, 1),
                (-1, 1),
                (1, -1),
                (-1, -1),
            ]:
                nx_pos = (x + dx * self.resolution, y + dy * self.resolution)
                if nx_pos in node_map:
                    neighbor = node_map[nx_pos]
                    weight = 1.414 if abs(dx) + abs(dy) == 2 else 1.0
                    g.add_edge(nid, neighbor, weight=weight)

        return g

    # ------------------------------------------------------------------
    # Metric calculations
    # ------------------------------------------------------------------
    def _find_chokepoints(self) -> List[Tuple[int, int]]:
        if not self.graph or self.graph.number_of_nodes() == 0:
            return []
        centrality = nx.betweenness_centrality(self.graph, normalized=True)
        if not centrality:
            return []
        threshold = np.percentile(list(centrality.values()), 90)
        return [self.graph.nodes[n]["pos"] for n, c in centrality.items() if c > threshold]

    def _find_dead_zones(self) -> List[Tuple[int, int]]:
        if not self.graph:
            return []
        return [self.graph.nodes[n]["pos"] for n in self.graph.nodes() if self.graph.degree(n) <= 1]

    def _generate_heat_map(
        self,
        spawns: List[Tuple[int, int]],
        items: Optional[Dict[str, List[Tuple[int, int]]]] = None,
    ) -> np.ndarray:
        if not self.graph:
            return np.zeros((1, 1))

        positions = [self.graph.nodes[n]["pos"] for n in self.graph.nodes()]
        if not positions:
            return np.zeros((1, 1))
        xs, ys = zip(*positions)
        width, height = max(xs) + 1, max(ys) + 1
        heat = np.zeros((height, width), dtype=float)

        spawn_nodes = [self._nearest_node(p) for p in spawns]
        spawn_nodes = [n for n in spawn_nodes if n is not None]
        item_nodes: List[int] = []
        if items:
            for plist in items.values():
                for pos in plist:
                    node = self._nearest_node(pos)
                    if node is not None:
                        item_nodes.append(node)

        num_sims = 800
        walk_len = 40
        for _ in range(num_sims):
            current = self._rng.choice(spawn_nodes) if spawn_nodes else self._rng.choice(list(self.graph.nodes()))
            for _ in range(walk_len):
                px, py = self.graph.nodes[current]["pos"]
                heat[py, px] += 1
                neighbors = list(self.graph.neighbors(current))
                if not neighbors:
                    break
                if item_nodes and self._rng.random() < 0.7:
                    target = self._rng.choice(item_nodes)
                    try:
                        path = nx.shortest_path(self.graph, current, target, weight="weight")
                        current = path[1] if len(path) > 1 else self._rng.choice(neighbors)
                    except nx.NetworkXNoPath:
                        current = self._rng.choice(neighbors)
                else:
                    current = self._rng.choice(neighbors)

        heat = heat / (num_sims + 1)
        return gaussian_filter(heat, sigma=2)

    def _calculate_spawn_balance(
        self,
        spawns: List[Tuple[int, int]],
        items: Optional[Dict[str, List[Tuple[int, int]]]],
    ) -> float:
        if not self.graph or len(spawns) < 2:
            return 0.0
        scores: List[float] = []
        for spawn in spawns:
            node = self._nearest_node(spawn)
            if node is None:
                continue
            distances: List[float] = []
            if items:
                for item_type, plist in items.items():
                    weight = self._item_weight(item_type)
                    for pos in plist:
                        target = self._nearest_node(pos)
                        if target is None:
                            continue
                        try:
                            dist = nx.shortest_path_length(self.graph, node, target, weight="weight")
                            distances.append(dist / (weight + 1))
                        except nx.NetworkXNoPath:
                            continue
            if distances:
                scores.append(float(np.mean(distances)))
        if scores and np.mean(scores) > 0:
            return float(np.std(scores) / np.mean(scores))
        return 0.0

    def _find_circulation_loops(self) -> List[List[Tuple[int, int]]]:
        if not self.graph:
            return []
        loops: List[List[Tuple[int, int]]] = []
        try:
            cycles = nx.simple_cycles(self.graph.to_directed())
            for cycle in cycles:
                if 3 <= len(cycle) <= 20:
                    loops.append([self.graph.nodes[n]["pos"] for n in cycle])
                if len(loops) >= 10:
                    break
        except nx.NetworkXNoCycle:
            pass
        return loops

    def _calculate_sightlines(self, layout: np.ndarray, wall_height: float) -> np.ndarray:
        h, w = layout.shape
        sight = np.zeros((h, w), dtype=float)
        sample_rate = max(1, self.resolution * 2)
        for y in range(0, h, sample_rate):
            for x in range(0, w, sample_rate):
                if layout[y, x] != 1:
                    continue
                visible = 0
                rays = 16
                for angle in np.linspace(0, 2 * np.pi, rays, endpoint=False):
                    dx, dy = np.cos(angle), np.sin(angle)
                    for step in range(1, 50):
                        rx, ry = int(x + dx * step), int(y + dy * step)
                        if rx < 0 or ry < 0 or rx >= w or ry >= h:
                            break
                        if layout[ry, rx] == 2:
                            break
                        if layout[ry, rx] == 1:
                            visible += 1
                sight[y, x] = visible / rays
        if sample_rate > 1:
            from scipy.interpolate import RegularGridInterpolator

            y_s = np.arange(0, h, sample_rate)
            x_s = np.arange(0, w, sample_rate)
            sampled = sight[::sample_rate, ::sample_rate]
            interp = RegularGridInterpolator((y_s, x_s), sampled, method="linear", bounds_error=False, fill_value=0)
            y_full, x_full = np.mgrid[0:h, 0:w]
            sight = interp(np.column_stack([y_full.ravel(), x_full.ravel()])).reshape(h, w)
        return sight

    def _find_camping_spots(self, layout: np.ndarray, sightline_map: np.ndarray) -> List[Tuple[int, int]]:
        spots: List[Tuple[int, int]] = []
        h, w = layout.shape
        for y in range(1, h - 1):
            for x in range(1, w - 1):
                if layout[y, x] != 1:
                    continue
                cover = 0
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        if layout[y + dy, x + dx] == 2:
                            cover += 1
                if 2 <= cover <= 4 and sightline_map[y, x] > 0.3:
                    spots.append((x, y))
        spots.sort(key=lambda p: sightline_map[p[1], p[0]], reverse=True)
        return spots[:20]

    def _calculate_engagement_distance(self, layout: np.ndarray) -> float:
        if not self.graph:
            return 20.0
        floor = [(x, y) for y in range(layout.shape[0]) for x in range(layout.shape[1]) if layout[y, x] == 1]
        if len(floor) < 2:
            return 20.0
        samples = min(80, len(floor))
        indices = self._rng.choice(len(floor), samples, replace=False)
        distances: List[float] = []
        for i in range(samples):
            for j in range(i + 1, min(samples, i + 10)):
                p1 = floor[int(indices[i])]
                p2 = floor[int(indices[j])]
                if self._line_of_sight(layout, p1, p2):
                    distances.append(float(np.linalg.norm(np.array(p1) - np.array(p2))))
        return float(np.mean(distances)) if distances else 20.0

    def _calculate_openness(self, layout: np.ndarray) -> float:
        floor = np.sum(layout == 1)
        walls = np.sum(layout == 2)
        if walls == 0:
            return 1.0
        ratio = floor / float(walls + 1)
        return min(1.0, ratio / 5.0)

    def _find_strategic_positions(self, items: Optional[Dict[str, List[Tuple[int, int]]]]) -> List[Tuple[int, int]]:
        if not self.graph:
            return []
        strategic: List[Tuple[int, int]] = []
        if items:
            all_items = [p for plist in items.values() for p in plist]
            if all_items:
                center = np.mean(all_items, axis=0)
                for node in self.graph.nodes():
                    pos = np.array(self.graph.nodes[node]["pos"])
                    if np.linalg.norm(pos - center) < 30 and self.graph.degree(node) >= 4:
                        strategic.append(tuple(pos))
        centrality = nx.closeness_centrality(self.graph)
        if centrality:
            thresh = np.percentile(list(centrality.values()), 80)
            for node, score in centrality.items():
                if score > thresh:
                    pos = self.graph.nodes[node]["pos"]
                    if pos not in strategic:
                        strategic.append(pos)
        return strategic[:15]

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------
    def _nearest_node(self, pos: Tuple[int, int]) -> Optional[int]:
        if not self.graph:
            return None
        best = None
        best_d = float("inf")
        for node in self.graph.nodes():
            p = self.graph.nodes[node]["pos"]
            d = (p[0] - pos[0]) ** 2 + (p[1] - pos[1]) ** 2
            if d < best_d:
                best_d = d
                best = node
        return best

    def _item_weight(self, item_type: str) -> float:
        weights = {
            "weapon_sniper": 3.0,
            "weapon_rocket": 3.0,
            "armor_heavy": 2.5,
            "health_mega": 2.0,
            "weapon_shotgun": 1.5,
            "armor_light": 1.0,
            "health_small": 0.5,
        }
        return weights.get(item_type, 1.0)

    def _line_of_sight(self, layout: np.ndarray, p1: Tuple[int, int], p2: Tuple[int, int]) -> bool:
        x1, y1 = p1
        x2, y2 = p2
        dx = abs(x2 - x1)
        dy = abs(y2 - y1)
        sx = 1 if x1 < x2 else -1
        sy = 1 if y1 < y2 else -1
        err = dx - dy
        x, y = x1, y1
        while True:
            if layout[y, x] == 2:
                return False
            if x == x2 and y == y2:
                return True
            e2 = 2 * err
            if e2 > -dy:
                err -= dy
                x += sx
            if e2 < dx:
                err += dx
                y += sy


# ----------------------------------------------------------------------
# CLI entry point
# ----------------------------------------------------------------------

def _classify_layout(img: Image.Image) -> np.ndarray:
    gray = np.array(img.convert("L"))
    layout = np.zeros_like(gray, dtype=int)
    layout[gray > 200] = 1  # floor
    layout[gray < 50] = 2   # wall
    layout[(gray >= 50) & (gray <= 200)] = 0
    return layout


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="Analyze map flow using graph theory")
    parser.add_argument("--map", required=True, help="Path to layout PNG")
    parser.add_argument("--output", default="flow_analysis.json", help="Metrics output path")
    parser.add_argument("--visualize", action="store_true", help="Show matplotlib visualization")
    parser.add_argument("--spawns", type=int, default=2, help="Number of spawn points (auto placed if none)")
    args = parser.parse_args()

    img = Image.open(args.map)
    layout = _classify_layout(img)
    h, w = layout.shape
    spawns = [(w // 4, h // 4), (3 * w // 4, 3 * h // 4)] if args.spawns >= 2 else []

    analyzer = GraphFlowAnalyzer()
    metrics = analyzer.analyze_map(layout, spawns)
    analyzer.export_metrics(metrics, args.output)
    print(f"Metrics written to {args.output}")

    if args.visualize:
        analyzer._visualize(metrics, layout)


def _matplotlib_safe_import():
    import matplotlib.pyplot as plt
    return plt


def _imshow(ax, data, title: str, cmap: str = "hot"):
    im = ax.imshow(data, cmap=cmap, interpolation="nearest")
    ax.set_title(title)
    ax.axis("off")
    return im


def _scatter(ax, layout: np.ndarray, points: List[Tuple[int, int]], title: str, color: str):
    ax.imshow(layout, cmap="gray", alpha=0.3)
    if points:
        xs, ys = zip(*points)
        ax.scatter(xs, ys, c=color, s=30, marker="o", linewidths=1, edgecolors="black")
    ax.set_title(title)
    ax.axis("off")


def _summary(ax, metrics: FlowMetrics):
    ax.axis("off")
    txt = f"""
    Spawn Balance: {metrics.spawn_balance:.3f}
    Openness: {metrics.map_openness:.2%}
    Avg Engagement: {metrics.average_engagement_distance:.1f}
    Chokepoints: {len(metrics.chokepoints)}
    Dead Zones: {len(metrics.dead_zones)}
    Camping Spots: {len(metrics.camping_spots)}
    Circulation Loops: {len(metrics.circulation_loops)}
    """
    ax.text(0.05, 0.5, txt, fontsize=11, va="center")


def _visualize(metrics: FlowMetrics, layout: np.ndarray):
    plt = _matplotlib_safe_import()
    plt.figure(figsize=(18, 10))
    ax1 = plt.subplot(2, 3, 1)
    _imshow(ax1, metrics.heat_map, "Heat Map")
    ax2 = plt.subplot(2, 3, 2)
    _scatter(ax2, layout, metrics.chokepoints, f"Chokepoints ({len(metrics.chokepoints)})", "red")
    ax3 = plt.subplot(2, 3, 3)
    _imshow(ax3, metrics.sightline_map, "Exposure", cmap="RdYlGn_r")
    ax4 = plt.subplot(2, 3, 4)
    _scatter(ax4, layout, metrics.dead_zones, f"Dead Zones ({len(metrics.dead_zones)})", "blue")
    ax5 = plt.subplot(2, 3, 5)
    _scatter(ax5, layout, metrics.strategic_positions, f"Strategic ({len(metrics.strategic_positions)})", "gold")
    ax6 = plt.subplot(2, 3, 6)
    _summary(ax6, metrics)
    plt.tight_layout()
    plt.show()


GraphFlowAnalyzer._visualize = staticmethod(_visualize)


if __name__ == "__main__":
    main()
