"""Blueprint mutation engine.

This module provides the `BlueprintMutator` class, which generates geometric variants
(rotations, mirrors) of a blueprint stack to multiply the training dataset or
provide gameplay variety.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import List

import numpy as np

from agent_v2.blueprints.stack_io import BlueprintStack

logger = logging.getLogger(__name__)


class BlueprintMutator:
    """Generates variants of a blueprint stack."""

    def mutate_stack(self, stack: BlueprintStack, variants: int, out_dir: Path) -> List[Path]:
        """Generate N variants of the given stack.
        
        Args:
            stack: The source BlueprintStack.
            variants: Number of variants to generate.
            out_dir: Directory where variant folders will be created.
            
        Returns:
            List of paths to the generated stack.json files.
        """
        generated_paths = []
        
        # Define available operations
        # We cycle through them deterministically for now
        ops = [
            ("rot90", lambda x: np.rot90(x, k=1)),
            ("rot180", lambda x: np.rot90(x, k=2)),
            ("rot270", lambda x: np.rot90(x, k=3)),
            ("flip_x", lambda x: np.fliplr(x)),
            ("flip_z", lambda x: np.flipud(x)), # 'z' in Unity is usually 'y' in image space
            # Combinations
            ("rot90_flip_x", lambda x: np.fliplr(np.rot90(x, k=1))),
        ]
        
        for i in range(variants):
            op_name, op_func = ops[i % len(ops)]
            suffix = f"v{i+1}_{op_name}"
            
            logger.info(f"Generating variant {i+1}/{variants}: {op_name}")
            
            # Create a deep copy by reloading (inefficient but safe) or just copy in memory.
            # Since BlueprintStack is a dataclass with numpy arrays, we need to be careful.
            # Best way: create new BlueprintStack with copied arrays.
            
            new_layers = {
                k: v.copy() for k, v in stack.layers.items()
            }
            new_meta = stack.meta.copy()
            
            # Create variant stack
            variant = BlueprintStack(
                stack_path=stack.stack_path, # Will be ignored by save
                meta=new_meta,
                layers=new_layers
            )
            
            # Apply transform
            variant.apply_transform(op_func)
            
            # Save
            new_path = variant.save(out_dir, suffix, relative_paths=True)
            generated_paths.append(new_path)
            
        return generated_paths
