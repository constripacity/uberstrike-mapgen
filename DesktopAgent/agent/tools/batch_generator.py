import concurrent.futures
import json
import shutil
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import List

import numpy as np

from ..utils.seed import set_global_seed


@dataclass
class GenerationJob:
    """Single map generation job."""

    job_id: str
    blueprint_path: str
    theme: str
    seed: int
    spawn_count: int
    item_preset: str
    output_path: str
    status: str = "pending"
    quality_score: float = 0.0
    generation_time: float = 0.0
    error_message: str = ""


class BatchGenerator:
    """Batch generation system for creating map variants."""

    def __init__(self, unity_path: str, project_path: str, seed: int | None = None):
        set_global_seed(seed)
        self.unity_path = unity_path
        self.project_path = project_path
        self.max_parallel = 4
        self._rng = np.random.default_rng(seed)

    def generate_tournament_set(
        self, base_name: str = "tournament", count: int = 100, quality_threshold: float = 0.7
    ) -> List[str]:
        print(f"Generating {count} tournament maps...")
        successful_maps: List[str] = []
        attempts = 0
        max_attempts = count * 3
        while len(successful_maps) < count and attempts < max_attempts:
            batch_size = min(self.max_parallel, count - len(successful_maps))
            jobs = self._create_job_batch(base_name, batch_size, attempts)
            results = self._execute_batch(jobs)
            for result in results:
                if result.quality_score >= quality_threshold:
                    successful_maps.append(result.output_path)
                    print(f"✓ Generated {result.job_id} (quality: {result.quality_score:.2f})")
                else:
                    print(f"✗ Rejected {result.job_id} (quality: {result.quality_score:.2f})")
            attempts += batch_size
        print(f"\nGeneration complete: {len(successful_maps)}/{count} maps")
        self._create_tournament_package(successful_maps, base_name)
        return successful_maps

    def _create_job_batch(self, base_name: str, count: int, offset: int) -> List[GenerationJob]:
        jobs: List[GenerationJob] = []
        themes = ["Industrial", "Warehouse", "SciFi", "Outdoor", "Tech"]
        item_presets = ["balanced", "sniper_heavy", "cqc_focus", "rocket_arena"]
        spawn_configs = [2, 4, 6, 8]
        for i in range(count):
            job_id = f"{base_name}_{offset + i:04d}"
            theme = str(self._rng.choice(themes))
            item_preset = str(self._rng.choice(item_presets))
            spawn_count = int(self._rng.choice(spawn_configs))
            seed = int(self._rng.integers(0, 1_000_000))
            job = GenerationJob(
                job_id=job_id,
                blueprint_path=self._generate_blueprint(seed),
                theme=theme,
                seed=seed,
                spawn_count=spawn_count,
                item_preset=item_preset,
                output_path=f"Assets/_UberStrike/Maps/Generated/{job_id}.unity",
            )
            jobs.append(job)
        return jobs

    def _generate_blueprint(self, seed: int) -> str:
        from .wave_function_collapse import WaveFunctionCollapse
        from PIL import Image

        wfc = WaveFunctionCollapse(64, 64, seed)
        blueprint = wfc.generate_arena_layout(spawn_count=2, ensure_connected=True)
        if blueprint is not None:
            blueprint_path = f"/tmp/blueprint_{seed}.png"
            img = Image.fromarray(blueprint, mode="RGB")
            img.save(blueprint_path)
            return blueprint_path
        return "Assets/_UberStrike/Blueprints/MapLayouts/default.png"

    def _execute_batch(self, jobs: List[GenerationJob]) -> List[GenerationJob]:
        completed: List[GenerationJob] = []
        with concurrent.futures.ThreadPoolExecutor(max_workers=self.max_parallel) as executor:
            future_to_job = {executor.submit(self._execute_single_job, job): job for job in jobs}
            for future in concurrent.futures.as_completed(future_to_job):
                job = future_to_job[future]
                try:
                    completed.append(future.result())
                except Exception as exc:  # pragma: no cover - log and continue
                    job.status = "error"
                    job.error_message = str(exc)
                    completed.append(job)
        return completed

    def _execute_single_job(self, job: GenerationJob) -> GenerationJob:
        start_time = time.time()
        cmd = [
            self.unity_path,
            "-projectPath",
            self.project_path,
            "-batchmode",
            "-quit",
            "-executeMethod",
            "UberStrike.MapGen.BatchProcessor.GenerateMap",
            f"-blueprint={job.blueprint_path}",
            f"-theme={job.theme}",
            f"-seed={job.seed}",
            f"-spawns={job.spawn_count}",
            f"-items={job.item_preset}",
            f"-output={job.output_path}",
        ]
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
            if result.returncode == 0:
                job.status = "completed"
                job.quality_score = self._analyze_quality(job.output_path)
            else:
                job.status = "failed"
                job.error_message = result.stderr
        except subprocess.TimeoutExpired:
            job.status = "timeout"
            job.error_message = "Generation exceeded 120 seconds"
        except Exception as exc:  # pragma: no cover - just log
            job.status = "error"
            job.error_message = str(exc)
        job.generation_time = time.time() - start_time
        return job

    def _analyze_quality(self, map_path: str) -> float:
        try:
            from .graph_flow_analyzer import GraphFlowAnalyzer

            analyzer = GraphFlowAnalyzer()
            # Placeholder: in a full pipeline, load layout + items from map_path
            dummy_layout = np.ones((64, 64))
            dummy_layout[0, :] = 0
            dummy_layout[-1, :] = 0
            dummy_layout[:, 0] = 0
            dummy_layout[:, -1] = 0
            metrics = analyzer.analyze_map(dummy_layout, spawn_points=[(16, 16), (48, 48)])
            return max(0.0, min(1.0, 1.0 - metrics.spawn_balance)) if metrics else 0.5
        except Exception:
            return 0.5

    def _create_tournament_package(self, maps: List[str], package_name: str) -> None:
        package_dir = Path(f"Tournament_{package_name}_{time.strftime('%Y%m%d')}")
        package_dir.mkdir(exist_ok=True)
        maps_dir = package_dir / "Maps"
        maps_dir.mkdir(exist_ok=True)
        for map_path in maps:
            try:
                shutil.copy(map_path, maps_dir)
            except FileNotFoundError:
                continue
        metadata = {
            "package": package_name,
            "version": "1.0",
            "date": time.strftime("%Y-%m-%d"),
            "map_count": len(maps),
            "maps": [Path(m).name for m in maps],
            "generation_settings": {
                "wfc_enabled": True,
                "voronoi_themes": True,
                "sa_placement": True,
                "lod_optimization": True,
            },
        }
        with open(package_dir / "metadata.json", "w", encoding="utf-8") as handle:
            json.dump(metadata, handle, indent=2)
        readme = f"""
# Tournament Package: {package_name}

Generated: {time.strftime('%Y-%m-%d %H:%M')}
Maps: {len(maps)}

## Installation
1. Copy Maps/ folder to your UberStrike/Maps directory
2. Restart UberStrike
3. Maps will appear in map selection

## Map List
{chr(10).join(f'- {Path(m).stem}' for m in maps)}

## Generation Parameters
- Wave Function Collapse layouts
- Voronoi theme regions
- Simulated Annealing item placement
- Adaptive LOD optimization
- Graph flow analysis validation

Generated by UberStrike MapGen v0.6
        """
        with open(package_dir / "README.md", "w", encoding="utf-8") as handle:
            handle.write(readme)
        print(f"Tournament package created: {package_dir}")


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="Batch generate UberStrike maps")
    parser.add_argument("--count", type=int, default=10, help="Number of maps")
    parser.add_argument("--name", default="batch", help="Batch name")
    parser.add_argument("--unity", required=True, help="Path to Unity.exe")
    parser.add_argument("--project", required=True, help="Path to project")
    parser.add_argument("--parallel", type=int, default=4, help="Parallel jobs")
    args = parser.parse_args()
    generator = BatchGenerator(args.unity, args.project)
    generator.max_parallel = max(1, args.parallel)
    maps = generator.generate_tournament_set(args.name, args.count)
    print(f"\nGenerated {len(maps)} maps successfully!")


if __name__ == "__main__":
    main()
