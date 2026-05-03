"""WFC tileset test harness.

Faithfully mirrors UberStrike2022/Editor/Stubs/WFCCore.cs so tileset/socket
changes can be A/B tested outside Unity. Runs N seeds at a configurable grid
size against a chosen tileset variant, reports convergence stats, and dumps a
PNG per attempt for visual inspection.

Variants live in this file as pure-data tilesets so a fix can be prototyped
here, validated, and only then ported back to WFCCore.cs.

Usage:
    python wfc_harness.py --variant baseline --size 16 --seeds 32
    python wfc_harness.py --variant wall_interior --size 32 --seeds 64 --out _harness_out
"""

from __future__ import annotations

import argparse
import math
import random
import time
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Sequence, Set, Tuple

try:
    from PIL import Image
except ImportError:
    Image = None


# ---------------------------------------------------------------- tile model

@dataclass(frozen=True)
class Tile:
    type: str
    id: str
    sockets: Tuple[str, str, str, str]  # N, E, S, W
    weight: float = 1.0
    rotation: int = 0

    def rotate(self, times: int) -> "Tile":
        times = times % 4
        if times == 0:
            return self
        s = self.sockets
        for _ in range(times):
            s = (s[3], s[0], s[1], s[2])
        new_rot = (self.rotation + times * 90) % 360
        return Tile(self.type, f"{self.id}_r{new_rot}", s, self.weight, new_rot)


@dataclass
class Variant:
    name: str
    base_tiles: Sequence[Tile]
    rotated_types: Set[str]
    extra_socket_pairs: Set[Tuple[str, str]] = field(default_factory=set)


# ---------------------------------------------------------- variants

# Baseline: matches WFCCore.cs BaseTiles + SocketsMatch exactly.
BASELINE = Variant(
    name="baseline",
    base_tiles=[
        Tile("Void",        "void",        ("void",  "void",  "void",  "void"),  0.05),
        Tile("Floor",       "floor",       ("floor", "floor", "floor", "floor"), 5.0),
        Tile("Wall",        "wall",        ("wall",  "void",  "wall",  "void"),  3.0),
        Tile("WallCorner",  "wall_corner", ("void",  "void",  "wall",  "wall"),  2.0),
        Tile("WallT",       "wall_t",      ("void",  "wall",  "wall",  "wall"),  1.5),
        Tile("WallEnd",     "wall_end",    ("void",  "void",  "wall",  "void"),  1.0),
        Tile("Door",        "door",        ("floor", "wall",  "floor", "wall"),  0.8),
        Tile("Water",       "water",       ("water", "water", "water", "water"), 0.4),
        Tile("Bridge",      "bridge",      ("floor", "water", "floor", "water"), 0.35),
        Tile("Spawn",       "spawn",       ("floor", "floor", "floor", "floor"), 0.15),
    ],
    rotated_types={"Wall", "WallCorner", "WallT", "WallEnd", "Door", "Bridge"},
    extra_socket_pairs={
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    },
)


# Candidate fix A: every wall-family tile gets one face turned to "floor"
# (the interior side). Wall stays continuous along its other axis. This
# gives walls a face that natively bridges to floor — no door required.
WALL_FLOOR_FACE = Variant(
    name="wall_floor_face",
    base_tiles=[
        Tile("Void",        "void",        ("void",  "void",  "void",  "void"),  0.05),
        Tile("Floor",       "floor",       ("floor", "floor", "floor", "floor"), 5.0),
        # Wall: vertical run; N+S = wall; E = floor (interior), W = void (exterior).
        Tile("Wall",        "wall",        ("wall",  "floor", "wall",  "void"),  3.0),
        # Corner: interior of room sits on N+E, walls run S+W.
        Tile("WallCorner",  "wall_corner", ("floor", "floor", "wall",  "wall"),  2.0),
        # T-junction: branch face is floor, three wall sockets.
        Tile("WallT",       "wall_t",      ("floor", "wall",  "wall",  "wall"),  1.5),
        # End cap: open face is floor.
        Tile("WallEnd",     "wall_end",    ("floor", "void",  "wall",  "void"),  1.0),
        Tile("Door",        "door",        ("floor", "wall",  "floor", "wall"),  0.8),
        Tile("Water",       "water",       ("water", "water", "water", "water"), 0.4),
        Tile("Bridge",      "bridge",      ("floor", "water", "floor", "water"), 0.35),
        Tile("Spawn",       "spawn",       ("floor", "floor", "floor", "floor"), 0.15),
    ],
    rotated_types={"Wall", "WallCorner", "WallT", "WallEnd", "Door", "Bridge"},
    extra_socket_pairs={
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    },
)


# Candidate fix B: keep original Wall family (so exterior runs read as
# void on both sides), and add a WallInterior tile that bridges floor to
# wall on perpendicular faces. Symmetric — sits on the boundary between
# a floor region and a wall run.
WALL_INTERIOR_TILE = Variant(
    name="wall_interior",
    base_tiles=[
        Tile("Void",         "void",         ("void",  "void",  "void",  "void"),  0.05),
        Tile("Floor",        "floor",        ("floor", "floor", "floor", "floor"), 5.0),
        Tile("Wall",         "wall",         ("wall",  "void",  "wall",  "void"),  3.0),
        Tile("WallCorner",   "wall_corner",  ("void",  "void",  "wall",  "wall"),  2.0),
        Tile("WallT",        "wall_t",       ("void",  "wall",  "wall",  "wall"),  1.5),
        Tile("WallEnd",      "wall_end",     ("void",  "void",  "wall",  "void"),  1.0),
        Tile("Door",         "door",         ("floor", "wall",  "floor", "wall"),  0.8),
        Tile("Water",        "water",        ("water", "water", "water", "water"), 0.4),
        Tile("Bridge",       "bridge",       ("floor", "water", "floor", "water"), 0.35),
        Tile("Spawn",        "spawn",        ("floor", "floor", "floor", "floor"), 0.15),
        # New: continuous wall (N+S "wall") with floor on E and void on W
        # so it can sit between an interior room and an exterior void run.
        Tile("WallInterior", "wall_interior", ("wall", "floor", "wall", "void"),  2.0),
    ],
    rotated_types={"Wall", "WallCorner", "WallT", "WallEnd", "Door", "Bridge", "WallInterior"},
    extra_socket_pairs={
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    },
)


# Candidate fix B (tuned): same architecture, but heavily floor-biased
# weights so interiors are open arenas instead of mazes. Walls only show
# up at the perimeter (forced by constraints) and as occasional partitions.
WALL_INTERIOR_TUNED = Variant(
    name="wall_interior_tuned",
    base_tiles=[
        Tile("Void",         "void",         ("void",  "void",  "void",  "void"),  0.02),
        Tile("Floor",        "floor",        ("floor", "floor", "floor", "floor"), 18.0),
        Tile("Wall",         "wall",         ("wall",  "void",  "wall",  "void"),  0.6),
        Tile("WallCorner",   "wall_corner",  ("void",  "void",  "wall",  "wall"),  0.4),
        Tile("WallT",        "wall_t",       ("void",  "wall",  "wall",  "wall"),  0.25),
        Tile("WallEnd",      "wall_end",     ("void",  "void",  "wall",  "void"),  0.2),
        Tile("Door",         "door",         ("floor", "wall",  "floor", "wall"),  0.35),
        Tile("Water",        "water",        ("water", "water", "water", "water"), 0.1),
        Tile("Bridge",       "bridge",       ("floor", "water", "floor", "water"), 0.1),
        Tile("Spawn",        "spawn",        ("floor", "floor", "floor", "floor"), 0.15),
        Tile("WallInterior", "wall_interior", ("wall", "floor", "wall", "void"),  0.6),
    ],
    rotated_types={"Wall", "WallCorner", "WallT", "WallEnd", "Door", "Bridge", "WallInterior"},
    extra_socket_pairs={
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    },
)


# Candidate fix C: same as wall_floor_face but additionally flip the
# remaining "void" face to also "floor", so walls can sit anywhere
# between two floor regions (interior partitions).
WALL_DOUBLE_FLOOR = Variant(
    name="wall_double_floor",
    base_tiles=[
        Tile("Void",        "void",        ("void",  "void",  "void",  "void"),  0.05),
        Tile("Floor",       "floor",       ("floor", "floor", "floor", "floor"), 5.0),
        Tile("Wall",        "wall",        ("wall",  "floor", "wall",  "floor"), 3.0),
        Tile("WallCorner",  "wall_corner", ("floor", "floor", "wall",  "wall"),  2.0),
        Tile("WallT",       "wall_t",      ("floor", "wall",  "wall",  "wall"),  1.5),
        Tile("WallEnd",     "wall_end",    ("floor", "floor", "wall",  "floor"), 1.0),
        Tile("Door",        "door",        ("floor", "wall",  "floor", "wall"),  0.8),
        Tile("Water",       "water",       ("water", "water", "water", "water"), 0.4),
        Tile("Bridge",      "bridge",      ("floor", "water", "floor", "water"), 0.35),
        Tile("Spawn",       "spawn",       ("floor", "floor", "floor", "floor"), 0.15),
    ],
    rotated_types={"Wall", "WallCorner", "WallT", "WallEnd", "Door", "Bridge"},
    extra_socket_pairs={
        ("floor", "door"),
        ("wall", "door"),
        ("water", "bridge"),
        ("floor", "bridge"),
    },
)


VARIANTS: Dict[str, Variant] = {
    v.name: v for v in (BASELINE, WALL_FLOOR_FACE, WALL_INTERIOR_TILE, WALL_INTERIOR_TUNED, WALL_DOUBLE_FLOOR)
}


# ---------------------------------------------------------- solver

DIRS = [(0, -1, 0), (1, 0, 1), (0, 1, 2), (-1, 0, 3)]  # N, E, S, W
OPPOSITE = [2, 3, 0, 1]


def sockets_match(a: str, b: str, extras: Set[Tuple[str, str]]) -> bool:
    if a == b:
        return True
    return (a, b) in extras or (b, a) in extras


def build_tileset(v: Variant) -> List[Tile]:
    tiles: List[Tile] = []
    for base in v.base_tiles:
        tiles.append(base)
        if base.type in v.rotated_types:
            tiles.append(base.rotate(1))
            tiles.append(base.rotate(2))
            tiles.append(base.rotate(3))
    return tiles


def build_adjacency(tiles: List[Tile], extras: Set[Tuple[str, str]]) -> List[List[Set[int]]]:
    n = len(tiles)
    adj = [[set() for _ in range(4)] for _ in range(n)]
    for i in range(n):
        for j in range(n):
            if sockets_match(tiles[i].sockets[0], tiles[j].sockets[2], extras):
                adj[i][0].add(j)
            if sockets_match(tiles[i].sockets[1], tiles[j].sockets[3], extras):
                adj[i][1].add(j)
            if sockets_match(tiles[i].sockets[2], tiles[j].sockets[0], extras):
                adj[i][2].add(j)
            if sockets_match(tiles[i].sockets[3], tiles[j].sockets[1], extras):
                adj[i][3].add(j)
    return adj


@dataclass
class RunResult:
    seed: int
    success: bool
    reason: str  # "ok" | "contradiction" | "disconnected" | "out_of_steps"
    restarts: int
    seconds: float
    grid: Optional[List[List[int]]] = None  # only on success
    tile_type_counts: Optional[Counter] = None


class WFCSolver:
    def __init__(self, width: int, height: int, variant: Variant, seed: int, max_restarts: int = 5):
        self.w = width
        self.h = height
        self.variant = variant
        self.tiles = build_tileset(variant)
        self.adj = build_adjacency(self.tiles, variant.extra_socket_pairs)
        self.base_seed = seed
        self.max_restarts = max_restarts
        self.saved_constraints: Dict[Tuple[int, int], str] = {}

    # -------- per-attempt state
    def _init_wave(self, seed: int) -> None:
        self.rng = random.Random(seed)
        n = len(self.tiles)
        self.wave: List[List[Set[int]]] = [
            [set(range(n)) for _ in range(self.w)] for _ in range(self.h)
        ]
        self.grid: List[List[int]] = [[-1] * self.w for _ in range(self.h)]
        if self.saved_constraints:
            self._apply_constraints(self.saved_constraints)

    def _apply_constraints(self, c: Dict[Tuple[int, int], str]) -> None:
        for (x, y), ttype in c.items():
            if not (0 <= x < self.w and 0 <= y < self.h):
                continue
            allowed = {i for i, t in enumerate(self.tiles) if t.type == ttype}
            if allowed:
                self.wave[y][x] = allowed

    # -------- entropy / observe / propagate
    def _entropy(self, x: int, y: int) -> float:
        opts = self.wave[y][x]
        if len(opts) <= 1:
            return math.inf
        weights = [self.tiles[i].weight for i in opts]
        total = sum(weights)
        if total <= 0:
            return math.inf
        e = 0.0
        for w in weights:
            p = w / total
            if p > 0:
                e -= p * math.log(p)
        return e + self.rng.random() * 1e-4

    def _observe(self) -> Tuple[bool, Optional[Tuple[int, int]]]:
        min_e = math.inf
        cands: List[Tuple[int, int]] = []
        for y in range(self.h):
            for x in range(self.w):
                if self.grid[y][x] != -1:
                    continue
                e = self._entropy(x, y)
                if e < min_e:
                    min_e = e
                    cands = [(x, y)]
                elif abs(e - min_e) < 1e-4:
                    cands.append((x, y))
        if not cands:
            return True, None
        x, y = self.rng.choice(cands)
        opts = list(self.wave[y][x])
        if not opts:
            return False, None
        weights = [self.tiles[i].weight for i in opts]
        total = sum(weights)
        if total <= 0:
            return False, None
        choice = self.rng.choices(opts, weights=weights, k=1)[0]
        self.wave[y][x] = {choice}
        self.grid[y][x] = choice
        return True, (x, y)

    def _propagate(self, start: Tuple[int, int]) -> bool:
        from collections import deque
        q = deque([start])
        while q:
            x, y = q.popleft()
            current = self.wave[y][x]
            if not current:
                return False
            for d in range(4):
                dx, dy, _ = DIRS[d]
                nx, ny = x + dx, y + dy
                if not (0 <= nx < self.w and 0 <= ny < self.h):
                    continue
                neighbor = self.wave[ny][nx]
                if len(neighbor) <= 1 and self.grid[ny][nx] != -1:
                    continue
                allowed: Set[int] = set()
                for ti in current:
                    allowed |= self.adj[ti][d]
                before = len(neighbor)
                neighbor &= allowed
                if not neighbor:
                    return False
                if len(neighbor) < before:
                    if len(neighbor) == 1:
                        self.grid[ny][nx] = next(iter(neighbor))
                    q.append((nx, ny))
        return True

    def _has_uncollapsed(self) -> bool:
        for y in range(self.h):
            for x in range(self.w):
                if self.grid[y][x] == -1:
                    return True
        return False

    def _sync_singletons(self) -> None:
        # Constraint application and propagate-without-reduction can leave
        # cells with wave={one_idx} but grid=-1. Sync them so observe doesn't
        # spin on them.
        for y in range(self.h):
            for x in range(self.w):
                if self.grid[y][x] == -1 and len(self.wave[y][x]) == 1:
                    self.grid[y][x] = next(iter(self.wave[y][x]))

    def _collapse_once(self, max_steps: int) -> str:
        for y in range(self.h):
            for x in range(self.w):
                if len(self.wave[y][x]) < len(self.tiles):
                    if not self._propagate((x, y)):
                        return "contradiction"
        self._sync_singletons()
        steps = 0
        while steps < max_steps:
            if not self._has_uncollapsed():
                return "ok"
            steps += 1
            ok, cell = self._observe()
            if not ok:
                return "contradiction"
            if cell and not self._propagate(cell):
                return "contradiction"
            self._sync_singletons()
        return "ok" if not self._has_uncollapsed() else "out_of_steps"

    def _is_walkable(self, t: str) -> bool:
        return t in {"Floor", "Door", "Spawn", "Bridge"}

    def _check_connectivity(self) -> bool:
        walkable: Set[Tuple[int, int]] = set()
        for y in range(self.h):
            for x in range(self.w):
                idx = self.grid[y][x]
                if idx >= 0 and self._is_walkable(self.tiles[idx].type):
                    walkable.add((x, y))
        if not walkable:
            return False
        start = next(iter(walkable))
        stack = [start]
        seen = {start}
        while stack:
            x, y = stack.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (x + dx, y + dy)
                if n in walkable and n not in seen:
                    seen.add(n)
                    stack.append(n)
        return len(seen) == len(walkable)

    def collapse(self, max_steps: int) -> Tuple[bool, str, int]:
        for attempt in range(self.max_restarts + 1):
            seed = self.base_seed + attempt * 7919
            self._init_wave(seed)
            outcome = self._collapse_once(max_steps)
            if outcome != "ok":
                continue
            if not self._check_connectivity():
                continue
            return True, "ok", attempt
        # final outcome from last attempt
        last = outcome if not self._has_uncollapsed() and not self._check_connectivity() else outcome
        if last == "ok":
            last = "disconnected"
        return False, last, self.max_restarts

    def generate_arena(self, spawn_count: int = 4) -> RunResult:
        c: Dict[Tuple[int, int], str] = {}
        for x in range(1, self.w - 1):
            c[(x, 0)] = "Wall"
            c[(x, self.h - 1)] = "Wall"
        for y in range(1, self.h - 1):
            c[(0, y)] = "Wall"
            c[(self.w - 1, y)] = "Wall"
        c[(0, 0)] = "WallCorner"
        c[(self.w - 1, 0)] = "WallCorner"
        c[(0, self.h - 1)] = "WallCorner"
        c[(self.w - 1, self.h - 1)] = "WallCorner"

        spawns = [
            (self.w // 4, self.h // 4),
            (3 * self.w // 4, 3 * self.h // 4),
            (self.w // 4, 3 * self.h // 4),
            (3 * self.w // 4, self.h // 4),
            (self.w // 2, self.h // 4),
            (self.w // 2, 3 * self.h // 4),
            (self.w // 4, self.h // 2),
            (3 * self.w // 4, self.h // 2),
        ]
        for i in range(min(max(0, spawn_count), len(spawns))):
            sx, sy = spawns[i]
            sx = max(2, min(self.w - 3, sx))
            sy = max(2, min(self.h - 3, sy))
            c[(sx, sy)] = "Spawn"

        self.saved_constraints = c

        t0 = time.perf_counter()
        ok, reason, restarts = self.collapse(max_steps=self.w * self.h * 3)
        elapsed = time.perf_counter() - t0

        grid = None
        type_counts: Optional[Counter] = None
        if ok:
            grid = [row[:] for row in self.grid]
            type_counts = Counter(self.tiles[grid[y][x]].type for y in range(self.h) for x in range(self.w))

        return RunResult(
            seed=self.base_seed,
            success=ok,
            reason=reason,
            restarts=restarts,
            seconds=elapsed,
            grid=grid,
            tile_type_counts=type_counts,
        )


# ---------------------------------------------------------- output

COLOR_BY_TYPE = {
    "Void":        (24, 24, 24),
    "Floor":       (160, 160, 160),
    "Wall":        (0, 0, 0),
    "WallCorner":  (32, 32, 32),
    "WallT":       (16, 16, 16),
    "WallEnd":     (48, 48, 48),
    "WallInterior":(64, 64, 64),
    "Door":        (200, 120, 0),
    "Water":       (0, 0, 200),
    "Bridge":      (128, 0, 128),
    "Spawn":       (255, 255, 0),
}


def render_grid_png(result: RunResult, tiles: List[Tile], path: Path, scale: int = 16) -> None:
    if Image is None:
        return
    if not result.grid:
        return
    h = len(result.grid)
    w = len(result.grid[0])
    img = Image.new("RGB", (w * scale, h * scale), (255, 0, 255))
    px = img.load()
    for y in range(h):
        for x in range(w):
            idx = result.grid[y][x]
            if idx < 0:
                color = (255, 0, 255)
            else:
                color = COLOR_BY_TYPE.get(tiles[idx].type, (255, 0, 255))
            for dy in range(scale):
                for dx in range(scale):
                    px[x * scale + dx, y * scale + dy] = color
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


# ---------------------------------------------------------- driver

def run_batch(variant_name: str, size: int, seeds: int, out_dir: Optional[Path], max_restarts: int) -> Dict:
    variant = VARIANTS[variant_name]
    print(f"\n=== variant: {variant.name}  size: {size}x{size}  seeds: {seeds}  max_restarts: {max_restarts} ===")
    summary = {
        "variant": variant_name,
        "size": size,
        "seeds": seeds,
        "ok": 0,
        "contradiction": 0,
        "disconnected": 0,
        "out_of_steps": 0,
        "total_restarts": 0,
        "total_seconds": 0.0,
        "tile_type_totals": Counter(),
    }
    for s in range(seeds):
        solver = WFCSolver(size, size, variant, seed=s, max_restarts=max_restarts)
        r = solver.generate_arena(spawn_count=4)
        summary["total_restarts"] += r.restarts
        summary["total_seconds"] += r.seconds
        if r.success:
            summary["ok"] += 1
            if r.tile_type_counts:
                summary["tile_type_totals"].update(r.tile_type_counts)
        else:
            summary[r.reason] = summary.get(r.reason, 0) + 1
        if out_dir:
            tag = "ok" if r.success else r.reason
            png_path = out_dir / variant_name / f"size{size}_seed{s:03d}_{tag}.png"
            render_grid_png(r, solver.tiles, png_path)
        if seeds <= 16 or s % max(1, seeds // 8) == 0:
            print(f"  seed {s:3d}: {('OK' if r.success else r.reason):14s}  restarts={r.restarts}  {r.seconds*1000:.0f}ms")
    n = max(1, summary["seeds"])
    print(f"\n  result: ok={summary['ok']}/{n}  "
          f"contradiction={summary.get('contradiction', 0)}  "
          f"disconnected={summary.get('disconnected', 0)}  "
          f"avg_restarts={summary['total_restarts'] / n:.2f}  "
          f"avg_ms={summary['total_seconds'] / n * 1000:.0f}")
    if summary["tile_type_totals"]:
        top = summary["tile_type_totals"].most_common()
        total_cells = sum(c for _, c in top)
        print(f"  tile mix:")
        for t, c in top:
            print(f"    {t:13s} {c:6d}  ({100 * c / total_cells:5.1f}%)")
    return summary


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--variant", default="all", help="variant name or 'all'")
    p.add_argument("--size", type=int, default=16)
    p.add_argument("--seeds", type=int, default=16)
    p.add_argument("--max-restarts", type=int, default=5)
    p.add_argument("--out", default="_harness_out", help="output dir for PNGs (relative to script). pass '' to skip")
    args = p.parse_args()

    here = Path(__file__).resolve().parent
    out_dir: Optional[Path] = (here / args.out) if args.out else None

    variants = list(VARIANTS) if args.variant == "all" else [args.variant]
    if any(v not in VARIANTS for v in variants):
        raise SystemExit(f"unknown variant; choices: {list(VARIANTS)}")

    summaries = [run_batch(v, args.size, args.seeds, out_dir, args.max_restarts) for v in variants]

    print("\n=== summary ===")
    print(f"{'variant':22s} {'ok':>6s} {'contra':>8s} {'disc':>6s} {'avg_rs':>7s} {'avg_ms':>7s}")
    for s in summaries:
        n = max(1, s["seeds"])
        print(f"{s['variant']:22s} "
              f"{s['ok']:>4d}/{n:<2d} "
              f"{s.get('contradiction', 0):>8d} "
              f"{s.get('disconnected', 0):>6d} "
              f"{s['total_restarts'] / n:>7.2f} "
              f"{s['total_seconds'] / n * 1000:>7.0f}")


if __name__ == "__main__":
    main()
