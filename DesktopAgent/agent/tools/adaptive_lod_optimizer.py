import json
import heapq
from collections import defaultdict
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

import numpy as np
import trimesh


@dataclass
class MeshLOD:
    """LOD level for a mesh."""

    level: int
    vertex_count: int
    triangle_count: int
    error_metric: float
    distance_threshold: float
    mesh_data: Optional[trimesh.Trimesh] = None


class AdaptiveLODOptimizer:
    """
    Adaptive Level of Detail optimizer for UberStrike maps.
    Uses Quadric Error Metrics for optimal simplification.
    """

    def __init__(self):
        self.lod_levels = [1.0, 0.5, 0.25, 0.1]
        self.distance_thresholds = [0, 20, 50, 100]

    def optimize_map_geometry(
        self,
        mesh_data: Dict[str, trimesh.Trimesh],
        importance_map: Optional[np.ndarray] = None,
    ) -> Dict[str, List[MeshLOD]]:
        """
        Optimize all meshes in the map with adaptive LOD.

        Args:
            mesh_data: Dict of mesh_name -> trimesh object
            importance_map: 2D array of importance values (0-1)

        Returns:
            Dict of mesh_name -> list of LOD levels
        """

        optimized: Dict[str, List[MeshLOD]] = {}
        for mesh_name, mesh in mesh_data.items():
            print(f"Optimizing {mesh_name}: {len(mesh.vertices)} vertices")
            importance = self._get_mesh_importance(mesh, importance_map)
            lod_chain = self._generate_lod_chain(mesh, importance)
            optimized[mesh_name] = lod_chain
            if lod_chain:
                original_tris = len(mesh.faces)
                final_tris = lod_chain[-1].triangle_count
                reduction = (1 - final_tris / max(1, original_tris)) * 100
                print(f"  → Reduced by {reduction:.1f}% at furthest LOD")
        return optimized

    def _generate_lod_chain(self, mesh: trimesh.Trimesh, importance: float) -> List[MeshLOD]:
        lod_chain: List[MeshLOD] = []
        lod_chain.append(
            MeshLOD(
                level=0,
                vertex_count=len(mesh.vertices),
                triangle_count=len(mesh.faces),
                error_metric=0.0,
                distance_threshold=self.distance_thresholds[0],
                mesh_data=mesh.copy(),
            )
        )
        current_mesh = mesh.copy()
        for i, (ratio, distance) in enumerate(zip(self.lod_levels[1:], self.distance_thresholds[1:]), 1):
            adjusted_ratio = ratio * (0.5 + importance * 0.5)
            simplified = self._simplify_quadric(current_mesh, adjusted_ratio)
            if simplified is None or len(simplified.faces) <= 10:
                break
            lod_chain.append(
                MeshLOD(
                    level=i,
                    vertex_count=len(simplified.vertices),
                    triangle_count=len(simplified.faces),
                    error_metric=self._calculate_error(mesh, simplified),
                    distance_threshold=distance,
                    mesh_data=simplified,
                )
            )
            current_mesh = simplified
        return lod_chain

    def _simplify_quadric(self, mesh: trimesh.Trimesh, target_ratio: float) -> Optional[trimesh.Trimesh]:
        target_faces = int(len(mesh.faces) * target_ratio)
        if target_faces < 10:
            return None
        quadrics = self._compute_quadrics(mesh)
        edges = self._get_edges(mesh)
        edge_heap: List[Tuple[float, Tuple[int, int], np.ndarray]] = []
        for edge in edges:
            cost, optimal_pos = self._compute_edge_cost(edge, quadrics, mesh)
            heapq.heappush(edge_heap, (cost, edge, optimal_pos))
        vertices = mesh.vertices.copy()
        faces = mesh.faces.copy()
        vertex_map = {i: i for i in range(len(vertices))}
        collapsed: set[int] = set()
        while len(faces) > target_faces and edge_heap:
            cost, (v1, v2), optimal_pos = heapq.heappop(edge_heap)
            if v1 in collapsed or v2 in collapsed:
                continue
            vertices[v1] = optimal_pos
            vertex_map[v2] = v1
            collapsed.add(v2)
            new_faces = []
            for face in faces:
                remapped = [vertex_map.get(v, v) for v in face]
                if len(set(remapped)) == 3:
                    new_faces.append(remapped)
            faces = np.array(new_faces)
            quadrics[v1] = quadrics[v1] + quadrics[v2]
        simplified = trimesh.Trimesh(vertices=vertices, faces=faces, process=False)
        simplified.remove_degenerate_faces()
        simplified.remove_duplicate_faces()
        simplified.remove_unreferenced_vertices()
        return simplified

    def _compute_quadrics(self, mesh: trimesh.Trimesh) -> Dict[int, np.ndarray]:
        quadrics: Dict[int, np.ndarray] = defaultdict(lambda: np.zeros((4, 4)))
        for face in mesh.faces:
            v0, v1, v2 = mesh.vertices[face]
            normal = np.cross(v1 - v0, v2 - v0)
            norm = np.linalg.norm(normal)
            if norm == 0:
                continue
            normal = normal / norm
            d = -np.dot(normal, v0)
            plane = np.append(normal, d)
            Q = np.outer(plane, plane)
            for vertex_idx in face:
                quadrics[vertex_idx] += Q
        return dict(quadrics)

    def _compute_edge_cost(
        self, edge: Tuple[int, int], quadrics: Dict[int, np.ndarray], mesh: trimesh.Trimesh
    ) -> Tuple[float, np.ndarray]:
        v1, v2 = edge
        Q = quadrics[v1] + quadrics[v2]
        A = Q[:3, :3]
        b = Q[:3, 3]
        try:
            optimal_pos = np.linalg.solve(A, -b)
        except np.linalg.LinAlgError:
            optimal_pos = (mesh.vertices[v1] + mesh.vertices[v2]) / 2
        v_homo = np.append(optimal_pos, 1)
        error = float(np.abs(v_homo.T @ Q @ v_homo))
        return error, optimal_pos

    def _get_edges(self, mesh: trimesh.Trimesh) -> List[Tuple[int, int]]:
        edges: set[Tuple[int, int]] = set()
        for face in mesh.faces:
            for i in range(3):
                v1, v2 = face[i], face[(i + 1) % 3]
                edges.add(tuple(sorted((v1, v2))))
        return list(edges)

    def _calculate_error(self, original: trimesh.Trimesh, simplified: trimesh.Trimesh) -> float:
        samples = original.sample(min(1000, len(original.vertices)))
        distances = []
        for point in samples:
            _, dist, _ = simplified.nearest.on_surface([point])
            distances.append(dist[0])
        return float(np.mean(distances)) if distances else 0.0

    def _get_mesh_importance(self, mesh: trimesh.Trimesh, importance_map: Optional[np.ndarray]) -> float:
        if importance_map is None:
            return 0.5
        bounds = mesh.bounds
        center = mesh.centroid
        map_h, map_w = importance_map.shape
        x = int(center[0] * map_w / max(1e-5, (bounds[1][0] - bounds[0][0] + 1)))
        y = int(center[2] * map_h / max(1e-5, (bounds[1][2] - bounds[0][2] + 1)))
        if 0 <= x < map_w and 0 <= y < map_h:
            return float(np.clip(importance_map[y, x], 0, 1))
        return 0.5

    def create_importance_map(
        self,
        width: int,
        height: int,
        spawn_points: List[Tuple[int, int]],
        chokepoints: List[Tuple[int, int]],
        item_positions: Dict[str, List[Tuple[int, int]]],
    ) -> np.ndarray:
        importance = np.ones((height, width)) * 0.3
        for spawn in spawn_points:
            self._add_importance_zone(importance, spawn, radius=30, value=1.0)
        for choke in chokepoints:
            self._add_importance_zone(importance, choke, radius=20, value=0.9)
        for item_type, positions in item_positions.items():
            if "weapon" in item_type or "armor" in item_type:
                for pos in positions:
                    self._add_importance_zone(importance, pos, radius=15, value=0.7)
        from scipy.ndimage import gaussian_filter

        importance = gaussian_filter(importance, sigma=5)
        return np.clip(importance, 0, 1)

    def _add_importance_zone(
        self, importance_map: np.ndarray, center: Tuple[int, int], radius: float, value: float
    ) -> None:
        h, w = importance_map.shape
        for y in range(max(0, int(center[1] - radius)), min(h, int(center[1] + radius))):
            for x in range(max(0, int(center[0] - radius)), min(w, int(center[0] + radius))):
                dist = np.sqrt((x - center[0]) ** 2 + (y - center[1]) ** 2)
                if dist < radius:
                    strength = value * (1 - dist / radius)
                    importance_map[y, x] = max(importance_map[y, x], strength)

    def export_lod_data(self, lod_data: Dict[str, List[MeshLOD]], output_path: str) -> None:
        export = {
            "version": "1.0",
            "mesh_count": len(lod_data),
            "meshes": {
                mesh_name: {
                    "lod_count": len(chain),
                    "levels": [
                        {
                            "level": lod.level,
                            "vertices": lod.vertex_count,
                            "triangles": lod.triangle_count,
                            "error": lod.error_metric,
                            "distance": lod.distance_threshold,
                        }
                        for lod in chain
                    ],
                }
                for mesh_name, chain in lod_data.items()
            },
        }
        with open(output_path, "w", encoding="utf-8") as handle:
            json.dump(export, handle, indent=2)
        print(f"LOD data exported to: {output_path}")


def main() -> None:
    import argparse
    from pathlib import Path
    from PIL import Image

    parser = argparse.ArgumentParser(description="Optimize meshes with adaptive LOD")
    parser.add_argument("--input", required=True, help="Input mesh file (OBJ/STL)")
    parser.add_argument("--output", default="optimized.obj", help="Output file")
    parser.add_argument("--importance", help="Importance map PNG")
    parser.add_argument("--levels", type=int, default=4, help="Number of LOD levels")
    args = parser.parse_args()

    mesh = trimesh.load(args.input)
    optimizer = AdaptiveLODOptimizer()
    importance = None
    if args.importance:
        img = Image.open(args.importance).convert("L")
        importance = np.array(img) / 255.0
    lod_data = optimizer.optimize_map_geometry({"main": mesh}, importance)
    for i, lod in enumerate(lod_data.get("main", [])):
        if lod.mesh_data:
            output_name = args.output.replace(".obj", f"_lod{i}.obj")
            lod.mesh_data.export(output_name)
            print(f"Saved LOD {i}: {output_name}")


if __name__ == "__main__":
    main()
