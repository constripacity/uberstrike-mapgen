#if UNITY_EDITOR
using UnityEngine;
using UnityAI;

/// <summary>
/// Stub for StackPreviewer — full implementation not yet ported from Unity 6.
/// </summary>
public static class StackPreviewer
{
    public static void Preview(StackDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogWarning("[StackPreviewer] No stack definition to preview.");
            return;
        }

        Debug.Log($"[StackPreviewer] Preview not yet ported to Unity 2022. Stack: '{definition.sourceName}', Size: {definition.Width}x{definition.Height}");
    }
}
#endif
