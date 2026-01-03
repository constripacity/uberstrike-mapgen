#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CyanClassificationTest
{
    private const string MenuPath = "Tools/UnityAI/Tests/Cyan Classification";

    [MenuItem(MenuPath)]
    public static void Run()
    {
        try
        {
            // Find BuildFromBlueprint type by searching all assemblies
            Type buildType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                buildType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "BuildFromBlueprint");
                if (buildType != null)
                    break;
            }

            if (buildType == null)
            {
                Debug.LogError("[CyanClassificationTest] BuildFromBlueprint type not found in any assembly. Make sure the class exists in your project.");
                return;
            }

            var classifyMethod = buildType.GetMethod("ClassifyPixel", BindingFlags.NonPublic | BindingFlags.Static);
            var tileKindType = buildType.GetNestedType("TileKind", BindingFlags.NonPublic);

            if (classifyMethod == null || tileKindType == null)
            {
                Debug.LogError("[CyanClassificationTest] ClassifyPixel or TileKind not found.");
                return;
            }

            var voidValue = Enum.ToObject(tileKindType, 0); // 0 is 'Void' in the enum

            bool pureCyanIgnored = Equals(classifyMethod.Invoke(null, new object[] { new Color32(0, 255, 255, 255) }), voidValue);
            bool nearCyanIgnored = Equals(classifyMethod.Invoke(null, new object[] { new Color32(0, 250, 250, 255) }), voidValue);

            if (pureCyanIgnored && nearCyanIgnored)
            {
                Debug.Log("[CyanClassificationTest] PASS: cyan pixels are classified as Void.");
            }
            else
            {
                Debug.LogError($"[CyanClassificationTest] FAIL: cyanIgnored={pureCyanIgnored}, nearCyanIgnored={nearCyanIgnored}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CyanClassificationTest] Exception: {ex.Message}\n{ex}");
        }
    }
}
#endif