"""
visualize_results.py -- Generate charts from Experiment 1 scaling data

Produces two PNGs:
    chart_a_runtime_vs_qubits.png   - Exponential runtime blowup
    chart_b_energy_vs_candidates.png - Solution quality vs problem size

Usage:
    python -m quantum_mapgen.visualize_results

Author: Culo / Quantum MapGen Project
"""

import matplotlib
matplotlib.use("Agg")  # non-interactive backend for headless rendering
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import numpy as np
import os

OUT_DIR = os.path.dirname(os.path.abspath(__file__))


def chart_a_runtime_vs_qubits():
    """Chart A: Runtime (log scale) vs Qubits with annotated zones."""

    # Local data points
    qubits_local = [12, 15, 18, 21, 24]
    runtime_local = [7.3, 10.3, 17.5, 52.2, 373.2]

    # Wall point (killed)
    qubit_wall = 27
    runtime_wall = 3600  # lower bound

    fig, ax = plt.subplots(figsize=(12, 7))

    # Plot local data
    ax.semilogy(qubits_local, runtime_local, "o-", color="#2563eb", markersize=10,
                linewidth=2.5, label="Local Simulator (Shadow PC)", zorder=5)

    # Wall point
    ax.semilogy(qubit_wall, runtime_wall, "^", color="#dc2626", markersize=14,
                label="27 qubits: killed after 1 hour", zorder=5)
    ax.annotate("KILLED\n(>1 hour)", xy=(qubit_wall, runtime_wall),
                xytext=(qubit_wall + 2, runtime_wall * 2),
                fontsize=10, color="#dc2626", fontweight="bold",
                arrowprops=dict(arrowstyle="->", color="#dc2626", lw=1.5))

    # Exponential fit line (extrapolation)
    log_rt = np.log(runtime_local)
    coeffs = np.polyfit(qubits_local, log_rt, 1)
    q_fit = np.linspace(10, 35, 100)
    rt_fit = np.exp(np.polyval(coeffs, q_fit))
    ax.semilogy(q_fit, rt_fit, "--", color="#94a3b8", linewidth=1.5, alpha=0.7,
                label="Exponential trend")

    # Zone shading
    # Local zone (practical)
    ax.axvspan(10, 25, alpha=0.08, color="#22c55e")
    ax.text(17, 2, "Local Simulator\n(practical)", fontsize=10, color="#15803d",
            ha="center", fontstyle="italic")

    # Local wall zone
    ax.axvspan(25, 28, alpha=0.10, color="#f59e0b")
    ax.text(26.5, 2, "Wall", fontsize=9, color="#b45309", ha="center", fontstyle="italic")

    # SV1 zone
    ax.axvspan(28, 34, alpha=0.08, color="#3b82f6")
    ax.text(31, 2, "SV1 Cloud\n(34 qubit limit)", fontsize=10, color="#1d4ed8",
            ha="center", fontstyle="italic")

    # SV1 data point (estimated from wall time)
    ax.semilogy(33, 44 * 60, "s", color="#3b82f6", markersize=12,
                label="SV1 Cloud (33 qubits, ~44 min wall)", zorder=5)

    # D-Wave zone
    ax.axvspan(34, 200, alpha=0.05, color="#a855f7")

    # D-Wave projections
    for q, label in [(150, "25 items x 50 cands"), (750, "25 items x 250 cands")]:
        if q <= 200:
            ax.axvline(x=q, color="#7c3aed", linestyle=":", alpha=0.5)

    ax.annotate("D-Wave Advantage\n5,000+ qubits\nNo classical simulation possible",
                xy=(100, 50), fontsize=11, color="#7c3aed", fontweight="bold",
                ha="center", bbox=dict(boxstyle="round,pad=0.5", facecolor="#f3e8ff",
                                       edgecolor="#7c3aed", alpha=0.9))

    # Red dashed line at the wall
    ax.axvline(x=25, color="#dc2626", linestyle="--", linewidth=2, alpha=0.6)

    # Formatting
    ax.set_xlabel("Number of Qubits", fontsize=13, fontweight="bold")
    ax.set_ylabel("Runtime (seconds, log scale)", fontsize=13, fontweight="bold")
    ax.set_title("QAOA Runtime vs Problem Size\nExponential Wall Motivates Quantum Hardware",
                 fontsize=14, fontweight="bold")
    ax.set_xlim(10, 200)
    ax.set_ylim(1, 1e5)
    ax.legend(loc="upper left", fontsize=10, framealpha=0.9)
    ax.grid(True, alpha=0.3, which="both")
    ax.set_xticks([12, 15, 18, 21, 24, 27, 33, 50, 100, 150])
    ax.set_xticklabels(["12", "15", "18", "21", "24", "27", "33", "50", "100", "150"])

    plt.tight_layout()
    path = os.path.join(OUT_DIR, "chart_a_runtime_vs_qubits.png")
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print(f"  Saved: {path}")
    return path


def chart_b_energy_vs_candidates():
    """Chart B: Best SA Energy vs Number of Candidates."""

    # Local data
    cands_local = [4, 5, 6, 7, 8]
    energy_local = [2589, 2496, 2447, 3054, 2653]
    violations_local = [2, 2, 1, 2, 1]

    # SV1 cloud data
    cands_cloud = 11
    energy_cloud = 1339.5
    violations_cloud = 2  # but rocket-armor OK

    fig, ax1 = plt.subplots(figsize=(11, 7))

    # Energy bars (local)
    colors_local = ["#ef4444" if v >= 2 else "#f59e0b" if v == 1 else "#22c55e"
                    for v in violations_local]
    bars = ax1.bar(cands_local, energy_local, color=colors_local, width=0.6,
                   edgecolor="white", linewidth=1.5, alpha=0.85, label="Local Simulator")

    # Add violation count on bars
    for i, (c, e, v) in enumerate(zip(cands_local, energy_local, violations_local)):
        ax1.text(c, e + 50, f"{v} viol", ha="center", fontsize=9, color="#374151")

    # SV1 cloud bar
    cloud_color = "#3b82f6"
    ax1.bar(cands_cloud, energy_cloud, color=cloud_color, width=0.6,
            edgecolor="white", linewidth=1.5, alpha=0.85, label="SV1 Cloud (33 qubits)")
    ax1.text(cands_cloud, energy_cloud + 50, f"{violations_cloud} viol\n(1 OK)",
             ha="center", fontsize=9, color="#1d4ed8", fontweight="bold")

    # Trend line
    all_cands = cands_local + [cands_cloud]
    all_energy = energy_local + [energy_cloud]
    z = np.polyfit(all_cands, all_energy, 1)
    x_trend = np.linspace(3, 13, 50)
    y_trend = np.polyval(z, x_trend)
    ax1.plot(x_trend, y_trend, "--", color="#94a3b8", linewidth=1.5, alpha=0.7,
             label="Trend (more candidates = lower energy)")

    # D-Wave projection zone
    ax1.axvspan(12, 16, alpha=0.08, color="#a855f7")
    ax1.annotate("D-Wave zone\n50-250 candidates\n(projected)",
                 xy=(14, 800), fontsize=10, color="#7c3aed",
                 ha="center", fontstyle="italic",
                 bbox=dict(boxstyle="round,pad=0.4", facecolor="#f3e8ff",
                           edgecolor="#7c3aed", alpha=0.8))

    # Gap annotation
    ax1.annotate("", xy=(cands_cloud, energy_cloud), xytext=(6, 2447),
                 arrowprops=dict(arrowstyle="<->", color="#059669", lw=2))
    mid_x = (6 + cands_cloud) / 2
    mid_y = (2447 + energy_cloud) / 2
    ax1.text(mid_x, mid_y + 100, "45% improvement\nwith 5 more candidates",
             ha="center", fontsize=10, color="#059669", fontweight="bold",
             bbox=dict(boxstyle="round,pad=0.3", facecolor="#ecfdf5",
                       edgecolor="#059669", alpha=0.9))

    # Legend for violation colors
    red_patch = mpatches.Patch(color="#ef4444", alpha=0.85, label="2+ spacing violations")
    yellow_patch = mpatches.Patch(color="#f59e0b", alpha=0.85, label="1 spacing violation")
    green_patch = mpatches.Patch(color="#22c55e", alpha=0.85, label="0 violations (feasible)")
    blue_patch = mpatches.Patch(color="#3b82f6", alpha=0.85, label="SV1 Cloud result")

    ax1.legend(handles=[red_patch, yellow_patch, green_patch, blue_patch],
               loc="upper right", fontsize=9, framealpha=0.9)

    # Formatting
    ax1.set_xlabel("Number of Candidate Positions", fontsize=13, fontweight="bold")
    ax1.set_ylabel("Best SA-Equivalent Energy (lower = better)", fontsize=13, fontweight="bold")
    ax1.set_title("Solution Quality vs Problem Size\nMore Candidates = Better Placements = Need for Quantum Hardware",
                  fontsize=14, fontweight="bold")
    ax1.set_xlim(2.5, 16)
    ax1.set_ylim(0, 3500)
    ax1.set_xticks([4, 5, 6, 7, 8, 11, 14])
    ax1.set_xticklabels(["4\n(12q)", "5\n(15q)", "6\n(18q)", "7\n(21q)",
                          "8\n(24q)", "11\n(33q)\nSV1", "50+\nD-Wave"])
    ax1.grid(True, alpha=0.2, axis="y")

    plt.tight_layout()
    path = os.path.join(OUT_DIR, "chart_b_energy_vs_candidates.png")
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print(f"  Saved: {path}")
    return path


def main():
    print("=" * 60)
    print("  Generating Experiment 1 Visualization Charts")
    print("=" * 60)
    print()

    chart_a_runtime_vs_qubits()
    chart_b_energy_vs_candidates()

    print()
    print("  Done. Charts saved to quantum_mapgen/")
    print("=" * 60)


if __name__ == "__main__":
    main()
