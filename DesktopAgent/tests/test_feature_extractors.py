"""Unit tests for feature extractors."""

import unittest
import numpy as np
import sys
from pathlib import Path
from unittest.mock import MagicMock

# Add parent dir to path
sys.path.append(str(Path(__file__).resolve().parent.parent))

from agent_v2.dataset import feature_extractors

class TestFeatures(unittest.TestCase):
    
    def test_masks_generation(self):
        """Test simple mask generation logic."""
        
        # 10x10 layout
        layout = np.zeros((10, 10, 4), dtype=np.uint8)
        # Red floor
        layout[0:5, 0:5] = (255, 0, 0, 255)
        # Cyan glass
        layout[5:10, 5:10] = (0, 255, 255, 255)
        
        flow = np.zeros((10, 10, 3), dtype=np.uint8)
        # Red spawn
        flow[2, 2] = (255, 0, 0)
        
        stack = MagicMock()
        stack.layers = {"layout": layout, "flow": flow}
        
        masks = feature_extractors.generate_masks(stack)
        
        # Check keys
        self.assertIn("walkable", masks)
        self.assertIn("spawn_mask", masks)
        
        # Check content
        # Walkable should include Red and Cyan areas
        self.assertEqual(masks["walkable"][2,2], 1)
        self.assertEqual(masks["walkable"][7,7], 1)
        self.assertEqual(masks["walkable"][8,1], 0) # Empty space
        
        # Spawn mask
        self.assertEqual(masks["spawn_mask"][2,2], 1)
        
    def test_calculate_features(self):
        """Test scalar calculation."""
        
        masks = {
            "walkable": np.ones((10, 10), dtype=np.uint8), # 100 pixels
            "wall": np.zeros((10, 10), dtype=np.uint8),
            "spawn_mask": np.zeros((10, 10), dtype=np.uint8)
        }
        masks["spawn_mask"][0,0] = 1
        masks["spawn_mask"][1,1] = 1 # 2 spawns
        
        feats = feature_extractors.calculate_features(masks)
        
        self.assertAlmostEqual(feats["walkable_area_ratio"], 1.0)
        self.assertEqual(feats["spawn_count"], 2)
        # Loopiness of solid block is 0 (or -1 depending on definition? code says max(0, n_holes-1))
        # Solid block has 0 holes. n_components(inv) -> inv is empty. 
        # Wait, if fully 1s, inv is all 0s. 0 components.
        # Logic: max(0, n_holes - 1). 0 - 1 -> 0. Correct.
        self.assertEqual(feats["loopiness_score"], 0.0)

if __name__ == "__main__":
    unittest.main()
