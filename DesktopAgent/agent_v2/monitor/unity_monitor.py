"""Unity real-time monitoring utilities for DesktopAgent v2.0.

This module exposes :class:`UnityMapMonitor`, a watchdog-backed monitor that
tracks the Unity editor log and emits structured diagnostics in (near) real
-time.  The implementation intentionally favours readability over absolute
performance – the monitor polls at a configurable cadence while also reacting
 to filesystem change notifications.
"""

from __future__ import annotations

import asyncio
import queue
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional

import yaml
from watchdog.events import FileSystemEventHandler
from watchdog.observers import Observer


@dataclass
class StackIssue:
    """Container describing an issue detected in the Unity log."""

    issue_type: str
    message: str
    severity: str
    line: Optional[str] = None


class _UnityLogEventHandler(FileSystemEventHandler):
    """Minimal watchdog handler that notifies the monitor when the log changes."""

    def __init__(self, log_path: Path, notification_queue: "queue.Queue[None]") -> None:
        super().__init__()
        self._log_path = log_path
        self._queue = notification_queue

    def on_modified(self, event):  # type: ignore[override]
        if Path(event.src_path) == self._log_path:
            self._queue.put_nowait(None)


class UnityMapMonitor:
    """Watches the Unity Editor log and surfaces actionable diagnostics.

    The monitor keeps track of the last processed byte offset and only scans the
    appended portion of the log.  Detected issues are exposed as ``StackIssue``
    instances and can optionally be auto-resolved by downstream tooling.
    """

    ISSUE_PATTERNS: Dict[str, Dict[str, str]] = {
        "FLAT_WALLS": {
            "needle": "localScale = (x, 0.1, x)",
            "severity": "HIGH",
            "summary": "Walls are generated with 0.1m height.",
        },
        "CYAN_PROCESSING": {
            "needle": "Processing pixel: (0, 255, 255)",
            "severity": "MEDIUM",
            "summary": "Cyan border pixels are being processed.",
        },
        "NULL_REF": {
            "needle": "NullReferenceException",
            "severity": "CRITICAL",
            "summary": "Unity reported a NullReferenceException.",
        },
        "MESH_COMBINE_FAIL": {
            "needle": "Mesh.CombineMeshes failed",
            "severity": "HIGH",
            "summary": "Mesh.CombineMeshes failed – geometry may be missing.",
        },
    }

    def __init__(self, config_path: Optional[Path] = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        self.config = self._load_config(self.config_path)
        self.log_path = Path(self.config["unity"]["log_path"]).expanduser()
        self.check_interval = float(self.config.get("monitoring", {}).get("check_interval", 0.5))

        self._last_position = 0
        self._observer: Optional[Observer] = None
        self._notification_queue: "queue.Queue[None]" = queue.Queue()
        self._lock = threading.Lock()

        log_dir = self.log_path.parent
        log_dir.mkdir(parents=True, exist_ok=True)
        if not self.log_path.exists():
            self.log_path.touch()

        self._start_observer()

    # ------------------------------------------------------------------
    # Observer lifecycle helpers
    # ------------------------------------------------------------------
    def _start_observer(self) -> None:
        if self._observer is not None:
            return
        handler = _UnityLogEventHandler(self.log_path, self._notification_queue)
        observer = Observer()
        observer.schedule(handler, str(self.log_path.parent), recursive=False)
        observer.start()
        self._observer = observer

    def _stop_observer(self) -> None:
        if self._observer is None:
            return
        self._observer.stop()
        self._observer.join(timeout=1.0)
        self._observer = None

    # ------------------------------------------------------------------
    async def monitor_generation(self) -> None:
        """Continuously monitor the Unity log for relevant issues."""

        try:
            while True:
                self._drain_notifications()
                issues = self._scan_recent_lines()
                if issues:
                    self._log_issues(issues)
                    if self.config.get("monitoring", {}).get("auto_fix", False):
                        instructions = self.auto_fix(issues)
                        for instruction in instructions:
                            print(f"[AUTO-FIX] {instruction}")
                await asyncio.sleep(self.check_interval)
        except asyncio.CancelledError:
            pass
        finally:
            self._stop_observer()

    def check_once(self) -> List[StackIssue]:
        """Scan the log a single time and return any detected issues."""

        issues = self._scan_recent_lines()
        self._log_issues(issues)
        return issues

    # ------------------------------------------------------------------
    def _drain_notifications(self) -> None:
        while True:
            try:
                self._notification_queue.get_nowait()
            except queue.Empty:
                break

    # ------------------------------------------------------------------
    def _scan_recent_lines(self) -> List[StackIssue]:
        """Return issues found in the newly appended portion of the log."""

        issues: List[StackIssue] = []
        try:
            with self._lock:
                with self.log_path.open("r", encoding="utf-8", errors="ignore") as handle:
                    handle.seek(self._last_position)
                    new_lines = handle.readlines()
                    self._last_position = handle.tell()
        except FileNotFoundError:
            return issues

        for line in new_lines:
            detected = self._classify_line(line.rstrip())
            if detected:
                issues.append(detected)
        return issues

    def _classify_line(self, line: str) -> Optional[StackIssue]:
        for issue_type, pattern in self.ISSUE_PATTERNS.items():
            if pattern["needle"] in line:
                return StackIssue(
                    issue_type=issue_type,
                    message=pattern["summary"],
                    severity=pattern["severity"],
                    line=line,
                )
        return None

    # ------------------------------------------------------------------
    def auto_fix(self, issues: Iterable[StackIssue]) -> List[str]:
        """Return suggested fixes for the supplied issues.

        The monitor does not directly mutate project files – instead it emits
        actionable instructions that can be consumed by :class:`AutoFixer` or by
        a human operator.
        """

        suggestions: List[str] = []
        for issue in issues:
            if issue.issue_type == "FLAT_WALLS":
                suggestions.append(
                    "Walls are flat; ensure wall localScale.y is set to 4.0f and positions are offset by Vector3.up * 2.0f."
                )
            elif issue.issue_type == "CYAN_PROCESSING":
                suggestions.append(
                    "Cyan pixels detected; add a guard that skips pixels where r < 0.1f, g > 0.9f, b > 0.9f."
                )
            elif issue.issue_type == "NULL_REF":
                suggestions.append("Unity threw a NullReferenceException – inspect the stack trace and guard any null usages.")
            elif issue.issue_type == "MESH_COMBINE_FAIL":
                suggestions.append("Mesh.CombineMeshes failed; verify meshes are readable and the combine list is non-empty.")
        return suggestions

    # ------------------------------------------------------------------
    def _log_issues(self, issues: Iterable[StackIssue]) -> None:
        notify = self.config.get("monitoring", {}).get("notify_on_fix", True)
        for issue in issues:
            prefix = "[UNITY-MONITOR]"
            print(f"{prefix} {issue.severity}: {issue.message}")
            if issue.line and notify:
                print(f"{prefix}  ↳ {issue.line}")

    # ------------------------------------------------------------------
    @staticmethod
    def _load_config(path: Path) -> Dict[str, Dict[str, object]]:
        if not path.exists():
            return {
                "unity": {
                    "log_path": str(Path.home() / "Editor.log"),
                },
                "monitoring": {"check_interval": 0.5, "auto_fix": False, "notify_on_fix": True},
            }
        with path.open("r", encoding="utf-8") as handle:
            return yaml.safe_load(handle)  # type: ignore[return-value]


__all__ = ["UnityMapMonitor", "StackIssue"]
