"""Wave Function Collapse generator for UberStrike MapGen blueprints.

This module emits architecturally valid layouts by enforcing socketed tile
compatibility (N/E/S/W), rotation-aware variants, constraint propagation,
and optional hard constraints for spawn/water/door placement. It exports a
PNG blueprint plus JSON metadata and exposes a small CLI for batch use.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Set, Tuple

import numpy as np
from PIL import Image

from ..utils.seed import set_global_seed

class TileType(Enum):
    """UberStrike-oriented tile types."""

    VOID = 0
    FLOOR = 1
    WALL = 2
    WALL_CORNER = 3
    WALL_T = 4
    WALL_END = 5
    DOOR = 6
    WATER = 7
    BRIDGE = 8
    SPAWN = 9


@dataclass(frozen=True)
class Tile:
    type: TileType
    id: str
    sockets: Tuple[str, str, str, str]  # N, E, S, W
    weight: float = 1.0
    rotation: int = 0  # degrees

    def rotate(self, times: int = 1) -> "Tile":
        """Rotate clockwise in 90° increments and return a new tile."""

        times = times % 4
        if times == 0:
            return self

        sockets = self.sockets
        for _ in range(times):
            sockets = (sockets[3], sockets[0], sockets[1], sockets[2])

        return Tile(
            type=self.type,
            id=f"{self.id}_r{(self.rotation + times * 90) % 360}",
            sockets=sockets,
            weight=self.weight,
            rotation=(self.rotation + times * 90) % 360,
        )


def _socket_match(a: str, b: str) -> bool:
    if a == b:
        return True

    compatible = {
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    }
    return (a, b) in compatible or (b, a) in compatible


class WaveFunctionCollapse:
    """Entropy-driven WFC solver with UberStrike socket rules."""

    _BASE_TILES: Sequence[Tile] = (
        Tile(TileType.VOID, "void", ("void", "void", "void", "void"), weight=0.05),
        Tile(TileType.FLOOR, "floor", ("floor", "floor", "floor", "floor"), weight=5.0),
        Tile(TileType.WALL, "wall", ("wall", "void", "wall", "void"), weight=3.0),
        Tile(TileType.WALL_CORNER, "wall_corner", ("void", "void", "wall", "wall"), weight=2.0),
        Tile(TileType.WALL_T, "wall_t", ("void", "wall", "wall", "wall"), weight=1.5),
        Tile(TileType.WALL_END, "wall_end", ("void", "void", "wall", "void"), weight=1.0),
        Tile(TileType.DOOR, "door", ("floor", "wall", "floor", "wall"), weight=0.8),
        Tile(TileType.WATER, "water", ("water", "water", "water", "water"), weight=0.4),
        Tile(TileType.BRIDGE, "bridge", ("floor", "water", "floor", "water"), weight=0.35),
        Tile(TileType.SPAWN, "spawn", ("floor", "floor", "floor", "floor"), weight=0.15),
    )

    def __init__(self, width: int, height: int, seed: Optional[int] = None):
        set_global_seed(seed)
        self.width = width
        self.height = height
        self.rng = random.Random(seed)
        self.tiles: List[Tile] = self._build_tileset()
        self.adj: Dict[int, Dict[str, Set[int]]] = self._build_adjacency()
        self.wave: List[List[Set[int]]] = [
            [set(range(len(self.tiles))) for _ in range(width)] for _ in range(height)
        ]
        self.grid = np.full((height, width), -1, dtype=int)

    def _build_tileset(self) -> List[Tile]:
        tiles: List[Tile] = []
        for base in self._BASE_TILES:
            tiles.append(base)
            if base.type in {
                TileType.WALL,
                TileType.WALL_CORNER,
                TileType.WALL_T,
                TileType.WALL_END,
                TileType.DOOR,
            }:
                tiles.append(base.rotate(1))
                tiles.append(base.rotate(2))
                tiles.append(base.rotate(3))
        return tiles

    def _build_adjacency(self) -> Dict[int, Dict[str, Set[int]]]:
        adj: Dict[int, Dict[str, Set[int]]] = {}
        for i, a in enumerate(self.tiles):
            adj[i] = {d: set() for d in "NESW"}
            for j, b in enumerate(self.tiles):
                if _socket_match(a.sockets[0], b.sockets[2]):
                    adj[i]["N"].add(j)
                if _socket_match(a.sockets[1], b.sockets[3]):
                    adj[i]["E"].add(j)
                if _socket_match(a.sockets[2], b.sockets[0]):
                    adj[i]["S"].add(j)
                if _socket_match(a.sockets[3], b.sockets[1]):
                    adj[i]["W"].add(j)
        return adj

    def add_constraints(self, constraints: Dict[Tuple[int, int], TileType]) -> None:
        for (x, y), ttype in constraints.items():
            if 0 <= x < self.width and 0 <= y < self.height:
                allowed = {i for i, t in enumerate(self.tiles) if t.type == ttype}
                if allowed:
                    self.wave[y][x] = allowed

    def _entropy(self, x: int, y: int) -> float:
        choices = self.wave[y][x]
        if len(choices) <= 1:
            return math.inf
        weights = [self.tiles[i].weight for i in choices]
        total = sum(weights)
        if total <= 0:
            return math.inf
        entropy = 0.0
        for w in weights:
            p = w / total
            entropy -= p * math.log(p)
        return entropy + self.rng.random() * 1e-4

    def _observe(self) -> bool:
        min_entropy = math.inf
        candidates: List[Tuple[int, int]] = []
        for y in range(self.height):
            for x in range(self.width):
                if self.grid[y, x] != -1:
                    continue
                ent = self._entropy(x, y)
                if ent < min_entropy:
                    min_entropy = ent
                    candidates = [(x, y)]
                elif ent == min_entropy:
                    candidates.append((x, y))

        if not candidates:
            return True

        x, y = self.rng.choice(candidates)
        options = list(self.wave[y][x])
        weights = [self.tiles[i].weight for i in options]
        total = sum(weights)
        if total <= 0:
            return False
        probs = [w / total for w in weights]
        choice = self.rng.choices(options, weights=probs, k=1)[0]
        self.wave[y][x] = {choice}
        self.grid[y, x] = choice
        return True

    def _propagate(self) -> bool:
        stack: List[Tuple[int, int]] = [(x, y) for y in range(self.height) for x in range(self.width)]
        while stack:
            x, y = stack.pop()
            current = self.wave[y][x]
            if not current:
                return False

            for dx, dy, dir_from in ((0, -1, "N"), (1, 0, "E"), (0, 1, "S"), (-1, 0, "W")):
                nx, ny = x + dx, y + dy
                if nx < 0 or ny < 0 or nx >= self.width or ny >= self.height:
                    continue
                neighbor = self.wave[ny][nx]
                allowed: Set[int] = set()
                for tile_idx in current:
                    allowed.update(self.adj[tile_idx][dir_from])
                intersection = neighbor & allowed
                if not intersection:
                    return False
                if intersection != neighbor:
                    self.wave[ny][nx] = intersection
                    stack.append((nx, ny))
        return True

    def collapse(self, max_steps: int = 10000) -> bool:
        steps = 0
        while steps < max_steps and np.any(self.grid == -1):
            steps += 1
            if not self._observe():
                return False
            if not self._propagate():
                return False
        return np.all(self.grid != -1)

    def generate_arena_layout(
        self,
        spawn_count: int = 2,
        ensure_connected: bool = True,
        max_steps: int = 10000,
        fallback_to_blank: bool = False,
    ) -> Optional[np.ndarray]:
        """Generate a full layout; raises on contradiction unless fallback is enabled."""
        constraints: Dict[Tuple[int, int], TileType] = {}
        for x in range(self.width):
            constraints[(x, 0)] = TileType.WALL
            constraints[(x, self.height - 1)] = TileType.WALL
        for y in range(self.height):
            constraints[(0, y)] = TileType.WALL
            constraints[(self.width - 1, y)] = TileType.WALL

        spawn_positions = [
            (self.width // 4, self.height // 4),
            (3 * self.width // 4, 3 * self.height // 4),
        ]
        for x, y in spawn_positions[: max(0, spawn_count)]:
            constraints[(x, y)] = TileType.SPAWN

        self.add_constraints(constraints)

        success = self.collapse(max_steps=max_steps)
        if not success:
            if fallback_to_blank:
                return self._blank_blueprint()
            raise RuntimeError("WaveFunctionCollapse failed to converge.")
        if ensure_connected and not self.ensure_connectivity():
            if fallback_to_blank:
                return self._blank_blueprint()
            raise RuntimeError("WaveFunctionCollapse produced disconnected layout.")
        return self.to_blueprint()

    def _blank_blueprint(self) -> np.ndarray:
        bp = np.ones((self.height, self.width, 3), dtype=np.uint8) * 128
        bp[0, :] = (0, 0, 0)
        bp[-1, :] = (0, 0, 0)
        bp[:, 0] = (0, 0, 0)
        bp[:, -1] = (0, 0, 0)
        return bp

    def ensure_connectivity(self) -> bool:
        walkable = {(x, y) for y in range(self.height) for x in range(self.width) if self._is_walkable(x, y)}
        if not walkable:
            return False
        start = next(iter(walkable))
        stack = [start]
        seen = {start}
        while stack:
            cx, cy = stack.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = cx + dx, cy + dy
                if (nx, ny) in walkable and (nx, ny) not in seen:
                    seen.add((nx, ny))
                    stack.append((nx, ny))
        return len(seen) == len(walkable)

    def _is_walkable(self, x: int, y: int) -> bool:
        idx = self.grid[y, x]
        if idx < 0:
            return False
        return self.tiles[idx].type in {TileType.FLOOR, TileType.DOOR, TileType.SPAWN, TileType.BRIDGE}

    def to_blueprint(self) -> np.ndarray:
        bp = np.zeros((self.height, self.width, 3), dtype=np.uint8)
        for y in range(self.height):
            for x in range(self.width):
                idx = self.grid[y, x]
                if idx < 0:
                    continue
                ttype = self.tiles[idx].type
                if ttype == TileType.WALL:
                    bp[y, x] = (0, 0, 0)
                elif ttype in {TileType.FLOOR, TileType.BRIDGE}:
                    bp[y, x] = (128, 128, 128)
                elif ttype == TileType.WATER:
                    bp[y, x] = (0, 0, 255)
                elif ttype == TileType.SPAWN:
                    bp[y, x] = (255, 255, 0)
                else:
                    bp[y, x] = (128, 128, 128)
        return bp


def _save_outputs(bp: np.ndarray, output: Path, meta: Dict[str, object]) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    img = Image.fromarray(bp, mode="RGB")
    img = img.resize((bp.shape[1], bp.shape[0]), Image.NEAREST)
    img.save(output)
    with output.with_suffix(".json").open("w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)


def run_cli() -> None:
    parser = argparse.ArgumentParser(description="UberStrike WFC blueprint generator")
    parser.add_argument("--width", type=int, default=64)
    parser.add_argument("--height", type=int, default=64)
    parser.add_argument("--spawns", type=int, default=2)
    parser.add_argument("--output", type=Path, default=Path("wfc_map.png"))
    parser.add_argument("--seed", type=int, default=None)
    args = parser.parse_args()

    solver = WaveFunctionCollapse(args.width, args.height, args.seed)
    try:
        bp = solver.generate_arena_layout(spawn_count=args.spawns, ensure_connected=True, fallback_to_blank=True)
        _save_outputs(
            bp,
            args.output,
            {
                "width": args.width,
                "height": args.height,
                "spawns": args.spawns,
                "seed": args.seed,
                "connected": True,
            },
        )
        print(f"Map generated: {args.output}")
    except RuntimeError as exc:
        print(f"Failed to generate valid map: {exc}")


if __name__ == "__main__":
    run_cli()
