"""Feature extraction utilities for map blueprints.

Generates boolean masks and semantic metrics (graph measures, density scores)
useful for ML training baselines.
"""

from __future__ import annotations

import logging
from typing import Dict, Tuple, TYPE_CHECKING

import networkx as nx
import numpy as np
from scipy.ndimage import distance_transform_edt, binary_dilation

if TYPE_CHECKING:
    from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.analyzer.quality_analyzer import color_match, COLOR_SPAWN_RED, COLOR_SPAWN_GREEN, COLOR_SPAWN_YELLOW

logger = logging.getLogger(__name__)

def generate_masks(stack: BlueprintStack) -> Dict[str, np.ndarray]:
    """Generate dictionary of boolean masks (H,W) from blueprint layers.
    
    Returns:
        Dict with keys: walkable, wall, water, glass, void, spawn_mask, etc.
    """
    if "layout" not in stack.layers:
        return {}
        
    layout = stack.layers["layout"]
    flow = stack.layers.get("flow", np.zeros_like(layout[:,:,:3])) # Fallback
    
    # 1. Layout Masks
    # Using vectorized matching if possible, or simple rule
    # Floor: Red (255,0,0)
    # Glass: Cyan (0,255,255)
    # Wall: Typically Black (0,0,0) or White (255,255,255) depending on convention,
    # but strictly defined as !Walkable & !Water & !Void?
    
    def match(arr, c):
        diff = np.abs(arr[:,:,:3] - np.array(c))
        return np.all(diff <= 10, axis=-1)

    is_floor = match(layout, (255, 0, 0))
    is_glass = match(layout, (0, 255, 255))
    is_water = match(layout, (0, 0, 255))
    is_void = np.all(layout[:,:,:3] <= 10, axis=-1) # Near black
    
    # Wall is tricky: usually anything not above. Let's define strictly.
    # In some specs, Wall is explicitly White or just "obstacle".
    # We'll define Wall as NOT (Floor|Glass|Water|Void)
    is_known = is_floor | is_glass | is_water | is_void
    is_wall = ~is_known
    
    walkable = is_floor | is_glass
    
    masks = {
        "walkable": walkable.astype(np.uint8),
        "wall": is_wall.astype(np.uint8),
        "water": is_water.astype(np.uint8),
        "glass": is_glass.astype(np.uint8),
        "void": is_void.astype(np.uint8),
    }

    # 2. Flow Masks
    # Spawns (Red, Green, Yellow)
    is_spawn_r = match(flow, COLOR_SPAWN_RED)
    is_spawn_g = match(flow, COLOR_SPAWN_GREEN)
    is_spawn_y = match(flow, COLOR_SPAWN_YELLOW)
    spawn_mask = is_spawn_r | is_spawn_g | is_spawn_y
    
    # Pickups (Blue 0,255,0 ? Wait, Green is spawn. Blue is usually Pickup in some specs, or Cyan)
    # Let's assume standard colors if known.
    # Teleporter: Magenta (255, 0, 255)
    # JumpPad: Orange (255, 128, 0)
    is_teleport = match(flow, (255, 0, 255))
    is_jumppad = match(flow, (255, 128, 0))
    # Pickups often pure blue (0,0,255) or similar
    is_pickup = match(flow, (0, 0, 255)) 

    masks.update({
        "spawn_mask": spawn_mask.astype(np.uint8),
        "teleport_mask": is_teleport.astype(np.uint8),
        "jumppad_mask": is_jumppad.astype(np.uint8),
        "pickup_mask": is_pickup.astype(np.uint8),
    })
    
    return masks


def calculate_features(masks: Dict[str, np.ndarray]) -> Dict[str, float]:
    """Calculate scalar features from masks."""
    
    walkable = masks.get("walkable", np.array([]))
    if walkable.size == 0:
        return {}
        
    h, w = walkable.shape
    total_pixels = h * w
    
    walkable_area = float(np.sum(walkable))
    walkable_ratio = walkable_area / total_pixels
    
    wall_area = float(np.sum(masks.get("wall", np.zeros_like(walkable))))
    wall_ratio = wall_area / total_pixels
    
    # Counts
    spawn_count = int(np.sum(masks.get("spawn_mask", 0)))
    pickup_count = int(np.sum(masks.get("pickup_mask", 0)))
    
    # Graph Metrics
    loopiness = 0.0
    chokepoint_score = 0.0
    
    if walkable_area > 0:
        # Build graph
        # Downsample for speed? Full res is slow for 256x256
        # Let's do simple connected components count first
        from scipy.ndimage import label
        _, n_components = label(walkable) # 8-connectivity default in scipy? No, default is structure=None -> generated
        # Let's reuse graph logic if we want loopiness
        
        # Loopiness approximation: 
        # Number of holes in the walkable shape. 
        # Euler characteristic: V - E + F = 1 - Holes (for connected planar)
        # Using topology is faster than networkx cycles
        # Holes = 1 - (V - E + F) (roughly)
        # Actually simplest is: Connected Components of (Inv Walkable) - 1 (for bounding box)
        # E.g. a donut has 1 hole. Inverse has 2 components (hole + outside).
        inv_walkable = ~walkable.astype(bool)
        # We need to handle border. Pad with True (void)
        inv_padded = np.pad(inv_walkable, 1, constant_values=1)
        _, n_holes = label(inv_padded)
        # n_holes includes the "outside". So actual internal holes = n_holes - 1
        loop_count = max(0, n_holes - 1)
        loopiness = float(loop_count)
        
        # Chokepoininess / Narrowness
        # Mean distance to wall for walkable pixels
        dist_map = distance_transform_edt(walkable)
        avg_width = np.mean(dist_map[walkable > 0]) if np.any(walkable) else 0
        chokepoint_score = 1.0 / (avg_width + 1e-5) # Higher = narrower

    return {
        "walkable_area_ratio": walkable_ratio,
        "wall_area_ratio": wall_ratio,
        "spawn_count": spawn_count,
        "pickup_count": pickup_count,
        "loopiness_score": loopiness,
        "chokepoint_score": chokepoint_score,
        "avg_corridor_width": 2.0 * (1.0 / chokepoint_score) if chokepoint_score > 0 else 0,
    }
