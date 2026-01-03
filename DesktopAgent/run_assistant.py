"""DesktopAgent v2.0 entry point."""

from __future__ import annotations

import asyncio

import click

from agent_v2.monitor.unity_monitor import UnityMapMonitor
from agent_v2.fixer.auto_fixer import AutoFixer
from agent_v2.cli.assistant_cli import InteractiveCLI


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
    """Auto-fix detected issues."""

    fixer = AutoFixer()
    issues = fixer.detect_issues()
    results = fixer.fix_all(issues)
    for result in results:
        status = "✓" if result.applied else "✗"
        click.echo(f"{status} {result.issue_type}: {result.details}")


@cli.command()
def interactive() -> None:
    """Launch interactive dashboard."""

    dashboard = InteractiveCLI()
    dashboard.run()


if __name__ == "__main__":
    cli()
