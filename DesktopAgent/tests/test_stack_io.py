import json
import shutil
import unittest
import tempfile
import sys
from pathlib import Path

# Add parent dir to path to find agent_v2
sys.path.append(str(Path(__file__).resolve().parent.parent))

import numpy as np
from PIL import Image

from agent_v2.blueprints.stack_io import BlueprintStack

class TestStackIO(unittest.TestCase):

    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.tmp_path = Path(self.test_dir)

    def tearDown(self):
        shutil.rmtree(self.test_dir)

    def test_stack_roundtrip(self):
        """Test loading, saving, and verifying content matches."""
        
        # 1. Create Mock Stack
        stack_dir = self.tmp_path / "mock_stack"
        stack_dir.mkdir()
        
        # Create fake images
        layout = np.zeros((100, 100, 4), dtype=np.uint8)
        layout[50, 50] = (255, 0, 0, 255) # Marker pixel
        Image.fromarray(layout).save(stack_dir / "layout.png")
        
        flow = np.zeros((100, 100, 3), dtype=np.uint8)
        Image.fromarray(flow).save(stack_dir / "flow.png")
        
        meta = {
            "id": "mock",
            "layoutPath": "layout.png",
            "flowPath": "flow.png"
        }
        stack_json = stack_dir / "mock.stack.json"
        with open(stack_json, "w") as f:
            json.dump(meta, f)
            
        # 2. Load
        stack = BlueprintStack.load(stack_json)
        self.assertTrue("layout" in stack.layers)
        self.assertTrue("flow" in stack.layers)
        self.assertTrue(np.array_equal(stack.layers["layout"], layout))
        
        # 3. Save
        out_dir = self.tmp_path / "output"
        new_path = stack.save(out_dir, "roundtrip")
        
        # 4. Verify
        self.assertTrue(new_path.exists())
        self.assertEqual(new_path.parent.name, "mock_roundtrip")
        
        with open(new_path) as f:
            new_meta = json.load(f)
        self.assertEqual(new_meta["layoutPath"], "layout.png") # Relative path check
        
        reloaded = BlueprintStack.load(new_path)
        self.assertTrue(np.array_equal(reloaded.layers["layout"], layout))


    def test_stack_transform(self):
        """Test applied geometric transformation."""
        
        # 1. Mock Stack (3x3 tiny)
        stack_dir = self.tmp_path / "tiny_stack"
        stack_dir.mkdir()
        
        # Layout with marker at (0, 1) -> Top-Middle
        layout = np.zeros((3, 3, 4), dtype=np.uint8)
        layout[0, 1] = (255, 0, 0, 255)
        Image.fromarray(layout).save(stack_dir / "layout.png")
        
        meta = {"layoutPath": "layout.png"}
        stack_json = stack_dir / "tiny.stack.json"
        with open(stack_json, "w") as f:
            json.dump(meta, f)
            
        stack = BlueprintStack.load(stack_json)
        
        # 2. Apply Rotate 90 (Counter-Clockwise)
        stack.apply_transform(lambda x: np.rot90(x, k=1))
        
        transformed = stack.layers["layout"]
        
        # Verify new position - just check it changed and marker exists
        self.assertTrue(np.sum(transformed) > 0)
        self.assertFalse(np.array_equal(layout, transformed))
        
        # Save and reload check
        out_dir = self.tmp_path / "out"
        new_path = stack.save(out_dir, "rot")
        reloaded = BlueprintStack.load(new_path)
        self.assertTrue(np.array_equal(reloaded.layers["layout"], transformed))

if __name__ == "__main__":
    unittest.main()
