#if UNITY_EDITOR
using UnityEngine;
using UnityAI;

public static class ColliderBuilder
{
    public static void Build(StackDefinition definition, GameObject root, float[,] tileHeights)
    {
        if (definition == null || root == null || tileHeights == null)
            return;

        var collisionTex = definition.Layers.collision;
        if (!collisionTex)
            return;

        float cell = definition.metersPerPixel;
        float halfW = collisionTex.width * cell * 0.5f;
        float halfH = collisionTex.height * cell * 0.5f;

        var walkParent = new GameObject("Collision_Walkable");
        walkParent.transform.SetParent(root.transform, false);
        var blockedParent = new GameObject("Collision_Blocked");
        blockedParent.transform.SetParent(root.transform, false);
        var climbParent = new GameObject("Collision_Climbable");
        climbParent.transform.SetParent(root.transform, false);
        var destructParent = new GameObject("Collision_Destructible");
        destructParent.transform.SetParent(root.transform, false);

        var pixels = collisionTex.GetPixels32();
        for (int y = 0; y < collisionTex.height; y++)
        {
            for (int x = 0; x < collisionTex.width; x++)
            {
                var col = pixels[y * collisionTex.width + x];
                var kind = definition.ClassifyCollision(col);
                if (kind == CollisionKind.Unknown)
                    continue;

                Vector3 center = new Vector3(x * cell - halfW + cell * 0.5f, tileHeights[x, y], halfH - y * cell - cell * 0.5f);
                switch (kind)
                {
                    case CollisionKind.Walkable:
                        CreateBoxCollider(walkParent.transform, center + Vector3.up * 0.05f, new Vector3(cell, 0.1f, cell));
                        break;
                    case CollisionKind.Blocked:
                        CreateBoxCollider(blockedParent.transform, center + Vector3.up * (definition.wallHeight * 0.5f), new Vector3(cell, definition.wallHeight, cell));
                        break;
                    case CollisionKind.Climbable:
                        var ladder = CreateBoxCollider(climbParent.transform, center + Vector3.up * (definition.wallHeight * 0.5f), new Vector3(cell * 0.6f, definition.wallHeight, cell * 0.4f));
                        ladder.isTrigger = true;
                        break;
                    case CollisionKind.Destructible:
                        var destruct = CreateBoxCollider(destructParent.transform, center + Vector3.up * (definition.wallHeight * 0.5f), new Vector3(cell, definition.wallHeight, cell));
                        var marker = destruct.gameObject.GetComponent<DestructibleMarker>() ?? destruct.gameObject.AddComponent<DestructibleMarker>();
                        marker.sourcePixel = new Vector2Int(x, y);
                        break;
                }
            }
        }
    }

    private static BoxCollider CreateBoxCollider(Transform parent, Vector3 center, Vector3 size)
    {
        var go = new GameObject("ColliderTile");
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var collider = go.AddComponent<BoxCollider>();
        collider.size = size;
        return collider;
    }
}
#endif
