"""Blueprint IO and manipulation utilities.

This module provides the `BlueprintStack` class, which serves as the Single Source of Truth
for a map blueprint (stack.json + layer images) in memory. It handles loading, saving,
and geometric transformations validation.
"""

from __future__ import annotations

import json
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple, Callable

import numpy as np
from PIL import Image

# Canonical list of supported layers
LAYER_KEYS = ["layout", "flow", "height", "theme", "lighting", "collision"]


@dataclass
class BlueprintStack:
    """Represents a loaded map blueprint with all its layers."""

    stack_path: Path
    meta: Dict[str, object]
    layers: Dict[str, np.ndarray] = field(default_factory=dict)

    @classmethod
    def load(cls, stack_path: Path) -> BlueprintStack:
        """Load a stack and all referenced images."""
        if not stack_path.exists():
            raise FileNotFoundError(f"Stack not found: {stack_path}")

        with stack_path.open("r", encoding="utf-8") as f:
            meta = json.load(f)

        stack_dir = stack_path.parent
        layers = {}

        for key in LAYER_KEYS:
            path_key = f"{key}Path"
            if path_key in meta:
                img_path = stack_dir / str(meta[path_key])
                if img_path.exists():
                    # Load as RGBA to match Unity behavior
                    img = Image.open(img_path).convert("RGBA")
                    layers[key] = np.array(img)
                else:
                    print(f"Warning: Layer {key} referenced but missing at {img_path}")

        return cls(stack_path=stack_path, meta=meta, layers=layers)

    def save(self, out_dir: Path, suffix: str, relative_paths: bool = True) -> Path:
        """Save the stack and layers to a new directory.
        
        Args:
            out_dir: The parent directory to save into.
            suffix: Suffix for the new folder name (e.g., "sanitized", "v1").
            relative_paths: Whether to use relative paths in the JSON (default True).
        
        Returns:
            Path to the new stack.json
        """
        # Create output directory: out_dir/<stack_name>_<suffix>
        base_name = self.stack_path.stem.replace(".stack", "")
        new_folder_name = f"{base_name}_{suffix}"
        target_dir = out_dir / new_folder_name
        target_dir.mkdir(parents=True, exist_ok=True)

        new_meta = self.meta.copy()

        # Save layers
        for key, data in self.layers.items():
            img_filename = f"{key}.png"
            img_path = target_dir / img_filename
            
            Image.fromarray(data).save(img_path)
            
            if relative_paths:
                new_meta[f"{key}Path"] = img_filename
            else:
                new_meta[f"{key}Path"] = str(img_path.absolute())

        # Save JSON
        new_stack_path = target_dir / f"{base_name}_{suffix}.stack.json"
        with new_stack_path.open("w", encoding="utf-8") as f:
            json.dump(new_meta, f, indent=2)

        return new_stack_path

    def apply_transform(self, transform_func: Callable[[np.ndarray], np.ndarray]) -> None:
        """Apply a geometric transform to all present layers in-place."""
        for key in self.layers:
            self.layers[key] = transform_func(self.layers[key])
        
        # If rotation swaps dimensions, we might need to update meta if it stores W/H.
        # Currently standard stack.json doesn't strictly enforce W/H in meta, 
        # but if we did, we'd update it here.
