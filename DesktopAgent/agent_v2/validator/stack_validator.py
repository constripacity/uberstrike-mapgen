"""Validation helpers for multi-layer blueprint stacks."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
from PIL import Image
import yaml


class StackValidator:
    """Validates and auto-fixes six-layer blueprint stacks."""

    REQUIRED_JSON_FIELDS = {
        "metersPerPixel",
        "wallHeight",
        "heightScale",
        "stairsRise",
        "rampMaxSlopeDeg",
        "doorWidthMeters",
        "bridgeWidthMeters",
        "pairTeleporters",
        "navmesh",
        "themeDefault",
        "themeMap",
        "flow",
        "lighting",
        "collision",
    }

    def __init__(self, config_path: Path | None = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        self.config = self._load_config(self.config_path)
        self.stacks_path = Path(self.config["unity"]["stacks_path"])

    # ------------------------------------------------------------------
    def validate_stack(self, stack_json: str) -> Dict[str, object]:
        stack_path = Path(stack_json)
        if not stack_path.is_absolute():
            stack_path = self.stacks_path / stack_path
        if not stack_path.exists():
            raise FileNotFoundError(f"Stack definition not found: {stack_path}")

        base = stack_path.with_suffix("")
        layer_suffixes = ["layout", "height", "flow", "theme", "lighting", "collision"]
        errors: List[str] = []
        warnings: List[str] = []

        images: Dict[str, Image.Image] = {}
        for suffix in layer_suffixes:
            path = Path(f"{base}.{suffix}.png")
            if not path.exists():
                errors.append(f"Missing layer: {path.name}")
                continue
            images[suffix] = Image.open(path).convert("RGB")

        if errors:
            return {"status": "ERROR", "errors": errors, "warnings": warnings}

        widths = {img.width for img in images.values()}
        heights = {img.height for img in images.values()}
        if len(widths) > 1 or len(heights) > 1:
            errors.append("Layer dimensions do not match.")

        layout_pixels = np.array(images["layout"])
        if not np.any(np.all(layout_pixels == [0, 0, 0], axis=-1)):
            errors.append("Layout lacks walls (no black pixels).")

        flow_pixels = np.array(images["flow"])
        spawn_mask = np.any(
            [
                np.all(flow_pixels == [255, 0, 0], axis=-1),
                np.all(flow_pixels == [0, 255, 0], axis=-1),
                np.all(flow_pixels == [255, 255, 0], axis=-1),
            ],
            axis=0,
        )
        if not np.any(spawn_mask):
            errors.append("Flow layer missing spawn markers.")

        lighting_pixels = np.array(images["lighting"])
        light_count = int(np.sum(np.any(lighting_pixels > 0, axis=-1)))
        if light_count > int(self.config.get("fixes", {}).get("max_lights", 50)):
            warnings.append(f"Lighting layer contains {light_count} hotspots (>50).")

        json_data = json.loads(stack_path.read_text(encoding="utf-8"))
        missing_keys = self.REQUIRED_JSON_FIELDS - json_data.keys()
        if missing_keys:
            errors.append(f"Stack JSON missing keys: {', '.join(sorted(missing_keys))}")

        fixes: List[str] = []
        if errors:
            fixed_base = base.with_name(base.name + "_fixed")
            fixes = self._attempt_auto_fix(images, fixed_base)
            if fixes:
                self._write_fixed_json(json_data, fixed_base)
                return {
                    "status": "FIXED",
                    "errors": errors,
                    "warnings": warnings,
                    "fixes": fixes,
                    "stack": str(fixed_base.with_suffix(".stack.json")),
                }
            return {"status": "ERROR", "errors": errors, "warnings": warnings}

        return {"status": "OK", "errors": errors, "warnings": warnings, "lights": light_count}

    # ------------------------------------------------------------------
    def _attempt_auto_fix(self, images: Dict[str, Image.Image], fixed_base: Path) -> List[str]:
        fixes: List[str] = []
        flow_img = images.get("flow")
        if flow_img is not None:
            pixels = flow_img.load()
            width, height = flow_img.size
            spawn_colors = [(255, 0, 0), (0, 255, 0), (255, 255, 0)]
            for idx, color in enumerate(spawn_colors):
                px = width // (len(spawn_colors) + 1) * (idx + 1)
                py = height // 2
                pixels[px, py] = color
            fixed_base.parent.mkdir(parents=True, exist_ok=True)
            flow_img.save(f"{fixed_base}.flow.png")
            fixes.append("Injected placeholder spawns into flow layer.")

        layout_img = images.get("layout")
        if layout_img is not None:
            layout_img.save(f"{fixed_base}.layout.png")
        for suffix, img in images.items():
            if suffix in {"layout", "flow"}:
                continue
            img.save(f"{fixed_base}.{suffix}.png")
        return fixes

    def _write_fixed_json(self, json_data: Dict[str, object], fixed_base: Path) -> None:
        json_path = f"{fixed_base}.stack.json"
        Path(json_path).write_text(json.dumps(json_data, indent=2), encoding="utf-8")

    # ------------------------------------------------------------------
    @staticmethod
    def _load_config(path: Path) -> Dict[str, Dict[str, object]]:
        if not path.exists():
            return {
                "unity": {"stacks_path": str(Path.cwd() / "generated_stacks")},
                "fixes": {"max_lights": 50},
            }
        with path.open("r", encoding="utf-8") as handle:
            return yaml.safe_load(handle)  # type: ignore[return-value]


__all__ = ["StackValidator"]
