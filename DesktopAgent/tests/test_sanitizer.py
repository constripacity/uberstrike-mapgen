import unittest
import numpy as np
import sys
from pathlib import Path
from unittest.mock import MagicMock

# Add parent dir to path to find agent_v2
sys.path.append(str(Path(__file__).resolve().parent.parent))

from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.fixer.blueprint_sanitizer import BlueprintSanitizer
from agent_v2.analyzer.quality_analyzer import COLOR_SPAWN_RED

class TestSanitizer(unittest.TestCase):

    def test_fix_spawns_adds_points(self):
        """Test that sanitizer adds spawns when count < 8."""
        
        # 1. Setup Mock Stack
        # 20x20 map
        # Layout: All Red (Floor)
        layout = np.zeros((20, 20, 4), dtype=np.uint8)
        layout[:, :] = (255, 0, 0, 255) # Floor
        
        # Flow: Empty
        flow = np.zeros((20, 20, 3), dtype=np.uint8)
        
        stack = MagicMock()
        stack.layers = {"layout": layout, "flow": flow}
        
        # 2. Sanitize
        sanitizer = BlueprintSanitizer()
        modified = sanitizer.fix_spawns(stack)
        
        self.assertTrue(modified)
        
        # 3. Verify
        # Count non-black pixels in flow
        spawn_pixels = np.any(flow != 0, axis=-1)
        count = np.sum(spawn_pixels)
        self.assertGreaterEqual(count, 8)
        
    def test_fix_spawns_respects_existing(self):
        """Test that sanitizer keeps existing spawns and adds remaining."""
        
        layout = np.zeros((20, 20, 4), dtype=np.uint8)
        layout[:, :] = (255, 0, 0, 255)
        
        flow = np.zeros((20, 20, 3), dtype=np.uint8)
        # Add 2 existing spawns
        flow[5, 5] = COLOR_SPAWN_RED
        flow[15, 15] = COLOR_SPAWN_RED
        
        stack = MagicMock()
        stack.layers = {"layout": layout, "flow": flow}
        
        sanitizer = BlueprintSanitizer()
        modified = sanitizer.fix_spawns(stack)
        
        self.assertTrue(modified)
        
        spawn_pixels = np.any(flow != 0, axis=-1)
        count = np.sum(spawn_pixels)
        self.assertEqual(count, 8) # 2 existing + 6 added
        
        # Assert existing match
        self.assertTrue(np.all(flow[5, 5] == COLOR_SPAWN_RED))

if __name__ == "__main__":
    unittest.main()
