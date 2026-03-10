"""DesktopAgent v2.0 entry point."""

from __future__ import annotations

import asyncio
import collections
import json
import logging
import shutil
import sys
import os
import platform
import time
from pathlib import Path
from typing import Optional

import click

from agent_v2.monitor.unity_monitor import UnityMapMonitor
from agent_v2.fixer.auto_fixer import AutoFixer
from agent_v2.cli.assistant_cli import InteractiveCLI
from agent_v2.blueprints.stack_io import BlueprintStack
from agent_v2.analyzer.quality_analyzer import MapQualityAnalyzer
from agent_v2.fixer.blueprint_sanitizer import BlueprintSanitizer
from agent_v2.mutator.blueprint_mutator import BlueprintMutator

# Configure logging
logging.basicConfig(level=logging.INFO, format="%(message)s")
logger = logging.getLogger("DesktopAgent")


@click.group()
def cli():
    """DesktopAgent v2.0 command group."""


@cli.command()
@click.option("--real-time", is_flag=True, help="Run monitor continuously")
def monitor(real_time: bool) -> None:
    """Monitor Unity map generation."""

    monitor = UnityMapMonitor()
    if real_time:
        asyncio.run(monitor.monitor_generation())
    else:
        issues = monitor.check_once()
        for issue in issues:
            click.echo(f"{issue.severity}: {issue.message}")


@cli.command()
@click.argument("prompt")
def generate(prompt: str) -> None:
    """Generate map from text prompt."""

    from agent_v2.generator.layer_generator import AILayerGenerator

    generator = AILayerGenerator()
    result = generator.generate_from_prompt(prompt)
    click.echo(f"Generated: {result}")


@cli.command()
def fix() -> None:
    """Auto-fix detected issues (LEGACY Post-Build)."""

    fixer = AutoFixer()
    issues = fixer.detect_issues()
    results = fixer.fix_all(issues)
    for result in results:
        status = "✓" if result.applied else "✗"
        click.echo(f"{status} {result.issue_type}: {result.details}")


@cli.command()
@click.option("--stack", required=True, help="Path to stack.json map blueprint")
@click.option("--out-dir", required=True, help="Output directory for generated map")
@click.option("--theme", default=None, help="Path to ThemeDefinition asset")
@click.option("--ubervocab", default=None, help="Path to ubervocab.json")
@click.option("--seed", default=None, type=int, help="Random seed")
@click.option("--use-ps1", is_flag=True, help="Use PowerShell script instead of Batch (Windows only)")
@click.option("--unity-log", default=None, help="Path to save Unity log")
@click.option("--open", "open_folder", is_flag=True, help="Open output folder on success")
# New Flags
@click.option("--analyze/--no-analyze", default=True, help="Run QC analysis before build")
@click.option("--auto-fix", is_flag=True, help="Automatically fix issues found by analysis")
@click.option("--write-fixed-to", default=None, help="Save fixed blueprint to specific path (default: temp)")
@click.option("--bundle", is_flag=True, help="Build AssetBundle for Runtime")
@click.option("--target", default="StandaloneWindows64", help="Build Target (default: StandaloneWindows64)")
def build(stack: str, out_dir: str, theme: Optional[str], ubervocab: Optional[str], seed: Optional[int], 
          use_ps1: bool, unity_log: Optional[str], open_folder: bool,
          analyze: bool, auto_fix: bool, write_fixed_to: Optional[str],
          bundle: bool, target: str) -> None:
    """Run Unity Headless Builder to generate a map."""
    
    stack_path = Path(stack).resolve()
    if not stack_path.exists():
        click.secho(f"Error: Stack not found at {stack_path}", fg="red")
        sys.exit(1)

    # 1. Load Blueprint
    try:
        blueprint = BlueprintStack.load(stack_path)
    except Exception as e:
        click.secho(f"Error loading stack: {e}", fg="red")
        sys.exit(1)

    # 2. Analyze & Fix
    modified = False
    if analyze:
        click.secho("\n🔍 Running Blueprint Analysis...", fg="cyan")
        analyzer = MapQualityAnalyzer()
        report = analyzer.analyze(blueprint)
        
        # Display Report
        color = "green" if report.status == "pass" else "yellow" if report.status == "warn" else "red"
        click.secho(f"  Status: {report.status.upper()} (Score: {report.score})", fg=color, bold=True)
        
        if report.reasons:
            click.echo("  Issues:")
            for reason in report.reasons:
                click.secho(f"    - {reason}", fg="yellow")
        
        # Auto-Fix Logic
        if report.status == "fail" and auto_fix:
            click.secho("\n🔧 Attempting Auto-Fix...", fg="cyan")
            sanitizer = BlueprintSanitizer()
            modified = sanitizer.sanitize(blueprint, report.to_dict())
            
            if modified:
                click.echo("  Applied fixes. Re-analyzing...")
                report = analyzer.analyze(blueprint)
                color = "green" if report.status == "pass" else "yellow" if report.status == "warn" else "red"
                click.secho(f"  New Status: {report.status.upper()} (Score: {report.score})", fg=color, bold=True)
            else:
                click.echo("  No fixes available.")

        # Final Halt Check
        if report.status == "fail":
            click.secho("\n❌ Build Aborted: Blueprint failed QC.", fg="red")
            sys.exit(1)
            
        # Save Fixed Blueprint (if modified and needed)
        # Note: If we fixed it in memory, we MUST save it to disk for Unity to read it.
    
    # 3. Build Logic (with potentially updated stack)
    from agent_v2.builder.headless_pipeline import HeadlessPipeline
    
    # Only save sanitized blueprint if we actually modified it
    if modified:
        if write_fixed_to:
             target_dir = Path(write_fixed_to)
        else:
             target_dir = Path(out_dir) / "_temp_build_staging"
             
        saved_path = blueprint.save(target_dir, "sanitized", relative_paths=True)
        stack_path_to_use = str(saved_path)
        click.echo(f"  Using sanitized blueprint: {stack_path_to_use}")
    else:
        stack_path_to_use = str(stack_path)

    
    click.echo(f"\n🚀 Sending to Unity... (Stack: {Path(stack_path_to_use).name})")
    
    pipeline = HeadlessPipeline()
    result = pipeline.run_build(
        stack_path=stack_path_to_use,
        out_dir=out_dir,
        theme_asset=theme,
        ubervocab=ubervocab,
        seed=seed,
        use_ps1=use_ps1,
        unity_log=unity_log,
        bundle=bundle,
        bundle_target=target
    )
    
    status = result.get("status", "fail")
    
    # Output formatting (Stats/Scene)
    click.echo("\n" + "="*40)
    if status == "pass":
         click.secho(f" STATUS: {status.upper()}", fg="green", bold=True)
    elif status == "warn":
         click.secho(f" STATUS: {status.upper()}", fg="yellow", bold=True)
    else:
         click.secho(f" STATUS: {status.upper()}", fg="red", bold=True)
         
    if "scenePath" in result:
        click.echo(f" Scene:  {result['scenePath']}")
    if "bundlePath" in result:
        click.secho(f" Bundle: {result['bundlePath']}", fg="cyan")
        
    click.echo("-" * 40)
    
    # Stats
    if "walls" in result:
        click.echo(f" Quads:   W:{result.get('walls')} F:{result.get('floors')} H2O:{result.get('water')} G:{result.get('glass')}")
    if "spawns" in result:
        click.echo(f" Items:   Spawns:{result.get('spawns')} TPs:{result.get('teleporters')} JPs:{result.get('jumpPads')} Pickups:{result.get('pickups')}")
    
    click.echo("="*40 + "\n")
    
    if open_folder and status != "fail" and platform.system() == "Windows":
        folder_to_open = result.get("resolved_out_dir", out_dir)
        os.startfile(folder_to_open)
        
    # Cleanup temp check could go here
        
    if status == "pass": sys.exit(0)
    elif status == "warn": sys.exit(2)
    else: sys.exit(1)


@cli.command()
@click.option("--stack", required=True, help="Path to stack.json map blueprint")
@click.option("--out-dir", default=None, help="Output directory (default: stack parent dir)")
@click.option("--count", default=3, help="Number of variants to generate")
def mutate(stack: str, out_dir: Optional[str], count: int) -> None:
    """Generate variants of a map blueprint."""
    
    stack_path = Path(stack).resolve()
    if not stack_path.exists():
        click.secho(f"Error: Stack not found at {stack_path}", fg="red")
        sys.exit(1)
        
    target_dir = Path(out_dir) if out_dir else stack_path.parent
    
    try:
        blueprint = BlueprintStack.load(stack_path)
        mutator = BlueprintMutator()
        
        click.echo(f"Generating {count} variants for {stack_path.name}...")
        paths = mutator.mutate_stack(blueprint, count, target_dir)
        
        click.secho(f"✓ Created {len(paths)} variants in {target_dir}", fg="green")
        for p in paths:
             click.echo(f"  - {p.parent.name}/{p.name}")
             
    except Exception as e:
        click.secho(f"Error mutating stack: {e}", fg="red")
        sys.exit(1)


@cli.command()
@click.option("--stack", required=True, help="Path to stack.json map blueprint")
@click.option("--out", "out_dir", required=True, help="Output dataset root directory")
@click.option("--variants", default=50, help="Number of variants to generate")
@click.option("--auto-fix", is_flag=True, help="Auto-fix source stack before mutation")
@click.option("--seed", default=42, help="Random seed")
def export_dataset(stack: str, out_dir: str, variants: int, auto_fix: bool, seed: int) -> None:
    """Export a Unity-free ML training dataset."""
    
    from agent_v2.dataset.exporter import DatasetExporter
    
    stack_path = Path(stack).resolve()
    base_dir = Path(out_dir).resolve()
    dataset_name = base_dir.name
    
    if not stack_path.exists():
        click.secho(f"Error: Stack not found at {stack_path}", fg="red")
        sys.exit(1)
        
    click.echo(f"Starting Dataset Export: {dataset_name}")
    click.echo(f" Source: {stack_path.name}")
    click.echo(f" Variants: {variants}")
    click.echo(f" Auto-Fix: {auto_fix}")
    
    exporter = DatasetExporter(base_dir=base_dir.parent)
    # Exporter expects base_dir to be the parent of the dataset folder based on our implementation logic?
    # Actually logic was: ds_root = self.base_dir / dataset_name
    # So if user passes "datasets/my_ds_v1", base should be "datasets", name "my_ds_v1"
    
    try:
        exporter.export_dataset(
            stack_path=stack_path,
            dataset_name=dataset_name,
            variants=variants,
            auto_fix=auto_fix,
            seed=seed
        )
    except Exception as e:
         click.secho(f"Export failed: {e}", fg="red")
         logger.exception(e)
         sys.exit(1)


@cli.command()
@click.option("--stack", required=True, help="Path to stack.json map blueprint")
def score(stack: str) -> None:
    """Compute and print QC and Features for a stack without building."""
    
    from agent_v2.dataset.feature_extractors import generate_masks, calculate_features
    
    stack_path = Path(stack).resolve()
    if not stack_path.exists():
        click.secho(f"Error: Stack not found at {stack_path}", fg="red")
        sys.exit(1)
        
    try:
        blueprint = BlueprintStack.load(stack_path)
        analyzer = MapQualityAnalyzer()
        report = analyzer.analyze(blueprint)
        
        masks = generate_masks(blueprint)
        features = calculate_features(masks)
        
        click.secho("\n=== Blueprint Analysis ===", bold=True)
        color = "green" if report.status == "pass" else "yellow" if report.status == "warn" else "red"
        click.secho(f"QC Status: {report.status.upper()} (Score: {report.score})", fg=color)
        if report.reasons:
            for r in report.reasons:
                click.echo(f" - {r}")
                
        click.secho("\n=== Features ===", bold=True)
        for k, v in features.items():
            click.echo(f" {k:<20}: {v:.4f}")
            
    except Exception as e:
        click.secho(f"Score failed: {e}", fg="red")
        sys.exit(1)


@cli.command()
@click.option("--dataset-dir", required=True, help="Path to dataset root (containing samples/)")
@click.option("--out", "out_model", default="quality_model.pkl", help="Output path for model")
def train_predictor(dataset_dir: str, out_model: str) -> None:
    """Train a ML model to predict map quality."""
    
    from agent_v2.ml.trainer import QualityModelTrainer
    
    ds_path = Path(dataset_dir).resolve()
    model_path = Path(out_model).resolve()
    
    if not ds_path.exists():
        click.secho(f"Error: Dataset not found at {ds_path}", fg="red")
        sys.exit(1)
        
    trainer = QualityModelTrainer(ds_path)
    try:
        metrics = trainer.train(model_path)
        click.secho(f"Success! Model saved to {model_path}", fg="green")
        click.echo(f"Train Accuracy: {metrics['train_acc']:.2%}")
        click.echo(f"Test Accuracy:  {metrics['test_acc']:.2%}")
    except Exception as e:
        click.secho(f"Training failed: {e}", fg="red")
        sys.exit(1)


@cli.command()
@click.option("--stack", required=True, help="Path to stack.json map blueprint")
@click.option("--model", default="quality_model.pkl", help="Path to trained model")
def predict(stack: str, model: str) -> None:
    """Predict map quality using ML model."""
    
    from agent_v2.dataset.feature_extractors import generate_masks, calculate_features
    from agent_v2.ml.predictor import QualityPredictor
    
    stack_path = Path(stack).resolve()
    model_path = Path(model).resolve()
    
    if not stack_path.exists():
        click.secho(f"Error: Stack not found at {stack_path}", fg="red")
        sys.exit(1)
    if not model_path.exists():
        click.secho(f"Error: Model not found at {model_path}. Train one first!", fg="red")
        sys.exit(1)
        
    try:
        # Load features
        blueprint = BlueprintStack.load(stack_path)
        masks = generate_masks(blueprint)
        features = calculate_features(masks)
        
        # Predict
        predictor = QualityPredictor(model_path)
        result = predictor.predict(features)
        
        status = result["prediction"]
        conf = result["confidence"]
        
        color = "green" if status == "PASS" else "red"
        click.echo("ML Quality Prediction:")
        click.secho(f"  {status} ({conf:.1%})", fg=color, bold=True)
        click.echo(f"  Pass Prob: {result['pass_probability']:.3f}")
        
    except Exception as e:
         click.secho(f"Prediction failed: {e}", fg="red")
         sys.exit(1)


def find_repo_root(start: Path | None = None) -> Path:
    """Find repository root by looking for .git or MapGen_Project"""
    cur = (start or Path.cwd()).resolve()
    for _ in range(12):
        if (cur / ".git").exists() or (cur / "MapGen_Project").exists():
            return cur
        if cur.parent == cur:
            break
        cur = cur.parent
    return (start or Path.cwd()).resolve()


def _backup_if_exists(dst: Path) -> None:
    """Create timestamped backup of existing file"""
    if dst.exists():
        stamp = time.strftime("%Y%m%d_%H%M%S")
        backup = dst.with_suffix(dst.suffix + f".bak_{stamp}")
        shutil.copy2(dst, backup)
        click.echo(f"  Backed up: {backup.name}")


def _resolve_bundle_from_report(report_path: Path) -> tuple[Path | None, str]:
    """Resolve bundle path from build report.
    
    Returns: (bundle_path, resolution_method)
    """
    data = json.loads(report_path.read_text(encoding="utf-8"))
    
    # Prefer absolute path (bundleDiskPath)
    disk = data.get("bundleDiskPath")
    if disk:
        p = Path(disk)
        if p.exists():
            return p, "bundleDiskPath"
    
    # Fallback: Unity-relative path (bundlePath like Assets/...)
    rel = data.get("bundlePath")
    if rel:
        # Resolve relative to MapGen_Project root
        repo_root = find_repo_root(report_path.parent)
        p = (repo_root / "MapGen_Project" / rel).resolve()
        if p.exists():
            return p, "bundlePath+repo_root"
    
    return None, "not_found"


@cli.command()
@click.option("--bundle", "bundle_path", default=None, help="Path to bundle file (overrides report)")
@click.option("--report", "report_path", default=None, help="Path to build_report.json")
@click.option("--target-dir", required=True, help="Game data directory to deploy to")
@click.option("--backup/--no-backup", default=True, help="Backup existing files before overwrite")
def deploy(bundle_path: str | None, report_path: str | None, target_dir: str, backup: bool) -> None:
    """Deploy generated AssetBundle to game data directory."""
    tgt = Path(target_dir).resolve()
    tgt.mkdir(parents=True, exist_ok=True)

    src_bundle: Path | None = None
    reason = ""

    if bundle_path:
        p = Path(bundle_path).resolve()
        if not p.exists():
            raise click.ClickException(f"--bundle not found: {p}")
        src_bundle, reason = p, "--bundle"
    elif report_path:
        rp = Path(report_path).resolve()
        if not rp.exists():
            raise click.ClickException(f"--report not found: {rp}")
        src_bundle, reason = _resolve_bundle_from_report(rp)
        if not src_bundle:
            raise click.ClickException(f"Could not resolve bundle from report ({reason}). Provide --bundle.")
    else:
        raise click.ClickException("Provide either --bundle or --report.")

    # Manifest is optional
    src_manifest = src_bundle.parent / (src_bundle.name + ".manifest")
    dst_bundle = tgt / src_bundle.name
    dst_manifest = tgt / src_manifest.name

    click.echo(f"Deploy source: {src_bundle} ({reason})")
    click.echo(f"Deploy target: {tgt}")

    if backup:
        _backup_if_exists(dst_bundle)
        if src_manifest.exists():
            _backup_if_exists(dst_manifest)

    shutil.copy2(src_bundle, dst_bundle)
    if src_manifest.exists():
        shutil.copy2(src_manifest, dst_manifest)

    click.secho("✓ Deploy complete", fg="green")
    click.echo(f"  Bundle:    {dst_bundle}")
    if src_manifest.exists():
        click.echo(f"  Manifest:  {dst_manifest}")


@cli.command()
def interactive() -> None:
    """Launch interactive dashboard."""

    dashboard = InteractiveCLI()
    dashboard.run()


if __name__ == "__main__":
    cli()
