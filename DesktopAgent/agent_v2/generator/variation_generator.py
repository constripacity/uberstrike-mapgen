"""Procedural variation generator for blueprint stacks."""
from __future__ import annotations

import base64
import io
import json
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
from PIL import Image

from ..validator.stack_validator import StackValidator
from ..generator.layer_generator import AILayerGenerator  # type: ignore  # circular runtime ok

STACK_SUFFIXES = ["layout", "height", "flow", "theme", "lighting", "collision"]


@dataclass
class VariationResult:
    path: Path
    mutations: List[str]


class MapVariationGenerator:
    """Creates variations of existing stack definitions."""

    def __init__(self, output_dir: Path | None = None) -> None:
        self.output_dir = Path(output_dir or "DesktopAgent/variations")
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.validator = StackValidator()
        self.layer_generator = AILayerGenerator()

    def create_variations(self, base_stack: Path | str, count: int = 5) -> List[VariationResult]:
        base_stack = Path(base_stack)
        if not base_stack.exists():
            raise FileNotFoundError(f"Stack not found: {base_stack}")

        results: List[VariationResult] = []
        for idx in range(count):
            mutations: List[str] = []
            stack_copy = self._duplicate_stack(base_stack, suffix=f"variation_{idx}")
            layers = self._load_layers(stack_copy)

            mutation_rate = 0.1 + (idx * 0.05)
            self._mutate_layout(layers["layout"], mutation_rate, mutations)
            self._mutate_height(layers["height"], mutation_rate, mutations)
            self._mutate_flow(layers["flow"], mutations)

            self._save_layers(stack_copy, layers)
            self._update_stack_json(stack_copy)

            validation = self.validator.validate_stack(str(stack_copy))
            if validation.get("status") == "FIXED":
                mutations.append("auto_fix_applied")
                fixed_path = validation.get("stack")
                if fixed_path:
                    stack_copy = Path(fixed_path)

            results.append(VariationResult(path=stack_copy, mutations=mutations))

        return results

    def mutate_and_generate(self, prompt: str, count: int = 3) -> List[VariationResult]:
        base = Path(self.layer_generator.generate_from_prompt(prompt))
        return self.create_variations(base, count=count)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------
    def _duplicate_stack(self, stack_path: Path, suffix: str) -> Path:
        data = json.loads(stack_path.read_text())
        new_name = f"{stack_path.stem}_{suffix}"
        new_stack_path = self.output_dir / f"{new_name}.stack.json"

        data["name"] = new_name
        new_stack_path.parent.mkdir(parents=True, exist_ok=True)
        new_stack_path.write_text(json.dumps(data, indent=2))

        for suffix_name in STACK_SUFFIXES:
            src = stack_path.with_name(f"{stack_path.stem}.{suffix_name}.png")
            txt = src.with_suffix(src.suffix + ".txt")
            dst = self.output_dir / f"{new_name}.{suffix_name}.png"
            if src.exists():
                Image.open(src).save(dst)
            elif txt.exists():
                data = base64.b64decode(Path(txt).read_text().replace("\n", ""))
                Image.open(io.BytesIO(data)).save(dst)

        return new_stack_path

    def _load_layers(self, stack_path: Path) -> Dict[str, Image.Image]:
        layers: Dict[str, Image.Image] = {}
        base = stack_path.with_suffix("")
        for suffix in STACK_SUFFIXES:
            path = base.with_suffix(f".{suffix}.png")
            if path.exists():
                layers[suffix] = Image.open(path).convert("RGBA")
        return layers

    def _save_layers(self, stack_path: Path, layers: Dict[str, Image.Image]) -> None:
        base = stack_path.with_suffix("")
        for suffix, image in layers.items():
            image.save(base.with_suffix(f".{suffix}.png"))

    def _update_stack_json(self, stack_path: Path) -> None:
        data = json.loads(stack_path.read_text())
        data["metersPerPixel"] = data.get("metersPerPixel", 1.0)
        stack_path.write_text(json.dumps(data, indent=2))

    def _mutate_layout(self, layout: Image.Image, mutation_rate: float, log: List[str]) -> None:
        arr = np.array(layout)
        h, w = arr.shape[:2]
        changes = int(h * w * mutation_rate * 0.01)
        for _ in range(max(changes, 1)):
            x = random.randrange(w)
            y = random.randrange(h)
            arr[y, x] = (0, 0, 0, 255) if random.random() < 0.5 else (128, 128, 128, 255)
        layout.paste(Image.fromarray(arr), (0, 0))
        log.append(f"layout_mutation_{mutation_rate:.2f}")

    def _mutate_height(self, height: Image.Image, mutation_rate: float, log: List[str]) -> None:
        arr = np.array(height.convert("L"))
        noise = np.random.normal(0, 20 * mutation_rate, size=arr.shape)
        arr = np.clip(arr + noise, 0, 255).astype(np.uint8)
        height.paste(Image.fromarray(arr, mode="L"))
        log.append("height_noise")

    def _mutate_flow(self, flow: Image.Image, log: List[str]) -> None:
        arr = np.array(flow)
        h, w = arr.shape[:2]
        for _ in range(5):
            x = random.randrange(w)
            y = random.randrange(h)
            arr[y, x] = random.choice([(255, 0, 0, 255), (0, 255, 0, 255), (255, 255, 0, 255)])
        flow.paste(Image.fromarray(arr))
        log.append("flow_spawns_adjusted")
