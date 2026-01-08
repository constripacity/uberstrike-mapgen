"""Main dataset export pipeline."""

from __future__ import annotations

import json
import logging
import shutil
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Optional

import numpy as np

from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.analyzer.quality_analyzer import MapQualityAnalyzer
from agent_v2.fixer.blueprint_sanitizer import BlueprintSanitizer
from agent_v2.mutator.blueprint_mutator import BlueprintMutator
from agent_v2.dataset import schema
from agent_v2.dataset.feature_extractors import generate_masks, calculate_features
from agent_v2.dataset.splitter import DatasetSplitter

logger = logging.getLogger(__name__)

class DatasetExporter:
    """Orchestrates the creation of a MapGen ML dataset."""

    def __init__(self, base_dir: Path):
        self.base_dir = base_dir
        self.samples_dir = base_dir / "samples"
        self.splits_dir = base_dir / "splits"
        self.analyzer = MapQualityAnalyzer()
        self.sanitizer = BlueprintSanitizer()
        self.mutator = BlueprintMutator()

    def export_dataset(self, stack_path: Path, dataset_name: str, variants: int, 
                       auto_fix: bool, seed: int = 42) -> None:
        """Run the full export pipeline."""
        
        # Setup Dirs
        ds_root = self.base_dir / dataset_name
        samples_root = ds_root / "samples"
        splits_root = ds_root / "splits"
        
        for d in [samples_root, splits_root]:
            d.mkdir(parents=True, exist_ok=True)
            
        # Load Source
        try:
            source_stack = BlueprintStack.load(stack_path)
        except Exception as e:
            logger.error(f"Failed to load source stack: {e}")
            return

        # Prepare List for Variants
        generated_sample_ids = []
        
        # 1. Auto-Fix Source (Optional, affects all variants)
        # Note: If we auto-fix, we change the seed blueprint for all mutations
        is_sanitized = False
        if auto_fix:
            report = self.analyzer.analyze(source_stack)
            if report.status == "fail":
                logger.info("Auto-fixing source stack before mutation...")
                if self.sanitizer.sanitize(source_stack, report.to_dict()):
                    is_sanitized = True
        
        # 2. Mutate
        # We generate variants. If variants=0, we just export source? 
        # Typically we want at least the source.
        # Let's treat source as variant 0 if we want, or just generate N variants.
        # Plan says: "Mutate variants".
        
        # Generate variants in a temp dir first, then process them
        logger.info(f"Generating {variants} variants...")
        
        # 3. Process Variants
        from tqdm import tqdm
        
        # We'll use the mutator's ability to save to disk, but we want to process them into DS format.
        # Mutator saves to explicit paths. We can use a temp dir.
        import tempfile
        with tempfile.TemporaryDirectory() as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            
            logger.info("Generating variants...")
            variant_paths = self.mutator.mutate_stack(source_stack, variants, tmp_dir)
            
            logger.info("Processing samples (Analysis + Features)...")
            # Wrap loop with tqdm for progress bar
            with tqdm(total=len(variant_paths), unit="sample", desc="Exporting") as pbar:
                for i, v_path in enumerate(variant_paths):
                    sample_id = f"{i+1:06d}_{dataset_name}_{v_path.parent.name}"
                    self._process_sample(
                        stack_path=v_path,
                        sample_id=sample_id,
                        out_dir=samples_root / sample_id,
                        source_ref=str(stack_path),
                        is_sanitized=is_sanitized,
                        seed=seed + i,
                        variant_info=v_path.parent.name
                    )
                    generated_sample_ids.append(sample_id)
                    pbar.update(1)

        # 4. Splits
        logger.info("Generating splits...")
        splitter = DatasetSplitter(seed=seed)
        splits = splitter.split(generated_sample_ids)
        
        for name, ids in splits.items():
            with (splits_root / f"{name}.txt").open("w") as f:
                f.write("\n".join(ids))

        # 5. Manifest
        logger.info("Writing manifest...")
        manifest = schema.DatasetManifest(
            name=dataset_name,
            created_utc=datetime.now(timezone.utc).isoformat(),
            exporter="DesktopAgent.agent_v2.dataset.exporter",
            blueprint_spec=schema.BLUEPRINT_SPEC_VERSION,
            sample_count=len(generated_sample_ids),
            layer_keys=["layout", "flow", "height"], # Actual present keys dynamically?
            labels=["qc_status", "qc_score", "loopiness_score", "chokepoint_score"],
            splits={"train": 0.8, "val": 0.1, "test": 0.1}
        )
        
        with (ds_root / "manifest.json").open("w") as f:
            json.dump(manifest.to_dict(), f, indent=2)
            
        logger.info(f"Dataset export complete: {ds_root}")

    def _process_sample(self, stack_path: Path, sample_id: str, out_dir: Path,
                        source_ref: str, is_sanitized: bool, seed: int, variant_info: str):
        """Convert a standard stack into a dataset sample."""
        
        out_dir.mkdir(exist_ok=True)
        
        # Load (it's already saved by mutator, but we load to memory for analysis/arrays)
        stack = BlueprintStack.load(stack_path)
        
        # A. Analyze
        report = self.analyzer.analyze(stack)
        
        # B. Features & Masks
        masks = generate_masks(stack)
        features = calculate_features(masks)
        
        # C. Save Files
        
        # 1. Stack & Layers (Raw Images)
        # We can just copy the stack files, but we want strict structure
        # layers/ *.png
        layers_dir = out_dir / schema.DIR_LAYERS
        layers_dir.mkdir()
        
        # Save stack using strict relative paths
        stack.save(out_dir, "stack", relative_paths=True)
        # Wait, stack.save creates a SUBDIR. We want flat in out_dir?
        # BlueprintStack.save signature: save(out_dir, suffix) -> creates out_dir/name_suffix/
        # We want to control exact layout.
        
        # Manual Save for strict DS layout
        # Save images to layers/
        new_meta = stack.meta.copy()
        for key, img in stack.layers.items():
            filename = f"{key}.png"
            # Keep as RGBA uint8 PNG
            from PIL import Image
            Image.fromarray(img).save(layers_dir / filename)
            new_meta[f"{key}Path"] = f"{schema.DIR_LAYERS}/{filename}"
            
        with (out_dir / schema.FILENAME_STACK).open("w") as f:
             json.dump(new_meta, f, indent=2)
             
        # 2. Arrays (NPY)
        arrays_dir = out_dir / schema.DIR_ARRAYS
        arrays_dir.mkdir()
        
        for key, img in stack.layers.items():
            # Save as standard .npy
            np.save(arrays_dir / f"{key}.npy", img)
            
        # 3. Masks (NPZ)
        np.savez_compressed(arrays_dir / schema.FILENAME_MASKS, **masks)
        
        # 4. JSONs
        with (out_dir / schema.FILENAME_QC).open("w") as f:
            json.dump(report.to_dict(), f, indent=2)
            
        with (out_dir / schema.FILENAME_FEATURES).open("w") as f:
            json.dump(features, f, indent=2)
            
        sample_meta = schema.SampleMeta(
            id=sample_id,
            source_stack=source_ref,
            variant_of=source_ref, # Simplify
            transform=variant_info,
            sanitized=is_sanitized,
            seed=seed,
            created_utc=datetime.now(timezone.utc).isoformat()
        )
        
        with (out_dir / schema.FILENAME_META).open("w") as f:
            json.dump(sample_meta.to_dict(), f, indent=2)
