"""Shared seed helpers to enforce deterministic runs across numpy/random/torch."""
from __future__ import annotations

import os
import random
from typing import Optional

import numpy as np


def set_global_seed(seed: Optional[int]) -> Optional[int]:
    """Seed Python, NumPy, and (if available) torch for deterministic behaviour."""
    if seed is None:
        return None

    random.seed(seed)
    np.random.seed(seed)
    os.environ["PYTHONHASHSEED"] = str(seed)

    try:
        import torch  # type: ignore

        torch.manual_seed(seed)
        if torch.cuda.is_available():
            torch.cuda.manual_seed_all(seed)
        torch.use_deterministic_algorithms(False)
    except Exception:
        # Torch is optional; ignore if unavailable.
        pass

    return seed

