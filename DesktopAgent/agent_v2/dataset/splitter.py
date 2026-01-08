"""Dataset splitting logic."""

from __future__ import annotations

import random
from typing import Dict, List, Tuple

class DatasetSplitter:
    """Partitions samples into train/val/test splits."""

    def __init__(self, train_ratio: float = 0.8, val_ratio: float = 0.1, test_ratio: float = 0.1, seed: int = 42):
        total = train_ratio + val_ratio + test_ratio
        if abs(total - 1.0) > 1e-5:
             # Normalize
             train_ratio /= total
             val_ratio /= total
             test_ratio /= total
             
        self.ratios = (train_ratio, val_ratio, test_ratio)
        self.seed = seed

    def split(self, sample_ids: List[str]) -> Dict[str, List[str]]:
        """Split a list of IDs into partitions."""
        rng = random.Random(self.seed)
        shuffled = sample_ids.copy()
        rng.shuffle(shuffled)
        
        n = len(shuffled)
        n_train = int(n * self.ratios[0])
        n_val = int(n * self.ratios[1])
        # Remainders go to test
        
        splits = {
            "train": shuffled[:n_train],
            "val": shuffled[n_train:n_train+n_val],
            "test": shuffled[n_train+n_val:]
        }
        return splits
