"""Voronoi-based theme texture generator for UberStrike MapGen.

This utility produces organic theme regions that align with a stack layout,
optionally respecting a provided layout mask. It exposes a CLI for batch use
and can be imported by the orchestrator for variant generation.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple

import numpy as np
from PIL import Image

try:  # Prefer scipy for a true Gaussian if present
    from scipy.ndimage import gaussian_filter as _gaussian_filter  # type: ignore
except Exception:  # pragma: no cover - runtime fallback when scipy is absent
    _gaussian_filter = None


THEME_COLORS: Dict[str, Tuple[int, int, int]] = {
    "Industrial": (34, 34, 34),
    "Warehouse": (85, 68, 51),
    "SciFi": (51, 68, 85),
    "Outdoor": (68, 85, 51),
    "Tech": (85, 51, 68),
    "Clean": (200, 200, 200),
}


class VoronoiThemeGenerator:
    """Generate Voronoi-based theme maps with smoothing and layout awareness."""

    def __init__(self, theme_weights: Optional[Dict[str, float]] = None):
        self.theme_weights = theme_weights or {
            "Industrial": 1.0,
            "Warehouse": 1.0,
            "SciFi": 1.0,
            "Outdoor": 0.5,
            "Tech": 0.8,
            "Clean": 0.3,
        }

    def generate(
        self,
        width: int,
        height: int,
        num_regions: int = 7,
        seed: Optional[int] = None,
        layout: Optional[np.ndarray] = None,
        smoothing: float = 1.0,
        strategy: str = "poisson",
    ) -> Dict:
        if width <= 0 or height <= 0:
            raise ValueError("Width and height must be positive")
        if num_regions <= 0:
            raise ValueError("num_regions must be positive")

        rng = np.random.default_rng(seed)
        seeds = self._generate_seeds(width, height, num_regions, layout, rng, strategy=strategy)
        region_map = self._compute_regions(width, height, seeds)

        if smoothing > 0:
            region_map = self._smooth_regions(region_map, smoothing)

        assignments = self._assign_themes(num_regions, rng)
        theme_image = self._paint_regions(region_map, assignments, layout)
        metadata = self._metadata(region_map, seeds, assignments)

        return {
            "image": theme_image,
            "regions": metadata,
            "legend": THEME_COLORS,
            "array": region_map,
        }

    # --------------------------- core steps ---------------------------
    def _generate_seeds(
        self,
        width: int,
        height: int,
        num_regions: int,
        layout: Optional[np.ndarray],
        rng: np.random.Generator,
        strategy: str = "poisson",
    ) -> np.ndarray:
        if strategy not in {"random", "grid", "poisson", "weighted"}:
            strategy = "poisson"

        if strategy == "grid":
            return self._grid_seeds(width, height, num_regions, rng)
        if strategy == "random":
            return rng.random((num_regions, 2)) * np.array([[width, height]])
        if strategy == "weighted" and layout is not None:
            return self._weighted(layout, num_regions, rng)
        return self._poisson(width, height, num_regions, rng)

    def _grid_seeds(self, width: int, height: int, num_regions: int, rng: np.random.Generator) -> np.ndarray:
        grid = max(1, int(math.ceil(math.sqrt(num_regions))))
        seeds: List[Tuple[float, float]] = []
        for gy in range(grid):
            for gx in range(grid):
                if len(seeds) >= num_regions:
                    break
                jitter = rng.random(2) * 0.35
                seeds.append(
                    (
                        (gx + 0.5 + jitter[0]) * width / grid,
                        (gy + 0.5 + jitter[1]) * height / grid,
                    )
                )
        return np.array(seeds)

    def _poisson(self, width: int, height: int, num_regions: int, rng: np.random.Generator) -> np.ndarray:
        min_dist = min(width, height) / max(3.0, num_regions * 0.8)
        samples: List[np.ndarray] = [rng.random(2) * np.array([[width, height]])[0]]
        max_attempts = 32
        while len(samples) < num_regions and samples:
            base = samples[rng.integers(0, len(samples))]
            found = False
            for _ in range(max_attempts):
                angle = rng.random() * math.tau
                radius = min_dist + rng.random() * min_dist
                candidate = base + np.array([math.cos(angle) * radius, math.sin(angle) * radius])
                if not (0 <= candidate[0] < width and 0 <= candidate[1] < height):
                    continue
                if all(np.linalg.norm(candidate - s) >= min_dist for s in samples):
                    samples.append(candidate)
                    found = True
                    break
            if not found:
                samples.pop(0)
        while len(samples) < num_regions:
            samples.append(rng.random(2) * np.array([[width, height]])[0])
        return np.array(samples[:num_regions])

    def _weighted(self, layout: np.ndarray, num_regions: int, rng: np.random.Generator) -> np.ndarray:
        mask = (layout > 0).astype(float)
        if mask.sum() <= 0:
            return self._poisson(layout.shape[1], layout.shape[0], num_regions, rng)
        flat = mask.ravel()
        weights = flat / flat.sum()
        indices = rng.choice(len(flat), size=num_regions, replace=False, p=weights)
        ys, xs = np.divmod(indices, layout.shape[1])
        return np.stack([xs, ys], axis=1).astype(float)

    def _compute_regions(self, width: int, height: int, seeds: np.ndarray) -> np.ndarray:
        yy, xx = np.mgrid[0:height, 0:width]
        coords = np.stack([xx, yy], axis=-1).reshape(-1, 2)
        region_map = np.zeros((height * width,), dtype=np.int32)
        min_dist = np.full_like(region_map, np.inf, dtype=float)
        for idx, seed in enumerate(seeds):
            dist = np.linalg.norm(coords - seed, axis=1)
            better = dist < min_dist
            region_map[better] = idx
            min_dist[better] = dist[better]
        return region_map.reshape((height, width))

    def _smooth_regions(self, region_map: np.ndarray, sigma: float) -> np.ndarray:
        regions = int(region_map.max()) + 1
        one_hot = np.zeros(region_map.shape + (regions,), dtype=float)
        for ridx in range(regions):
            one_hot[:, :, ridx] = (region_map == ridx).astype(float)

        if _gaussian_filter is not None:
            for ridx in range(regions):
                one_hot[:, :, ridx] = _gaussian_filter(one_hot[:, :, ridx], sigma=sigma)
        else:  # lightweight fallback
            kernel = self._gaussian_kernel(max(1, int(math.ceil(sigma * 2))), sigma)
            one_hot = self._convolve(one_hot, kernel)

        return np.argmax(one_hot, axis=2).astype(np.int32)

    def _gaussian_kernel(self, radius: int, sigma: float) -> np.ndarray:
        ax = np.arange(-radius, radius + 1)
        xx, yy = np.meshgrid(ax, ax)
        kernel = np.exp(-(xx ** 2 + yy ** 2) / (2 * sigma ** 2))
        kernel /= kernel.sum()
        return kernel

    def _convolve(self, one_hot: np.ndarray, kernel: np.ndarray) -> np.ndarray:
        pad = kernel.shape[0] // 2
        padded = np.pad(one_hot, ((pad, pad), (pad, pad), (0, 0)), mode="edge")
        out = np.zeros_like(one_hot)
        for y in range(out.shape[0]):
            for x in range(out.shape[1]):
                window = padded[y : y + kernel.shape[0], x : x + kernel.shape[1], :]
                out[y, x, :] = (window * kernel[..., None]).sum(axis=(0, 1))
        return out

    def _assign_themes(self, num_regions: int, rng: np.random.Generator) -> List[str]:
        themes = list(self.theme_weights.keys())
        weights = np.array(list(self.theme_weights.values()), dtype=float)
        weights /= weights.sum()
        choices = rng.choice(themes, size=num_regions, replace=True, p=weights)
        return list(choices)

    def _paint_regions(
        self,
        region_map: np.ndarray,
        themes: List[str],
        layout: Optional[np.ndarray],
    ) -> Image.Image:
        h, w = region_map.shape
        rgb = np.zeros((h, w, 3), dtype=np.uint8)
        for region_id, theme in enumerate(themes):
            color = THEME_COLORS.get(theme, (128, 128, 128))
            rgb[region_map == region_id] = color

        if layout is not None:
            mask = layout > 0  # assume >0 indicates walkable
            rgb[~mask] = (0, 0, 0)

        return Image.fromarray(rgb, mode="RGB")

    def _metadata(self, region_map: np.ndarray, seeds: np.ndarray, themes: List[str]) -> Dict:
        data: Dict[str, object] = {
            "seeds": seeds.tolist(),
            "themes": themes,
            "region_sizes": [],
        }
        total = region_map.size
        for idx, theme in enumerate(themes):
            count = int((region_map == idx).sum())
            data["region_sizes"].append(
                {
                    "region_id": idx,
                    "theme": theme,
                    "pixel_count": count,
                    "percentage": round((count / total) * 100.0, 2),
                }
            )
        return data


# --------------------------- CLI ---------------------------

def _load_layout(layout_path: Optional[str]) -> Optional[np.ndarray]:
    if not layout_path:
        return None
    path = Path(layout_path)
    if not path.exists():
        raise FileNotFoundError(path)
    img = Image.open(path).convert("L")
    arr = np.array(img, dtype=float) / 255.0
    return arr


def main(argv: Optional[Iterable[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Generate Voronoi theme maps")
    parser.add_argument("--width", type=int, default=256)
    parser.add_argument("--height", type=int, default=256)
    parser.add_argument("--regions", type=int, default=7)
    parser.add_argument("--output", type=str, default="theme.png")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--smooth", type=float, default=1.0)
    parser.add_argument("--layout", type=str, default=None, help="Optional layout mask (PNG)")
    parser.add_argument("--strategy", type=str, default="poisson", choices=["poisson", "grid", "random", "weighted"])

    args = parser.parse_args(list(argv) if argv is not None else None)

    layout = _load_layout(args.layout)
    generator = VoronoiThemeGenerator()
    result = generator.generate(
        width=args.width,
        height=args.height,
        num_regions=args.regions,
        seed=args.seed,
        layout=layout,
        smoothing=args.smooth,
        strategy=args.strategy,
    )

    out_path = Path(args.output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    result["image"].save(out_path)

    meta_path = out_path.with_suffix(".json")
    meta_path.write_text(json.dumps(result["regions"], indent=2))

    print(f"Generated theme map: {out_path}")
    print(f"Metadata saved: {meta_path}")
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
