"""Automated blueprint remediation.

This module provides the `BlueprintSanitizer` class, which proactively repairs
blueprints (images) to meet strict QC requirements before they reach Unity.
"""

from __future__ import annotations

import logging
from typing import List, Tuple, TYPE_CHECKING

import numpy as np
from scipy.spatial.distance import cdist

from agent_v2.blueprints.stack_io import LAYER_KEYS

if TYPE_CHECKING:
    from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.analyzer.quality_analyzer import COLOR_SPAWN_RED, COLOR_SPAWN_GREEN, COLOR_SPAWN_YELLOW, COLOR_TOLERANCE, color_match

logger = logging.getLogger(__name__)

# Colors to cycle through for new spawns
SPAWN_COLORS = [COLOR_SPAWN_RED, COLOR_SPAWN_GREEN, COLOR_SPAWN_YELLOW]

class BlueprintSanitizer:
    """Applies proactive fixes to map blueprints."""

    def sanitize(self, stack: BlueprintStack, report: dict) -> bool:
        """Apply fixes based on the analysis report. Returns True if any changes were made."""
        
        fixes_needed = report.get("suggested_fixes", [])
        if not fixes_needed:
            return False

        modified = False
        
        if "fix_spawns" in fixes_needed:
            if self.fix_spawns(stack):
                modified = True
                logger.info("Fixed spawns")

        return modified

    def fix_spawns(self, stack: BlueprintStack) -> bool:
        """Ensure at least 8 spawns exist, distributed via Farthest Point Sampling."""
        
        if "layout" not in stack.layers or "flow" not in stack.layers:
            return False

        layout = stack.layers["layout"]
        flow = stack.layers["flow"]
        
        # 1. Identify Walkable Mask (Floor | Glass)
        # Using vectorized matching similar to Analyzer
        def match_mask(arr, color):
            diff = np.abs(arr[:,:,:3] - np.array(color))
            return np.all(diff <= COLOR_TOLERANCE, axis=-1)

        floor_mask = match_mask(layout, (255, 0, 0))
        glass_mask = match_mask(layout, (0, 255, 255))
        walkable = np.logical_or(floor_mask, glass_mask)

        # 2. Identify Existing Spawns
        spawn_r = match_mask(flow, COLOR_SPAWN_RED)
        spawn_g = match_mask(flow, COLOR_SPAWN_GREEN)
        spawn_y = match_mask(flow, COLOR_SPAWN_YELLOW)
        spawn_mask = np.logical_or(spawn_r, np.logical_or(spawn_g, spawn_y))
        
        current_spawns = np.argwhere(spawn_mask) # (N, 2) coords
        count = len(current_spawns)
        
        if count >= 8:
            return False # No fix needed

        missing = 8 - count
        
        # 3. Generate Candidates
        # Filter walkable pixels to valid candidates (optional: 3x3 clearance)
        # Simple erosion to ensure we don't spawn on edge pixels next to walls
        from scipy.ndimage import binary_erosion
        valid_candidates_mask = binary_erosion(walkable, structure=np.ones((3,3)))
        candidate_coords = np.argwhere(valid_candidates_mask)
        
        if len(candidate_coords) == 0:
            logger.warning("No valid spawn candidates found (map full/empty?)")
            return False

        # 4. Farthest Point Sampling
        # Initialize selected with current spawns
        selected_indices = [] # Indices into candidate_coords
        
        # Determine existing points to measure distance against
        # If no current spawns, pick random first candidate
        if count == 0:
            import random
            first_idx = random.randint(0, len(candidate_coords) - 1)
            new_spawn = candidate_coords[first_idx]
            self._paint_spawn(flow, new_spawn, 0)
            
            # Update state
            current_spawns = np.array([new_spawn])
            missing -= 1
        
        # Iteratively add farthest points
        for i in range(missing):
            # Compute distance from every candidate to NEAREST existing spawn
            # cdist returns matrix (candidates x existing)
            dists = cdist(candidate_coords, current_spawns, metric='euclidean')
            min_dists = np.min(dists, axis=1) # (candidates,)
            
            # Select candidate with MAX min_dist
            best_candidate_idx = np.argmax(min_dists)
            best_candidate = candidate_coords[best_candidate_idx]
            
            # Paint it
            color_idx = (count + i) % 3
            self._paint_spawn(flow, best_candidate, color_idx)
            
            # Add to current_spawns for next iteration
            current_spawns = np.vstack([current_spawns, best_candidate])
            
        return True

    def _paint_spawn(self, flow: np.ndarray, coord: np.ndarray, color_idx: int) -> None:
        """Paint a single pixel in the flow layer."""
        y, x = coord
        color = SPAWN_COLORS[color_idx]
        # flow is (H, W, 4) or (H, W, 3)
        if flow.shape[2] == 4:
            flow[y, x] = (*color, 255)
        else:
            flow[y, x] = color
