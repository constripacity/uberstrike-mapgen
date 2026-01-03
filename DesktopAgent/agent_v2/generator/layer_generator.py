"""Prompt-driven blueprint stack generator for DesktopAgent v2.0."""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
from PIL import Image, ImageDraw
import yaml


@dataclass
class LayoutSpec:
    name: str
    rooms: List[Tuple[int, int, int, int]]
    corridors: List[Tuple[int, int, int, int]]


class AILayerGenerator:
    """Generates six blueprint layers based on a natural language prompt."""

    def __init__(self, config_path: Path | None = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        self.config = self._load_config(self.config_path)
        self.size = int(self.config.get("generation", {}).get("default_size", 256))
        self.stacks_path = Path(self.config["unity"]["stacks_path"])
        self.stacks_path.mkdir(parents=True, exist_ok=True)

    # ------------------------------------------------------------------
    def generate_from_prompt(self, prompt: str) -> str:
        layout_spec = self._derive_layout(prompt)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        stack_name = f"ArenaGen_{timestamp}"
        base_path = self.stacks_path / stack_name

        layout = self._generate_layout(layout_spec)
        height = self._generate_heightmap()
        flow = self._generate_flow()
        theme = self._generate_theme()
        lighting = self._generate_lighting()
        collision = self._generate_collision(layout)

        layout.save(f"{base_path}.layout.png")
        height.save(f"{base_path}.height.png")
        flow.save(f"{base_path}.flow.png")
        theme.save(f"{base_path}.theme.png")
        lighting.save(f"{base_path}.lighting.png")
        collision.save(f"{base_path}.collision.png")

        stack_json_path = f"{base_path}.stack.json"
        self._write_stack_json(stack_json_path, stack_name)
        return stack_json_path

    # ------------------------------------------------------------------
    def _derive_layout(self, prompt: str) -> LayoutSpec:
        slug = re.sub(r"[^a-z0-9]+", "_", prompt.lower()).strip("_")[:40]
        name = slug or "generated_arena"
        room_match = re.search(r"(\d+)\s*room", prompt.lower())
        room_count = int(room_match.group(1)) if room_match else 4
        grid = math.ceil(math.sqrt(room_count))
        room_size = self.size // (grid * 2)

        rooms: List[Tuple[int, int, int, int]] = []
        corridors: List[Tuple[int, int, int, int]] = []

        for idx in range(room_count):
            row = idx // grid
            col = idx % grid
            x = (col * 2 + 1) * room_size
            y = (row * 2 + 1) * room_size
            rooms.append((x, y, room_size, room_size))

        # Connect rooms horizontally and vertically
        for idx in range(room_count):
            row = idx // grid
            col = idx % grid
            cx, cy, _, _ = rooms[idx]
            if col + 1 < grid and idx + 1 < room_count:
                nx, ny, _, _ = rooms[idx + 1]
                corridors.append((cx + room_size // 2, cy, nx - room_size // 2, cy))
            if row + 1 < grid and idx + grid < room_count:
                nx, ny, _, _ = rooms[idx + grid]
                corridors.append((cx, cy + room_size // 2, cx, ny - room_size // 2))

        return LayoutSpec(name=name, rooms=rooms, corridors=corridors)

    # ------------------------------------------------------------------
    def _generate_layout(self, spec: LayoutSpec) -> Image.Image:
        img = Image.new("RGB", (self.size, self.size), "white")
        draw = ImageDraw.Draw(img)
        border = 2
        draw.rectangle((0, 0, self.size - 1, self.size - 1), outline=(0, 255, 255), width=border)

        wall_color = (0, 0, 0)
        floor_color = (128, 128, 128)
        bridge_color = (128, 0, 128)

        for x, y, w, h in spec.rooms:
            draw.rectangle((x - w // 2, y - h // 2, x + w // 2, y + h // 2), fill=floor_color)
            draw.rectangle((x - w // 2, y - h // 2, x + w // 2, y + h // 2), outline=wall_color, width=3)

        for x1, y1, x2, y2 in spec.corridors:
            draw.line((x1, y1, x2, y2), fill=floor_color, width=10)

        # Add a central bridge if prompt references courtyard
        draw.line((self.size // 4, self.size // 2, 3 * self.size // 4, self.size // 2), fill=bridge_color, width=6)
        return img

    def _generate_heightmap(self) -> Image.Image:
        coords = np.indices((self.size, self.size))
        cx, cy = self.size / 2, self.size / 2
        distances = np.sqrt((coords[0] - cx) ** 2 + (coords[1] - cy) ** 2)
        max_dist = np.max(distances)
        normalized = 1.0 - (distances / max_dist)
        normalized = np.clip(normalized + 0.1 * np.random.rand(self.size, self.size), 0.0, 1.0)
        array = (normalized * 255).astype(np.uint8)
        return Image.fromarray(array, mode="L").convert("RGB")

    def _generate_flow(self) -> Image.Image:
        img = Image.new("RGB", (self.size, self.size), "white")
        draw = ImageDraw.Draw(img)
        spawn_colors = [(255, 0, 0), (0, 255, 0), (255, 255, 0)]
        positions = [
            (self.size // 4, self.size // 4),
            (3 * self.size // 4, self.size // 4),
            (self.size // 2, 3 * self.size // 4),
        ]
        for idx, pos in enumerate(positions):
            draw.ellipse((pos[0] - 4, pos[1] - 4, pos[0] + 4, pos[1] + 4), fill=spawn_colors[idx % len(spawn_colors)])

        orange = (255, 165, 0)
        draw.rectangle((self.size // 2 - 10, self.size // 2 - 3, self.size // 2 + 10, self.size // 2 + 3), fill=orange)
        draw.rectangle((self.size // 2 - 3, self.size // 2 - 20, self.size // 2 + 3, self.size // 2 + 20), fill=orange)

        gray = (128, 128, 128)
        draw.rectangle((self.size // 3, self.size // 3, self.size // 3 + 20, self.size // 3 + 20), fill=gray)
        draw.rectangle((2 * self.size // 3 - 20, 2 * self.size // 3 - 20, 2 * self.size // 3, 2 * self.size // 3), fill=gray)

        arrow_color = (0, 255, 255)
        draw.line((self.size // 4, self.size // 2, self.size // 2, self.size // 2), fill=arrow_color, width=2)
        draw.line((3 * self.size // 4, self.size // 2, self.size // 2, self.size // 2), fill=arrow_color, width=2)
        return img

    def _generate_theme(self) -> Image.Image:
        img = Image.new("RGB", (self.size, self.size), "white")
        draw = ImageDraw.Draw(img)
        draw.rectangle((0, 0, self.size // 3, self.size), fill=(34, 34, 34))
        draw.rectangle((self.size // 3, 0, 2 * self.size // 3, self.size), fill=(85, 68, 51))
        draw.rectangle((2 * self.size // 3, 0, self.size, self.size), fill=(51, 68, 85))
        return img

    def _generate_lighting(self) -> Image.Image:
        img = Image.new("RGB", (self.size, self.size), "black")
        draw = ImageDraw.Draw(img)
        white = (255, 255, 255)
        orange = (255, 208, 128)
        for pos in [
            (self.size // 4, self.size // 4),
            (3 * self.size // 4, self.size // 4),
            (self.size // 4, 3 * self.size // 4),
            (3 * self.size // 4, 3 * self.size // 4),
            (self.size // 2, self.size // 2),
        ]:
            draw.ellipse((pos[0] - 2, pos[1] - 2, pos[0] + 2, pos[1] + 2), fill=white)
        for pos in [
            (self.size // 3, self.size // 3),
            (2 * self.size // 3, self.size // 3),
            (self.size // 3, 2 * self.size // 3),
            (2 * self.size // 3, 2 * self.size // 3),
        ]:
            draw.rectangle((pos[0] - 2, pos[1] - 2, pos[0] + 2, pos[1] + 2), fill=orange)
        return img

    def _generate_collision(self, layout: Image.Image) -> Image.Image:
        layout_pixels = np.array(layout.convert("RGB"))
        collision = np.full_like(layout_pixels, fill_value=255)
        black_mask = np.all(layout_pixels == [0, 0, 0], axis=-1)
        collision[black_mask] = [0, 0, 0]
        climbable_mask = np.zeros_like(black_mask)
        climbable_mask[:, self.size // 2 - 2 : self.size // 2 + 2] = True
        collision[climbable_mask] = [0, 170, 255]
        destructible_mask = np.zeros_like(black_mask)
        destructible_mask[self.size // 3 : self.size // 3 + 10, self.size // 3 : self.size // 3 + 10] = True
        collision[destructible_mask] = [255, 0, 255]
        return Image.fromarray(collision, mode="RGB")

    def _write_stack_json(self, path: str, name: str) -> None:
        data = {
            "metersPerPixel": self.config.get("generation", {}).get("meters_per_pixel", 1.0),
            "wallHeight": self.config.get("generation", {}).get("wall_height", 4.0),
            "heightScale": 0.05,
            "stairsRise": 0.25,
            "rampMaxSlopeDeg": 25,
            "doorWidthMeters": 2.0,
            "bridgeWidthMeters": 3.0,
            "pairTeleporters": True,
            "navmesh": True,
            "themeDefault": "DefaultTheme",
            "themeMap": {
                "#222222": "Industrial",
                "#554433": "Warehouse",
                "#334455": "BlueSteel",
            },
            "flow": {
                "spawnColorYellow": "#FFFF00",
                "spawnColorRed": "#FF0000",
                "spawnColorGreen": "#00FF00",
                "chokeColor": "#FFA500",
                "coverColor": "#808080",
                "arrowColor": "#00FFFF",
            },
            "lighting": {
                "pointColor": "#FFFFFF",
                "spotColor": "#FFD080",
                "sunDirDeg": [50, -30, 0],
                "fogDensity": 0.02,
            },
            "collision": {
                "walkable": "#FFFFFF",
                "blocked": "#000000",
                "climbable": "#00AAFF",
                "destructible": "#FF00FF",
            },
        }
        Path(path).write_text(json.dumps(data, indent=2), encoding="utf-8")

    # ------------------------------------------------------------------
    @staticmethod
    def _load_config(path: Path) -> Dict[str, Dict[str, object]]:
        if not path.exists():
            return {
                "unity": {
                    "stacks_path": str(Path.cwd() / "generated_stacks"),
                },
                "generation": {
                    "default_size": 256,
                    "meters_per_pixel": 1.0,
                    "wall_height": 4.0,
                },
            }
        with path.open("r", encoding="utf-8") as handle:
            return yaml.safe_load(handle)  # type: ignore[return-value]


__all__ = ["AILayerGenerator"]
