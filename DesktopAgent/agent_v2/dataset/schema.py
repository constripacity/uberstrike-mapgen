"""Dataset schema and file format definitions.

Defines the contract for the dataset structure, ensuring strict adherence to the
versioned specification.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional

# File Naming Constants
FILENAME_STACK = "stack.json"
FILENAME_META = "meta.json"
FILENAME_QC = "qc_report.json"
FILENAME_FEATURES = "features.json"
FILENAME_MASKS = "masks.npz"

DIR_LAYERS = "layers"
DIR_ARRAYS = "arrays"

# Array filenames (inside arrays/)
ARRAY_LAYOUT = "layout.npy"
ARRAY_FLOW = "flow.npy"
ARRAY_HEIGHT = "height.npy"

# Schema Version
SCHEMA_VERSION = "ds_v1"
BLUEPRINT_SPEC_VERSION = "BlueprintSpec_v1"

@dataclass
class DatasetManifest:
    """Top-level dataset manifest."""
    name: str
    created_utc: str
    exporter: str
    blueprint_spec: str
    sample_count: int
    layer_keys: List[str]
    labels: List[str]
    splits: Dict[str, float]
    
    def to_dict(self) -> dict:
        return self.__dict__

@dataclass
class SampleMeta:
    """Metadata for a single sample."""
    id: str
    source_stack: str
    variant_of: Optional[str]
    transform: Optional[str]
    sanitized: bool
    seed: Optional[int]
    stack_version: str = BLUEPRINT_SPEC_VERSION
    export_version: str = SCHEMA_VERSION
    created_utc: str = ""

    def to_dict(self) -> dict:
        return self.__dict__
