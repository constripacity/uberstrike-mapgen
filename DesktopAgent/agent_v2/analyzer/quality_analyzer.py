"""Map quality analysis utilities for DesktopAgent v2.0."""

from __future__ import annotations

import collections
import json
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Dict, List, Tuple, TYPE_CHECKING, Union

import networkx as nx
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
import yaml

if TYPE_CHECKING:
    from agent_v2.blueprints.stack_io import BlueprintStack

# Unity Color definitions (approximate)
COLOR_SPAWN_RED = (255, 0, 0)
COLOR_SPAWN_GREEN = (0, 255, 0)
COLOR_SPAWN_BLUE = (0, 0, 255) # Not typical, usually Yellow is 3rd team or DM
COLOR_SPAWN_YELLOW = (255, 255, 0)

# Tolerance for color matching (matches Unity)
COLOR_TOLERANCE = 10

def color_match(pixel: Union[np.ndarray, Tuple[int, ...]], target: Tuple[int, ...]) -> Union[bool, np.ndarray]:
    """Matches a pixel color with tolerance. Supports 1D pixel or 3D image."""
    c = np.array(target)
    if isinstance(pixel, (tuple, list)):
        pixel = np.array(pixel)
    
    if pixel.ndim == 3:
        # Image (H, W, C)
        diff = np.abs(pixel[:, :, :3] - c)
        return np.all(diff <= COLOR_TOLERANCE, axis=-1)
    else:
        # Single Pixel (C,)
        diff = np.abs(pixel[:3] - c)
        return np.all(diff <= COLOR_TOLERANCE)

@dataclass
class BlueprintReport:
    status: str  # "pass", "warn", "fail"
    score: float
    reasons: List[str]
    suggested_fixes: List[str]
    metrics: Dict[str, float]

    def to_dict(self) -> dict:
        return asdict(self)

@dataclass
class AnalysisMetrics:
    spawn_count: int
    spawn_balance: float
    path_diversity: float
    verticality: float
    cover_density: float
    sightline: float
    connected_components: int
    playable_area_ratio: float

    def aggregate_score(self) -> float:
        # Base score from gameplay metrics
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
    """Provides gameplay-centric analysis for generated maps with strict QC."""

    def __init__(self, config_path: Path | None = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        # self.config = self._load_config(self.config_path) # Config unused for now

    def analyze(self, stack: BlueprintStack) -> BlueprintReport:
        """Analyze a loaded BlueprintStack."""
        
        if "layout" not in stack.layers or "flow" not in stack.layers:
            return BlueprintReport("fail", 0.0, ["Missing layout or flow layer"], [], {})

        layout = stack.layers["layout"]
        flow = stack.layers["flow"]
        height = stack.layers.get("height", np.zeros_like(layout[:, :, 0])) # Default flat if missing

        metrics = self._compute_metrics(layout, height, flow)
        
        # Strict Pass/Fail Logic
        status = "pass"
        reasons = []
        fixes = []

        if metrics.spawn_count < 8:
            status = "fail"
            reasons.append(f"Insufficient spawns: {metrics.spawn_count} < 8")
            fixes.append("fix_spawns")

        if metrics.connected_components > 1:
            status = "fail"
            reasons.append(f"Map fragmented: {metrics.connected_components} disconnected islands")
            # fixes.append("fix_connectivity") # Not yet implemented

        if metrics.playable_area_ratio < 0.1:
            status = "fail"
            reasons.append("Map too small/empty")
        
        # Warnings
        if status != "fail":
            if metrics.path_diversity < 0.1:
                status = "warn"
                reasons.append("Low path diversity (linear map)")
                fixes.append("fix_corridors")

        # Generate Heatmaps (optional, side effect)
        # self._generate_heatmaps(layout, metrics, stack.stack_path.stem)

        return BlueprintReport(
            status=status,
            score=metrics.aggregate_score() if status != "fail" else 0.0,
            reasons=reasons,
            suggested_fixes=fixes,
            metrics=asdict(metrics)
        )

    def _compute_metrics(self, layout: np.ndarray, height: np.ndarray, flow: np.ndarray) -> AnalysisMetrics:
        # 1. Walkable Mask: Floor(Red) or Glass(Cyan)
        # Assuming layout is RGB. 
        # Floor ~ (255, 0, 0), Glass ~ (0, 255, 255)
        # Wall ~ (0, 0, 0) or (255, 255, 255)? Actually Wall is typically Black in some specs, 
        # but let's stick to positive definition.
        
        is_floor = color_match(layout, (255, 0, 0)) # Red
        is_glass = color_match(layout, (0, 255, 255)) # Cyan
        # Note: color_match vectorization is tricky on full array, let's use numpy masks directly for speed
        
        # Vectorized color matching
        def match_mask(arr, color):
            diff = np.abs(arr[:,:,:3] - np.array(color))
            return np.all(diff <= COLOR_TOLERANCE, axis=-1)

        floor_mask = match_mask(layout, (255, 0, 0))
        glass_mask = match_mask(layout, (0, 255, 255))
        walkable = np.logical_or(floor_mask, glass_mask)
        
        total_pixels = layout.shape[0] * layout.shape[1]
        playable_area = np.sum(walkable)
        playable_ratio = playable_area / total_pixels if total_pixels > 0 else 0

        # 2. Connectivity
        if playable_area > 0:
            # Create graph from walkable pixels
            # 4-connectivity
            labeled_array, num_features = self._label_components(walkable)
            connected_components = num_features
        else:
            connected_components = 0

        # 3. Spawns
        # Spawns are Red, Green, Yellow in Flow layer
        spawn_r = match_mask(flow, COLOR_SPAWN_RED)
        spawn_g = match_mask(flow, COLOR_SPAWN_GREEN)
        spawn_y = match_mask(flow, COLOR_SPAWN_YELLOW)
        spawn_mask = np.logical_or(spawn_r, np.logical_or(spawn_g, spawn_y))
        
        spawn_indices = np.argwhere(spawn_mask)
        spawn_count = spawn_indices.shape[0]

        # 4. Metrics logic (simplified from original for brevity/robustness)
        center = np.array([layout.shape[0] / 2, layout.shape[1] / 2])
        if spawn_count > 0:
            distances = np.linalg.norm(spawn_indices - center, axis=1)
            spawn_balance = float(1.0 - np.std(distances) / (np.max(distances) + 1e-5))
        else:
            spawn_balance = 0.0

        # Path diversity (approximate via distance transform on walkable)
        if playable_area > 0:
            dist_map = distance_transform_edt(walkable)
            path_diversity = float(np.mean(dist_map) / (np.max(dist_map) + 1e-5))
        else:
            path_diversity = 0.0

        verticality = float(np.std(height) / 255.0)
        
        cover_mask = match_mask(flow, (128, 128, 128)) # Grey cover
        cover_density = float(np.mean(cover_mask) * 10.0) # Boost small value

        sightline = 0.5 # Placeholder, expensive to compute accurately without raycast

        return AnalysisMetrics(
            spawn_count=spawn_count,
            spawn_balance=spawn_balance,
            path_diversity=path_diversity,
            verticality=verticality,
            cover_density=cover_density,
            sightline=sightline,
            connected_components=connected_components,
            playable_area_ratio=playable_ratio
        )
    
    def _label_components(self, mask: np.ndarray) -> Tuple[np.ndarray, int]:
        """Label connected components in a binary mask."""
        from scipy.ndimage import label
        structure = np.array([[0,1,0], [1,1,1], [0,1,0]]) # 4-connectivity
        labeled, ncomponents = label(mask, structure)
        return labeled, ncomponents

    # Legacy method wrapper for compatibility if needed
    def analyze_map(self, stack_name: str) -> dict:
        # This would need to load the stack using stack_io
        raise DeprecationWarning("Use analyze(stack) instead.")

__all__ = ["MapQualityAnalyzer", "BlueprintReport"]
