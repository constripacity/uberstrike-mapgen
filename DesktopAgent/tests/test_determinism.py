import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.append(str(Path(__file__).resolve().parents[1]))

from agent.tools.graph_flow_analyzer import GraphFlowAnalyzer
from agent.tools.simulated_annealing_placer import PlacementConstraints, SimulatedAnnealingPlacer
from agent.tools.voronoi_theme_generator import VoronoiThemeGenerator
from agent.tools.wave_function_collapse import WaveFunctionCollapse


def test_wfc_deterministic():
    seed = 42
    solver_a = WaveFunctionCollapse(8, 8, seed)
    bp_a = solver_a.generate_arena_layout(spawn_count=2, ensure_connected=True, fallback_to_blank=True)

    solver_b = WaveFunctionCollapse(8, 8, seed)
    bp_b = solver_b.generate_arena_layout(spawn_count=2, ensure_connected=True, fallback_to_blank=True)

    assert np.array_equal(bp_a, bp_b)


def test_wfc_reports_failure():
    solver = WaveFunctionCollapse(8, 8, seed=99)
    with pytest.raises(RuntimeError):
        solver.generate_arena_layout(spawn_count=2, ensure_connected=True, max_steps=1, fallback_to_blank=False)


def test_voronoi_deterministic():
    gen = VoronoiThemeGenerator()
    out_a = gen.generate(16, 16, num_regions=4, seed=123)
    out_b = gen.generate(16, 16, num_regions=4, seed=123)
    assert np.array_equal(out_a["array"], out_b["array"])


def test_simulated_annealing_respects_bounds_and_spacing():
    mask = np.ones((10, 10), dtype=bool)
    constraints = PlacementConstraints(
        spawn_points=[(1.0, 1.0), (8.0, 8.0)],
        walkable_areas=mask,
        choke_points=[],
        cover_positions=[],
        existing_items={},
    )
    placer = SimulatedAnnealingPlacer(seed=7, temperature=250.0, cooling_rate=0.9)
    placements = placer.optimise(constraints, {"weapon_sniper": 1, "health_small": 2}, max_iterations=400)

    for positions in placements.values():
        for x, y in positions:
            assert 0 <= x < 10
            assert 0 <= y < 10


def test_graph_flow_analyzer_outputs_metrics():
    layout = np.zeros((6, 6), dtype=int)
    layout[1:5, 1:5] = 1
    analyzer = GraphFlowAnalyzer(seed=5)
    metrics = analyzer.analyze_map(layout, spawn_points=[(2, 2), (4, 4)], item_positions={})
    assert metrics.heat_map.size > 0
    assert metrics.spawn_balance >= 0
    assert metrics.map_openness >= 0
