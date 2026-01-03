"""High-level orchestration helpers for the UberStrike MapGen toolchain."""
from __future__ import annotations

import asyncio
import json
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

from .unity_automation import UnityAutomation
from .voronoi_theme_generator import VoronoiThemeGenerator


@dataclass
class MapVariant:
    name: str
    seed: int
    legend_path: Path
    scene_root: Path


class MapGenOrchestrator:
    """Convenience wrapper that coordinates Unity automation, dataset export and QC."""

    def __init__(self, project_root: Path | str, unity_path: Optional[str] = None):
        self.project_root = Path(project_root)
        self.unity = UnityAutomation(project_path=str(self.project_root), unity_path=unity_path)
        self.variant_dir = self.project_root / "Assets" / "_Generated" / "Variants"
        self.variant_dir.mkdir(parents=True, exist_ok=True)

    def generate_map_variants(self, base_blueprint: Path | str) -> List[MapVariant]:
        """Generate ten themed variants for the supplied blueprint via MapGen.CLI."""
        base_blueprint = Path(base_blueprint)
        results: List[MapVariant] = []
        for idx in range(10):
            seed = 1337 + idx * 7
            legend_path = self.variant_dir / f"{base_blueprint.stem}_seed{seed}.png"
            args = [
                "-executeMethod", "MapGen.CLI.Run",
                "-seed", str(seed),
                "-size", str(96 + (idx % 3) * 16),
                "-t", str(1 + (idx % 4))
            ]
            asyncio.run(self.unity.launch_and_monitor(args, timeout=UnityAutomation.BUILD_TIMEOUT))
            results.append(MapVariant(name=f"Variant_{idx:00}", seed=seed, legend_path=legend_path, scene_root=self.variant_dir))
        return results

    def extract_patterns_from_originals(self) -> Path:
        """Invoke the editor-side MapPatternExtractor to build training data from all scenes."""
        args = ["-executeMethod", "UnityAI.MapPatternExtractor.ExtractAllScenes"]
        asyncio.run(self.unity.launch_and_monitor(args, timeout=UnityAutomation.BUILD_TIMEOUT))
        pattern_dir = self.project_root / "Assets" / "_Generated" / "Patterns"
        pattern_dir.mkdir(parents=True, exist_ok=True)
        latest = max(pattern_dir.glob("patterns_*.json"), default=None, key=lambda p: p.stat().st_mtime if p.exists() else 0)
        return latest if latest else pattern_dir

    def quality_score(self, generated_map: Path | str) -> float:
        """Calculate a lightweight quality score using exported dataset metadata."""
        generated_map = Path(generated_map)
        meta_file = generated_map / "map.json"
        if not meta_file.exists():
            raise FileNotFoundError(meta_file)
        meta = json.loads(meta_file.read_text())
        qc = meta.get("qc", {})
        area = qc.get("map_area_m2", 0)
        navmesh = 1.0 if qc.get("navmesh_ok", False) else 0.25
        flow = min(qc.get("num_spawns", 0), 12) / 12.0
        los = min(qc.get("avg_long_los_m", 0), 35) / 35.0
        return round((area / 1200.0 + navmesh + flow + los) / 4.0, 3)

    def generate_theme_variants(self, layout_png: Path | str, count: int = 5) -> List[Path]:
        """Generate multiple Voronoi theme variants for a given layout texture."""
        layout_png = Path(layout_png)
        if not layout_png.exists():
            raise FileNotFoundError(layout_png)

        generator = VoronoiThemeGenerator()
        layout_array = None
        try:
            from PIL import Image  # lazy import to avoid hard dependency at module import time

            layout_array = None
            with Image.open(layout_png).convert("L") as img:
                import numpy as np

                layout_array = np.array(img, dtype=float) / 255.0
        except Exception:
            layout_array = None
        results: List[Path] = []
        for idx in range(count):
            regions = random.randint(5, 10)
            res = generator.generate(
                width=256,
                height=256,
                num_regions=regions,
                seed=idx,
                layout=layout_array,
                smoothing=1.25,
            )
            out = self.variant_dir / f"{layout_png.stem}_theme_{idx}.png"
            out.parent.mkdir(parents=True, exist_ok=True)
            res["image"].save(out)
            (out.with_suffix(".json")).write_text(json.dumps(res["regions"], indent=2))
            results.append(out)
        return results
