"""Interactive command-line dashboard for DesktopAgent v2.0."""

from __future__ import annotations

import os
import select
import sys
import time
from dataclasses import dataclass, field
from typing import List

from rich import box
from rich.console import Console
from rich.live import Live
from rich.table import Table

from ..fixer.auto_fixer import AutoFixer
from ..monitor.unity_monitor import UnityMapMonitor


@dataclass
class DashboardState:
    current_map: str = "Unknown"
    wall_count: int = 0
    wall_height_ok: bool = True
    spawn_count: int = 0
    spawn_balance_ok: bool = True
    light_count: int = 0
    issues: List[str] = field(default_factory=list)


class InteractiveCLI:
    """Terminal dashboard with hotkeys to control the DesktopAgent."""

    REFRESH_INTERVAL = 0.5

    def __init__(self) -> None:
        self.console = Console()
        self.monitor = UnityMapMonitor()
        self.fixer = AutoFixer()
        self.state = DashboardState()
        self._should_exit = False

    # ------------------------------------------------------------------
    def run(self) -> None:
        """Start the interactive dashboard loop."""

        with Live(self._render_table(), console=self.console, refresh_per_second=int(1 / self.REFRESH_INTERVAL)) as live:
            while not self._should_exit:
                self._refresh_state()
                live.update(self._render_table())
                command = self._read_command()
                if command:
                    self._handle_command(command)
                time.sleep(self.REFRESH_INTERVAL)

    # ------------------------------------------------------------------
    def _refresh_state(self) -> None:
        issues = [issue.issue_type for issue in self.monitor.check_once()]
        self.state.issues = issues
        self.state.wall_height_ok = "FLAT_WALLS" not in issues
        self.state.spawn_balance_ok = "MISSING_SPAWNS" not in issues
        self.state.wall_count += 0  # placeholder for future integration
        self.state.spawn_count += 0
        self.state.light_count += 0

    def _render_table(self) -> Table:
        table = Table(title="🎮 UberStrike DesktopAgent", box=box.SQUARE)
        table.add_column("Metric", style="cyan", justify="left")
        table.add_column("Value", style="green")
        table.add_column("Status", style="magenta")

        table.add_row("Current Map", self.state.current_map, "–")
        table.add_row("Walls", str(self.state.wall_count), "OK" if self.state.wall_height_ok else "Check height")
        table.add_row("Spawns", str(self.state.spawn_count), "OK" if self.state.spawn_balance_ok else "Rebalance")
        table.add_row("Lights", str(self.state.light_count), "–")
        table.add_row("Issues", ", ".join(self.state.issues) if self.state.issues else "None", "Attention" if self.state.issues else "All clear")

        table.add_section()
        table.add_row("Hotkeys", "F=Fix • A=Analyze • G=Generate • R=Rebuild • Q=Quit", "")
        return table

    # ------------------------------------------------------------------
    def _read_command(self) -> str:
        if os.name == "nt":
            import msvcrt

            if msvcrt.kbhit():
                return msvcrt.getwch().lower()
            return ""
        else:
            dr, _, _ = select.select([sys.stdin], [], [], 0.0)
            if dr:
                return sys.stdin.read(1).lower()
            return ""

    def _handle_command(self, command: str) -> None:
        if command == "q":
            self._should_exit = True
        elif command == "f":
            results = self.fixer.fix_all(self.state.issues)
            for result in results:
                status = "✓" if result.applied else "✗"
                self.console.print(f"{status} {result.issue_type}: {result.details}")
        elif command == "a":
            from ..analyzer.quality_analyzer import MapQualityAnalyzer

            analyzer = MapQualityAnalyzer()
            report = analyzer.analyze_map(self.state.current_map or "ArenaStack_Sample")
            self.console.print(f"Analysis score: {report['score']}/100")
        elif command == "g":
            from ..generator.layer_generator import AILayerGenerator

            generator = AILayerGenerator()
            stack = generator.generate_from_prompt("Symmetrical arena with central courtyard")
            self.console.print(f"Generated stack: {stack}")
        elif command == "r":
            self.console.print("Rebuild requested – trigger Unity build pipeline manually.")


__all__ = ["InteractiveCLI"]
