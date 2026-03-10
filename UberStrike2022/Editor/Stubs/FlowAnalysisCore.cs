#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stub for FlowAnalysisCore — full NavMesh-based flow analysis not yet ported from Unity 6.
/// Returns empty metrics so QC report generation doesn't crash.
/// </summary>
public static class FlowAnalysisCore
{
    public class FlowMetrics
    {
        public List<Vector3> chokepoints = new List<Vector3>();
        public List<Bounds> deadZones = new List<Bounds>();
        public List<Vector3> strategicPositions = new List<Vector3>();

        public string Summary()
        {
            return $"[FlowAnalysisCore] Stub metrics — chokepoints: {chokepoints.Count}, dead zones: {deadZones.Count}, strategic: {strategicPositions.Count}";
        }
    }

    public static FlowMetrics Analyze(GameObject root)
    {
        Debug.Log("[FlowAnalysisCore] Stub: full NavMesh-based analysis not yet ported to Unity 2022.");
        return new FlowMetrics();
    }
}
#endif
