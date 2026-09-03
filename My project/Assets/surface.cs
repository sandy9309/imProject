using UnityEngine;

// Reserved prefab settings. Snapping is disabled for the current placement mode.
public class SurfaceSnapper : MonoBehaviour
{
    [HideInInspector] public LayerMask surfaceLayer;
    [HideInInspector] public float offsetFromWall = 0.05f;
    [HideInInspector] public float snapDistance = 0.3f;
    [HideInInspector] public bool snapEnabled = false;
}
