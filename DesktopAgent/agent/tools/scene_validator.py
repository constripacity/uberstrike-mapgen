"""
Scene validation utilities for Unity .unity scene files.

Provides a simple, heuristic `validate_scene(scene_path)` function that:
- Parses YAML-style Unity scene files
- Detects spawn points (names containing "spawn")
- Checks for navmesh presence (NavMeshSurface / navmesh markers)
- Counts colliders (BoxCollider, SphereCollider, CapsuleCollider, MeshCollider)
- Estimates playable area (bounding box from m_LocalPosition x/z values)
- Returns a quality score and issues list

This is intentionally heuristic (Unity scene files can be complex/binary). It works well
for text-based YAML scenes produced by Unity in source control / headless runs.
"""

from typing import Dict, Any, List
import re
import math
import os
import json

SPAWN_REGEX = re.compile(r"m_Name:\s*(?P<name>.*[sS]pawn.*)")
POSITION_INLINE_REGEX = re.compile(
    r"m_LocalPosition:\s*\{\s*x:\s*(?P<x>-?\d+(?:\.\d+)?),\s*y:\s*(?P<y>-?\d+(?:\.\d+)?),\s*z:\s*(?P<z>-?\d+(?:\.\d+)?).*\}"
)
POSITION_BLOCK_REGEX = re.compile(
    r"m_LocalPosition:\s*\n(?:\s+)[xyz]:\s*(?P<val>-?\d+(?:\.\d+)?)", re.IGNORECASE
)
NAVMESH_PATTERNS = [re.compile(r"NavMeshSurface", re.IGNORECASE), re.compile(r"navmesh", re.IGNORECASE)]
COLLIDER_TYPES = ["BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider", "WheelCollider"]

def _read_text(scene_path: str) -> str:
    with open(scene_path, "r", encoding="utf-8", errors="ignore") as f:
        return f.read()

def _find_spawn_points(text: str) -> List[str]:
    return [m.group("name").strip() for m in SPAWN_REGEX.finditer(text)]

def _has_navmesh(text: str) -> bool:
    for pat in NAVMESH_PATTERNS:
        if pat.search(text):
            return True
    return False

def _count_colliders(text: str) -> int:
    count = 0
    for c in COLLIDER_TYPES:
        count += len(re.findall(r"\b" + re.escape(c) + r"\b", text))
    return count

def _estimate_playable_area(scene_text: str) -> float:
    # Try inline positions first
    xs = []
    zs = []

    for m in POSITION_INLINE_REGEX.finditer(scene_text):
        try:
            x = float(m.group("x"))
            z = float(m.group("z"))
            xs.append(x)
            zs.append(z)
        except Exception:
            continue

    # Fallback: attempt to parse block-style positions by scanning file for "m_LocalPosition:"
    if not xs:
        # naive block parse: find occurrences and look nearby lines
        lines = scene_text.splitlines()
        for i, line in enumerate(lines):
            if "m_LocalPosition:" in line:
                # inspect next few lines for x, y, z
                maybe_x = None
                maybe_z = None
                for j in range(i+1, min(i+6, len(lines))):
                    lx = lines[j].strip()
                    if lx.startswith("x:") or lx.startswith("m_X:") or lx.startswith("m_X"):
                        try:
                            maybe_x = float(lx.split(":")[1].strip())
                        except Exception:
                            pass
                    if lx.startswith("z:") or lx.startswith("m_Z:") or lx.startswith("m_Z"):
                        try:
                            maybe_z = float(lx.split(":")[1].strip())
                        except Exception:
                            pass
                if maybe_x is not None and maybe_z is not None:
                    xs.append(maybe_x)
                    zs.append(maybe_z)

    if not xs or not zs:
        return 0.0

    min_x, max_x = min(xs), max(xs)
    min_z, max_z = min(zs), max(zs)
    width = max_x - min_x
    depth = max_z - min_z
    # if width or depth are tiny (e.g., 0), try to treat area as small positive
    width = max(width, 0.0)
    depth = max(depth, 0.0)
    area = abs(width * depth)
    # Convert to reasonable units if values look like meters already — keep as-is
    return area

def _gather_issues(spawns: List[str], navmesh: bool, colliders: int) -> List[str]:
    issues = []
    if not spawns:
        issues.append("No spawn points found")
    elif len(spawns) < 4:
        issues.append("Too few spawn points (<4) for balanced play")
    if not navmesh:
        issues.append("Navmesh not detected")
    if colliders == 0:
        issues.append("No colliders detected")
    return issues

def _compute_quality(spawns: int, navmesh: bool, area: float, colliders: int) -> float:
    # Heuristic scoring:
    # - spawn score: up to 4 points (0-4) mapped from number of spawns (ideal ~8)
    spawn_score = min(4.0, (spawns / 8.0) * 4.0)
    # - navmesh: 2 points if present
    nav_score = 2.0 if navmesh else 0.0
    # - area: up to 2 points for reasonable area (normalized)
    # assume 250 sqm is ideal, scale accordingly
    area_score = max(0.0, min(2.0, (area / 250.0) * 2.0))
    # - colliders: up to 2 points if there are colliders (presence matters), penalize too many (>100)
    if colliders == 0:
        collider_score = 0.0
    elif colliders > 200:
        collider_score = 0.5
    else:
        collider_score = 2.0
    raw = spawn_score + nav_score + area_score + collider_score
    # Normalize to 0-10 scale (raw max = 10)
    quality = max(0.0, min(10.0, raw))
    # round
    return round(quality, 2)

def validate_scene(scene_path: str) -> Dict[str, Any]:
    """
    Validate a Unity scene (.unity YAML file) and return a report dict.

    Example return:
    {
      "scene": "Arena_2da29146.unity",
      "spawn_points": 8,
      "spawn_point_distribution": "balanced",
      "navmesh_coverage": 87.3,
      "playable_area_sqm": 245.6,
      "wall_count": 142,
      "has_lighting": True,
      "issues": [...],
      "quality_score": 8.2
    }
    """
    report: Dict[str, Any] = {
        "scene": os.path.basename(scene_path),
        "spawn_points": 0,
        "spawn_point_distribution": "unknown",
        "navmesh_coverage": 0.0,
        "playable_area_sqm": 0.0,
        "wall_count": 0,
        "has_lighting": False,
        "issues": [],
        "quality_score": 0.0,
    }

    if not os.path.isfile(scene_path):
        report["issues"].append("Scene file not found")
        return report

    try:
        text = _read_text(scene_path)
    except Exception as e:
        report["issues"].append(f"Could not read scene: {e}")
        return report

    spawns = _find_spawn_points(text)
    navmesh = _has_navmesh(text)
    colliders = _count_colliders(text)
    area = _estimate_playable_area(text)

    # Basic heuristics for distribution
    if len(spawns) >= 8:
        distribution = "balanced"
    elif 4 <= len(spawns) < 8:
        distribution = "ok"
    elif 1 <= len(spawns) < 4:
        distribution = "sparse"
    else:
        distribution = "none"

    # wall_count heuristic: count occurrences of "Wall" in names
    wall_count = len(re.findall(r"m_Name:\s*.*[Ww]all", text))

    # lighting heuristic
    has_lighting = bool(re.search(r"Light:", text)) or bool(re.search(r"m_Type:\s*Directional", text))

    issues = _gather_issues(spawns, navmesh, colliders)

    quality = _compute_quality(len(spawns), navmesh, area, colliders)

    # rough navmesh coverage estimate: if navmesh present, 70-95% depending on area
    nav_cov = 0.0
    if navmesh:
        if area <= 0:
            nav_cov = 50.0
        else:
            nav_cov = max(40.0, min(95.0, 70.0 + (area - 250.0) / 50.0))
        nav_cov = round(nav_cov, 1)

    report.update({
        "spawn_points": len(spawns),
        "spawn_point_distribution": distribution,
        "navmesh_coverage": nav_cov,
        "playable_area_sqm": round(area, 2),
        "wall_count": wall_count,
        "has_lighting": has_lighting,
        "issues": issues,
        "quality_score": quality
    })

    return report

# small CLI for quick manual testing
if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("Usage: python -m agent.tools.scene_validator <scene_path>")
        sys.exit(1)
    scene = sys.argv[1]
    r = validate_scene(scene)
    print(json.dumps(r, indent=2))
