#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class QCVisualizer {
  [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
  static void Draw(Transform t, GizmoType gt) {
    if (!t || t.name.StartsWith("Arena_") == false) return;
    foreach (var go in GameObject.FindGameObjectsWithTag("SpawnPoint"))
      DrawSphere(go.transform.position, Color.yellow);
    foreach (var j in GameObject.FindGameObjectsWithTag("JumpPad"))
      DrawSphere(j.transform.position, Color.magenta);
    // Teleporter link lines: objects named "Teleporter_*" with optional "Exit" child
    var teles = GameObject.FindObjectsOfType<Transform>();
    var list = new System.Collections.Generic.List<Transform>();
    foreach (var tt in teles) if (tt.name.StartsWith("Teleporter_")) list.Add(tt);
    for (int i = 0; i + 1 < list.Count; i += 2)
      Gizmos.DrawLine(list[i].position, list[i+1].position);
  }
  static void DrawSphere(Vector3 p, Color c){ Gizmos.color=c; Gizmos.DrawSphere(p,0.25f); }
}
#endif
