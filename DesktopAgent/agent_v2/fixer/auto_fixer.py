"""Automated remediation helpers for DesktopAgent v2.0.

The :class:`AutoFixer` class inspects Unity project artifacts, applies
well-known fixes and verifies their effect.  The implementation intentionally
keeps heuristics simple – it prefers deterministic, transparent edits that can
be reviewed easily.
"""

from __future__ import annotations

import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional

import yaml


@dataclass
class FixResult:
    issue_type: str
    applied: bool
    details: str


class AutoFixer:
    """Applies opinionated fixes for well-known blueprint issues."""

    FIX_REGISTRY: Dict[str, str] = {
        "FLAT_WALLS": "Replace wall localScale.y with 4.0f and offset by Vector3.up * 2.0f.",
        "CYAN_PROCESSING": "Guard cyan pixels before processing gameplay logic.",
        "MISSING_SPAWNS": "Inject balanced spawn points into the flow layer.",
        "POOR_LIGHTING": "Add additional lighting markers to the lighting layer.",
    }

    def __init__(self, config_path: Optional[Path] = None) -> None:
        self.config_path = config_path or Path(__file__).resolve().parents[2] / "config.yaml"
        self.config = self._load_config(self.config_path)
        self.project_path = Path(self.config["unity"]["project_path"]).expanduser()
        self.blueprints_path = Path(self.config["unity"]["blueprints_path"]).expanduser()

    # ------------------------------------------------------------------
    def detect_issues(self) -> List[str]:
        """Heuristically detect outstanding issues using the Unity log."""

        log_path = Path(self.config["unity"]["log_path"]).expanduser()
        if not log_path.exists():
            return []

        issues: List[str] = []
        with log_path.open("r", encoding="utf-8", errors="ignore") as handle:
            tail = handle.read()[-4096:]
        if "localScale = (x, 0.1, x)" in tail or "localScale = new Vector3(" in tail and "0.1f" in tail:
            issues.append("FLAT_WALLS")
        if "Processing pixel: (0, 255, 255)" in tail:
            issues.append("CYAN_PROCESSING")
        if "Spawn count" in tail and "0" in tail:
            issues.append("MISSING_SPAWNS")
        if "Lighting pass" in tail and "insufficient" in tail.lower():
            issues.append("POOR_LIGHTING")
        return issues

    # ------------------------------------------------------------------
    def fix_all(self, issues: Iterable[str]) -> List[FixResult]:
        results: List[FixResult] = []
        for issue in issues:
            applied, details = False, "No handler registered."
            if issue in {"FLAT_WALLS", "CYAN_PROCESSING"}:
                target = self.project_path / "UberStrikeGen/Assets/_UberStrike/Scripts/Editor/BuildFromBlueprint.cs"
                applied = self.inject_unity_fix(issue, target)
                details = "Patched BuildFromBlueprint.cs" if applied else "No matching pattern found."
            elif issue == "MISSING_SPAWNS":
                applied = self._augment_flow_layer()
                details = "Added spawn markers to flow layer" if applied else "No writable flow layer found."
            elif issue == "POOR_LIGHTING":
                applied = self._augment_lighting_layer()
                details = "Added additional lights" if applied else "No lighting layer found."
            results.append(FixResult(issue_type=issue, applied=applied, details=details))
        return results

    # ------------------------------------------------------------------
    def inject_unity_fix(self, issue_type: str, target_file: Path) -> bool:
        """Apply a direct text patch to a Unity C# file."""

        if not target_file.exists():
            return False

        backup = target_file.with_suffix(target_file.suffix + ".bak")
        shutil.copy2(target_file, backup)

        original = target_file.read_text(encoding="utf-8")
        patched = original

        if issue_type == "FLAT_WALLS":
            patched = re.sub(
                r"localScale\s*=\s*new Vector3\(([^,]+),\s*0\.1f,\s*([^\)]+)\)",
                r"localScale = new Vector3(\1, 4.0f, \2)",
                patched,
            )
            patched = re.sub(
                r"transform\.position\s*=\s*([^;]+)",
                r"transform.position = \1 + Vector3.up * 2.0f",
                patched,
                count=1,
            )
        elif issue_type == "CYAN_PROCESSING":
            guard = "if (pixel.r < 0.1f && pixel.g > 0.9f && pixel.b > 0.9f) continue;"
            if guard not in patched:
                patched = patched.replace("for (int y = 0; y < height; y++)\n                {", f"for (int y = 0; y < height; y++)\n                {{\n                    {guard}\n")

        if patched == original:
            return False

        target_file.write_text(patched, encoding="utf-8")
        self._touch_assets()
        return self.validate_fix(issue_type, target_file)

    # ------------------------------------------------------------------
    def validate_fix(self, issue_type: str, target_file: Path) -> bool:
        """Validate that a previous patch removed the offending pattern."""

        content = target_file.read_text(encoding="utf-8")
        if issue_type == "FLAT_WALLS":
            return "0.1f" not in content or "localScale" not in content
        if issue_type == "CYAN_PROCESSING":
            return "0, 255, 255" not in content or "continue;" in content
        return True

    # ------------------------------------------------------------------
    def _augment_flow_layer(self) -> bool:
        flow_layers = sorted(self.blueprints_path.glob("**/*.flow.png"))
        if not flow_layers:
            return False
        flow_layer = flow_layers[0]
        from PIL import Image

        image = Image.open(flow_layer).convert("RGB")
        width, height = image.size
        pixels = image.load()
        spawn_colors = [(255, 0, 0), (0, 255, 0), (255, 255, 0)]
        locations = [
            (width // 4, height // 4),
            (3 * width // 4, height // 4),
            (width // 2, 3 * height // 4),
        ]
        for idx, loc in enumerate(locations):
            pixels[loc] = spawn_colors[idx % len(spawn_colors)]
        image.save(flow_layer)
        return True

    def _augment_lighting_layer(self) -> bool:
        lighting_layers = sorted(self.blueprints_path.glob("**/*.lighting.png"))
        if not lighting_layers:
            return False
        lighting_layer = lighting_layers[0]
        from PIL import Image

        image = Image.open(lighting_layer).convert("RGB")
        width, height = image.size
        pixels = image.load()
        anchors = [
            (width // 3, height // 3),
            (2 * width // 3, height // 3),
            (width // 2, 2 * height // 3),
        ]
        for x, y in anchors:
            pixels[x, y] = (255, 255, 255)
        image.save(lighting_layer)
        return True

    # ------------------------------------------------------------------
    def _touch_assets(self) -> None:
        assets_dir = self.project_path / "UberStrikeGen/Assets"
        sentinel = assets_dir / ".desktop_agent_touch"
        sentinel.write_text("trigger reimport", encoding="utf-8")

    # ------------------------------------------------------------------
    @staticmethod
    def _load_config(path: Path) -> Dict[str, Dict[str, object]]:
        if not path.exists():
            return {
                "unity": {
                    "project_path": str(Path.cwd()),
                    "blueprints_path": str(Path.cwd() / "UberStrikeGen/Assets/_UberStrike/Blueprints"),
                    "log_path": str(Path.cwd() / "Editor.log"),
                }
            }
        with path.open("r", encoding="utf-8") as handle:
            return yaml.safe_load(handle)  # type: ignore[return-value]


__all__ = ["AutoFixer", "FixResult"]
