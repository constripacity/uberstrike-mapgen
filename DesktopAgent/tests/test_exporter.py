"""Unit tests for dataset exporter."""

import unittest
import sys
import shutil
import tempfile
import numpy as np
from pathlib import Path
from PIL import Image

# Add parent dir to path
sys.path.append(str(Path(__file__).resolve().parent.parent))

from agent_v2.dataset.exporter import DatasetExporter
from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.dataset import schema

class TestExporter(unittest.TestCase):
    
    def setUp(self):
        self.test_dir = tempfile.mkdtemp()
        self.root = Path(self.test_dir)
        
        # Create Dummy Stack
        self.stack_dir = self.root / "source_stack"
        self.stack_dir.mkdir()
        
        layout = np.zeros((20, 20, 4), dtype=np.uint8)
        layout[0:10, 0:10] = (255, 0, 0, 255) # Valid floor for qc
        Image.fromarray(layout).save(self.stack_dir / "layout.png")
        
        flow = np.zeros((20, 20, 3), dtype=np.uint8) 
        # Add 8 spawns to pass QC
        for i in range(8):
            flow[i, 0] = (255, 0, 0)
        Image.fromarray(flow).save(self.stack_dir / "flow.png")
        
        import json
        with (self.stack_dir / "stack.json").open("w") as f:
            json.dump({
                "layoutPath": "layout.png",
                "flowPath": "flow.png"
            }, f)
            
        self.stack_path = self.stack_dir / "stack.json"

    def tearDown(self):
        shutil.rmtree(self.test_dir)
        
    def test_export_pipeline(self):
        """Test full export run."""
        
        out_base = self.root / "datasets"
        out_base.mkdir()
        
        dataset_name = "test_ds_v1"
        
        exporter = DatasetExporter(base_dir=out_base)
        
        # Export 2 variants
        exporter.export_dataset(
            stack_path=self.stack_path,
            dataset_name=dataset_name,
            variants=2,
            auto_fix=False,
            seed=123
        )
        
        ds_root = out_base / dataset_name
        self.assertTrue(ds_root.exists())
        self.assertTrue((ds_root / "manifest.json").exists())
        
        samples_dir = ds_root / "samples"
        self.assertTrue(samples_dir.exists())
        
        # Should have 2 samples
        samples = list(samples_dir.iterdir())
        self.assertEqual(len(samples), 2)
        
        # Check sample structure
        s0 = samples[0]
        self.assertTrue((s0 / schema.FILENAME_META).exists())
        self.assertTrue((s0 / schema.DIR_LAYERS / "layout.png").exists())
        self.assertTrue((s0 / schema.DIR_ARRAYS / "layout.npy").exists())
        self.assertTrue((s0 / schema.DIR_ARRAYS / schema.FILENAME_MASKS).exists())

if __name__ == "__main__":
    unittest.main()
