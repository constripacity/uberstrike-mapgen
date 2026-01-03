from __future__ import annotations

import json
import math
import statistics
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple

TOOL_METADATA = {
    "name": "asset_extractor",
    "description": "Parses Unity .unity YAML scenes to extract gameplay patterns and training blueprints.",
    "version": "0.1.0",
    "capabilities": [
        "extract_spawn_patterns",
        "extract_weapon_placements",
        "generate_training_blueprints",
        "export_pattern_json"
    ],
}

_GAMEOBJECT_CLASS = "1"
_TRANSFORM_CLASS = "4"
_HEADER_REGEX = re.compile(r"^--- !u!(?P<class>\d+) &(?P<file>\d+)")
_NAME_REGEX = re.compile(r"m_Name:\s*(?P<name>.+)")
_TAG_REGEX = re.compile(r"m_TagString:\s*(?P<tag>.+)")
_LAYER_REGEX = re.compile(r"m_Layer:\s*(?P<layer>\d+)")
_GO_REF_REGEX = re.compile(r"m_GameObject:\s*\{\s*fileID:\s*(?P<file>\d+)\s*\}")
_INLINE_POS_REGEX = re.compile(
    r"m_LocalPosition:\s*\{\s*x:\s*(?P<x>-?\d+(?:\.\d+)?),\s*y:\s*(?P<y>-?\d+(?:\.\d+)?),\s*z:\s*(?P<z>-?\d+(?:\.\d+)?)\s*\}"
)


@dataclass
class SceneObject:
    file_id: int
    name: str
    position: Tuple[float, float, float]
    tag: str = ""
    layer: int = -1

    def to_payload(self) -> Dict[str, Any]:
        return {
            "name": self.name,
            "tag": self.tag,
            "layer": self.layer,
            "pos": [round(self.position[0], 3), round(self.position[1], 3), round(self.position[2], 3)],
        }


def extract_map_patterns(scene_path: str) -> Dict[str, Any]:
    """Read a Unity scene and return spawn/weapon/flow/height metadata for ML pipelines."""

    scene_path = str(scene_path)
    text = _read_scene(scene_path)
    objects = _collect_scene_objects(text)
    if not objects:
        return {"scene": Path(scene_path).name, "error": "no_objects"}

    bounds = _compute_bounds(objects)
    spawn_objs = [o for o in objects if "spawn" in o.name.lower()]
    weapon_objs = [o for o in objects if _is_weapon(o)]
    cover_objs = [o for o in objects if _is_cover(o)]
    chokepoint_objs = [o for o in objects if _is_chokepoint(o)]

    spawn_positions = [o.to_payload() | {"team": _infer_team(o.name)} for o in spawn_objs]
    weapon_positions = [o.to_payload() for o in weapon_objs]
    cover_positions = [o.to_payload() for o in cover_objs]

    spawn_stats = _spawn_distance_stats(spawn_objs)
    height_stats = _height_statistics(objects)
    flow_paths = _derive_flow_paths(spawn_objs)
    chokepoint_widths = _estimate_chokepoint_widths(chokepoint_objs)
    style_profile = _derive_style_profile(spawn_objs, weapon_objs, bounds, height_stats)

    return {
        "scene": Path(scene_path).name,
        "bounds": bounds,
        "spawn_positions": spawn_positions,
        "weapon_positions": weapon_positions,
        "cover_positions": cover_positions,
        "spawn_stats": spawn_stats,
        "height_stats": height_stats,
        "flow_paths": flow_paths,
        "chokepoint_widths": chokepoint_widths,
        "style_profile": style_profile,
    }


def generate_training_blueprint(patterns: Dict[str, Any], resolution: int = 128) -> Dict[str, Any]:
    """Convert extracted patterns into a normalized training blueprint asset."""

    bounds = patterns.get("bounds") or {
        "min": [0.0, 0.0, 0.0],
        "max": [1.0, 0.0, 1.0],
    }
    blueprint = {
        "scene": patterns.get("scene"),
        "cell_size_m": 1.0,
        "resolution": resolution,
        "bounds": bounds,
        "style_profile": patterns.get("style_profile", {}),
        "spawns": [],
        "weapons": [],
    }

    for entry in patterns.get("spawn_positions", []):
        grid = _world_to_grid(entry.get("pos", [0, 0, 0]), bounds, resolution)
        blueprint["spawns"].append({
            "team": entry.get("team", "neutral"),
            "grid": grid,
        })

    for entry in patterns.get("weapon_positions", []):
        grid = _world_to_grid(entry.get("pos", [0, 0, 0]), bounds, resolution)
        blueprint["weapons"].append({
            "name": entry.get("name"),
            "grid": grid,
        })

    blueprint["flow_paths"] = patterns.get("flow_paths", [])
    blueprint["height_profile"] = patterns.get("height_stats", {})
    blueprint["chokepoint_widths"] = patterns.get("chokepoint_widths", [])
    return blueprint


def export_training_data(scenes_folder: str, output_dir: Optional[str] = None) -> Path:
    """Extract patterns + blueprints for every .unity scene in a folder."""

    scenes_root = Path(scenes_folder)
    out_dir = Path(output_dir) if output_dir else scenes_root / "_training"
    out_dir.mkdir(parents=True, exist_ok=True)

    manifest: List[Dict[str, Any]] = []
    for scene in sorted(scenes_root.rglob("*.unity")):
        patterns = extract_map_patterns(str(scene))
        blueprint = generate_training_blueprint(patterns)
        pattern_path = out_dir / f"{scene.stem}_patterns.json"
        blueprint_path = out_dir / f"{scene.stem}_blueprint.json"
        pattern_path.write_text(json.dumps(patterns, indent=2), encoding="utf-8")
        blueprint_path.write_text(json.dumps(blueprint, indent=2), encoding="utf-8")
        manifest.append({
            "scene": scene.name,
            "pattern": pattern_path.name,
            "blueprint": blueprint_path.name,
            "style": blueprint.get("style_profile", {}).get("type", "unknown"),
        })

    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest_path


def _read_scene(scene_path: str) -> str:
    path = Path(scene_path)
    if not path.exists():
        raise FileNotFoundError(scene_path)
    return path.read_text(encoding="utf-8", errors="ignore")


def _collect_scene_objects(text: str) -> List[SceneObject]:
    blocks = list(_split_blocks(text))
    game_objects: Dict[int, Dict[str, Any]] = {}
    transforms: Dict[int, Tuple[float, float, float]] = {}

    for header, body in blocks:
        match = _HEADER_REGEX.match(header)
        if not match:
            continue
        class_id = match.group("class")
        file_id = int(match.group("file"))

        if class_id == _GAMEOBJECT_CLASS:
            name = _match_group(_NAME_REGEX, body)
            tag = _match_group(_TAG_REGEX, body) or "Untagged"
            layer = int(_match_group(_LAYER_REGEX, body) or -1)
            game_objects[file_id] = {"name": name or f"GameObject_{file_id}", "tag": tag, "layer": layer}
        elif class_id == _TRANSFORM_CLASS:
            ref_id = _extract_gameobject_reference(body)
            if ref_id is None:
                continue
            position = _extract_position(body)
            if position is None:
                continue
            transforms[ref_id] = position

    objects: List[SceneObject] = []
    for file_id, info in game_objects.items():
        position = transforms.get(file_id)
        if position is None:
            continue
        objects.append(SceneObject(file_id=file_id, name=info["name"], position=position, tag=info["tag"], layer=info["layer"]))
    return objects


def _split_blocks(text: str) -> Iterable[Tuple[str, str]]:
    header: Optional[str] = None
    body_lines: List[str] = []
    for line in text.splitlines():
        if line.startswith("--- !u!"):
            if header is not None:
                yield header, "\n".join(body_lines)
                body_lines = []
            header = line
        elif header is not None:
            body_lines.append(line)
    if header is not None:
        yield header, "\n".join(body_lines)


def _match_group(regex: re.Pattern[str], text: str) -> Optional[str]:
    match = regex.search(text)
    return match.group(1).strip() if match else None


def _extract_gameobject_reference(block: str) -> Optional[int]:
    match = _GO_REF_REGEX.search(block)
    if not match:
        return None
    try:
        return int(match.group("file"))
    except ValueError:
        return None


def _extract_position(block: str) -> Optional[Tuple[float, float, float]]:
    inline = _INLINE_POS_REGEX.search(block)
    if inline:
        return float(inline.group("x")), float(inline.group("y")), float(inline.group("z"))

    if "m_LocalPosition" not in block:
        return None

    x = y = z = None
    for line in block.splitlines():
        stripped = line.strip()
        if stripped.startswith("x:"):
            x = float(stripped.split(":", 1)[1])
        elif stripped.startswith("y:"):
            y = float(stripped.split(":", 1)[1])
        elif stripped.startswith("z:"):
            z = float(stripped.split(":", 1)[1])
    if x is None or y is None or z is None:
        return None
    return x, y, z


def _compute_bounds(objects: List[SceneObject]) -> Dict[str, List[float]]:
    xs = [o.position[0] for o in objects]
    ys = [o.position[1] for o in objects]
    zs = [o.position[2] for o in objects]
    return {
        "min": [min(xs), min(ys), min(zs)],
        "max": [max(xs), max(ys), max(zs)],
    }


def _world_to_grid(pos: Iterable[float], bounds: Dict[str, List[float]], resolution: int) -> List[int]:
    min_x, _, min_z = bounds["min"]
    max_x, _, max_z = bounds["max"]
    width = max(max_x - min_x, 1e-3)
    depth = max(max_z - min_z, 1e-3)
    x = int(((pos[0] - min_x) / width) * (resolution - 1))
    z = int(((pos[2] - min_z) / depth) * (resolution - 1))
    return [max(0, min(resolution - 1, x)), max(0, min(resolution - 1, z))]


def _is_weapon(obj: SceneObject) -> bool:
    lower = obj.name.lower()
    return any(token in lower for token in ("weapon", "rifle", "gun", "rocket", "pickup"))


def _is_cover(obj: SceneObject) -> bool:
    lower = obj.name.lower()
    return any(token in lower for token in ("cover", "crate", "pillar", "box"))


def _is_chokepoint(obj: SceneObject) -> bool:
    lower = obj.name.lower()
    return any(token in lower for token in ("door", "choke", "hall", "bridge", "tunnel"))


def _spawn_distance_stats(spawns: List[SceneObject]) -> Dict[str, float]:
    if len(spawns) < 2:
        return {"count": len(spawns), "min": 0.0, "max": 0.0, "avg": 0.0}

    distances: List[float] = []
    for i in range(len(spawns)):
        for j in range(i + 1, len(spawns)):
            distances.append(_distance(spawns[i].position, spawns[j].position))

    return {
        "count": len(spawns),
        "min": min(distances),
        "max": max(distances),
        "avg": sum(distances) / len(distances),
    }


def _height_statistics(objects: List[SceneObject]) -> Dict[str, float]:
    heights = [o.position[1] for o in objects]
    if not heights:
        return {"min": 0.0, "max": 0.0, "mean": 0.0, "std": 0.0}
    return {
        "min": min(heights),
        "max": max(heights),
        "mean": statistics.fmean(heights),
        "std": statistics.pstdev(heights) if len(heights) > 1 else 0.0,
    }


def _derive_flow_paths(spawns: List[SceneObject]) -> List[Dict[str, Any]]:
    if len(spawns) < 2:
        return []

    center_x = sum(o.position[0] for o in spawns) / len(spawns)
    center_z = sum(o.position[2] for o in spawns) / len(spawns)

    ordered = sorted(
        spawns,
        key=lambda obj: math.atan2(obj.position[2] - center_z, obj.position[0] - center_x),
    )

    paths = []
    for i in range(len(ordered)):
        a = ordered[i]
        b = ordered[(i + 1) % len(ordered)]
        paths.append({
            "from": a.to_payload(),
            "to": b.to_payload(),
            "length": _distance(a.position, b.position),
        })
    return paths


def _estimate_chokepoint_widths(objects: List[SceneObject]) -> List[float]:
    widths: List[float] = []
    for obj in objects:
        nearest = min((_distance(obj.position, other.position) for other in objects if other is not obj), default=0.0)
        if nearest > 0:
            widths.append(nearest)
    return sorted(widths)[:10]


def _derive_style_profile(spawns: List[SceneObject], weapons: List[SceneObject], bounds: Dict[str, Any], height_stats: Dict[str, float]) -> Dict[str, Any]:
    spawn_count = len(spawns)
    teams = _team_counts(spawns)
    map_type = "Arena"
    if teams.get("red") and teams.get("blue"):
        map_type = "CTF"
    elif spawn_count >= 12:
        map_type = "Deathmatch"

    balance_score = 1.0
    if teams:
        max_team = max(teams.values())
        min_team = min(teams.values())
        total = sum(teams.values())
        balance_score = 1.0 - ((max_team - min_team) / max(1, total))

    area = max((bounds["max"][0] - bounds["min"][0]) * (bounds["max"][2] - bounds["min"][2]), 1.0)
    weapon_density = len(weapons) / area

    return {
        "type": map_type,
        "spawn_balance": round(balance_score, 3),
        "weapon_density": round(weapon_density, 4),
        "avg_height": round(height_stats.get("mean", 0.0), 3),
    }


def _team_counts(spawns: List[SceneObject]) -> Dict[str, int]:
    counts: Dict[str, int] = {"neutral": 0, "red": 0, "blue": 0, "green": 0}
    for spawn in spawns:
        team = _infer_team(spawn.name)
        counts[team] = counts.get(team, 0) + 1
    return counts


def _infer_team(name: str) -> str:
    lower = name.lower()
    if "red" in lower:
        return "red"
    if "blue" in lower:
        return "blue"
    if "green" in lower:
        return "green"
    return "neutral"


def _distance(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def _is_main():
    import argparse

    parser = argparse.ArgumentParser(description="Extract UberStrike patterns from Unity scenes")
    parser.add_argument("path", help="Scene file or folder to scan")
    parser.add_argument("--out", dest="out", help="Output folder for JSON exports")
    args = parser.parse_args()

    path = Path(args.path)
    if path.is_file():
        data = extract_map_patterns(str(path))
        print(json.dumps(data, indent=2))
    else:
        manifest = export_training_data(str(path), args.out)
        print(f"Patterns exported to {manifest}")


if __name__ == "__main__":
    _is_main()
