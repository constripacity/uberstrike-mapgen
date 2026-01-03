"""
ML-like map quality analysis (heuristic implementation)

Provides analyze_map_quality(scene_path) -> dict with:
 - flow_score (0-10)
 - balance_score (0-10)
 - cover_distribution: "poor"|"ok"|"good"
 - sightline_analysis: { long, medium, short }
 - predicted_fun_score (0-10)
 - recommendations: list[str]

This is a heuristic, deterministic implementation that reuses the scene_validator
to extract basic scene features, then derives gameplay metrics and recommendations.

The goal is to provide an API similar to a future ML model output so it can be
swapped out with a learned model later.
"""

from typing import Dict, Any
import os
import json
from .scene_validator import validate_scene

def _score_flow(area: float, wall_count: int, navmesh_present: bool) -> float:
    # Heuristic: mid-sized areas with navmesh and moderate walls score higher
    if area <= 0:
        return 2.0
    area_factor = min(1.0, area / 400.0)         # ideal around 400 sqm
    wall_factor = 1.0 if (10 <= wall_count <= 300) else 0.6
    nav_factor = 1.2 if navmesh_present else 0.7
    raw = 10.0 * area_factor * wall_factor * nav_factor
    return round(max(0.0, min(10.0, raw)), 2)

def _score_balance(spawn_count: int, area: float) -> float:
    # Heuristic: ideal spawn_count ~8, penalty if too few or too many
    if spawn_count <= 0:
        return 1.0
    spawn_ratio = min(spawn_count / 8.0, 2.0)    # cap at 2x
    area_factor = 1.0 if area >= 100 else max(0.3, area / 100.0)
    raw = 10.0 * (spawn_ratio / 2.0) * area_factor
    return round(max(0.0, min(10.0, raw)), 2)

def _cover_distribution(colliders: int, wall_count: int) -> str:
    # Very rough heuristic: colliders + walls -> proxy for cover
    score = colliders + (wall_count * 0.5)
    if score > 300:
        return "good"
    if score > 80:
        return "ok"
    return "poor"

def _sightline_analysis(area: float, wall_count: int) -> Dict[str, int]:
    # Estimate number of long/medium/short sightlines based on geometry heuristics
    # Larger area and fewer walls -> more long sightlines
    if area <= 0:
        return {"long_sightlines": 0, "medium_sightlines": 0, "short_sightlines": 0}
    long = int(max(0, (area / 200.0) * max(0, 10 - (wall_count / 50.0))))
    medium = int(max(0, (area / 150.0) * (5 + (wall_count / 100.0))))
    short = int(max(0, (wall_count / 3.0)))
    return {
        "long_sightlines": long,
        "medium_sightlines": medium,
        "short_sightlines": short
    }

def _predicted_fun(flow: float, balance: float, cover_dist: str, sightlines: Dict[str,int]) -> float:
    # Combine metrics into a predicted fun score
    cover_bonus = {"poor": 0.8, "ok": 1.0, "good": 1.1}[cover_dist]
    sight_penalty = 1.0
    long = sightlines.get("long_sightlines", 0)
    if long > 20:
        sight_penalty = 0.85
    elif long > 10:
        sight_penalty = 0.92
    raw = (flow * 0.45 + balance * 0.35 + 10.0 * 0.20) * 0.1  # normalize to 0-10-ish
    raw = raw * cover_bonus * sight_penalty
    return round(max(0.0, min(10.0, raw)), 2)

def _generate_recommendations(report: Dict[str, Any]) -> list:
    recs = []
    if report["spawn_points"] < 6:
        recs.append("Add more spawn points to improve balance (target ~8).")
    if report["navmesh_coverage"] < 60.0:
        recs.append("Improve navmesh coverage so bots and pathing can traverse the map reliably.")
    if report["cover_distribution"] == "poor":
        recs.append("Add more cover objects (crates, walls) especially in open center areas.")
    sl = report.get("sightline_analysis", {})
    if sl.get("long_sightlines", 0) > 12:
        recs.append("Reduce long sightlines (add mid-range cover) to prevent sniper-dominant play.")
    if report["playable_area_sqm"] > 800:
        recs.append("Consider subdividing large open areas to maintain action density.")
    if not report["has_lighting"]:
        recs.append("Add lighting to ensure visibility and atmosphere.")
    if not recs:
        recs.append("No major issues detected. Map looks good.")
    return recs

def analyze_map_quality(scene_path: str) -> Dict[str, Any]:
    """
    Analyze map quality for a Unity scene. Returns a structured dict.
    """
    base_report = {
        "scene": os.path.basename(scene_path),
        "flow_score": 0.0,
        "balance_score": 0.0,
        "cover_distribution": "unknown",
        "sightline_analysis": {"long_sightlines": 0, "medium_sightlines": 0, "short_sightlines": 0},
        "predicted_fun_score": 0.0,
        "recommendations": []
    }

    # Try several normalized path forms to handle Windows forward/backward slashes and odd inputs
    tried_paths = []
    candidates = [
        scene_path,
        os.path.normpath(scene_path),
        scene_path.replace('/', os.sep),
        scene_path.replace('\\', os.sep),
        os.path.abspath(scene_path),
        os.path.abspath(os.path.normpath(scene_path))
    ]
    file_found = False
    for p in dict.fromkeys(candidates):  # preserve order, remove duplicates
        tried_paths.append(p)
        if os.path.exists(p) and os.path.isfile(p):
            scene_path = p
            file_found = True
            break
    if not file_found:
        base_report["recommendations"] = [f"Scene file not found. Tried paths: {json.dumps(tried_paths)}"]
        base_report["tried_paths"] = tried_paths
        return base_report

    # Use scene_validator to extract features
    sv = validate_scene(scene_path)
    spawns = sv.get("spawn_points", 0)
    navmesh_cov = sv.get("navmesh_coverage", 0.0)
    area = sv.get("playable_area_sqm", 0.0)
    colliders = sv.get("collider_count", sv.get("wall_count", 0))  # prefer explicit collider_count if provided, else fall back to wall_count
    wall_count = sv.get("wall_count", 0)
    has_lighting = sv.get("has_lighting", False)
    # colliders_count was unused; using 'colliders' variable above as the proxy for cover calculations

    flow = _score_flow(area, wall_count, navmesh_cov > 0)
    balance = _score_balance(spawns, area)
    cover = _cover_distribution(colliders, wall_count)
    sight = _sightline_analysis(area, wall_count)
    predicted = _predicted_fun(flow, balance, cover, sight)
    recs = _generate_recommendations({
        "spawn_points": spawns,
        "navmesh_coverage": navmesh_cov,
        "playable_area_sqm": area,
        "cover_distribution": cover,
        "has_lighting": has_lighting,
        "sightline_analysis": sight,
        "wall_count": wall_count
    })

    base_report.update({
        "flow_score": flow,
        "balance_score": balance,
        "cover_distribution": cover,
        "sightline_analysis": sight,
        "predicted_fun_score": predicted,
        "recommendations": recs
    })

    return base_report

# small CLI for manual testing
if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("Usage: python -m agent.tools.map_quality <scene_path>")
        sys.exit(1)
    scene = sys.argv[1]
    out = analyze_map_quality(scene)
    print(json.dumps(out, indent=2))
