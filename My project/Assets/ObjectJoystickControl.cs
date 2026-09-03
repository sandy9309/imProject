using UnityEngine;

// Compatibility for existing prefabs. No input polling, transform writes or freeze coroutine.
public class ObjectJoystickControl : MonoBehaviour
{
    [HideInInspector] public Transform visualModel;
    [HideInInspector] public float pushPullSpeed = 1f;
    [HideInInspector] public float rotationSpeed = 90f;
    [HideInInspector] public float wallBuffer = 0.1f;
    [HideInInspector] public LayerMask obstacleLayers = ~0;
    private System.Collections.IEnumerator Start()
    {
        if (GetComponent<FurnitureTag>() != null) yield break;
        while (SceneAutoScanner.PlacementWalls.Count == 0) yield return null;
        if (GetComponent<FurniturePlacementController>() != null || visualModel == null) yield break;
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) yield break;
        var validator = GetComponent<FurnitureWallCollisionGuard>();
        if (validator == null) validator = gameObject.AddComponent<FurnitureWallCollisionGuard>();
        validator.Configure(box, visualModel);
    }
    public void TriggerDelayFreeze() => GetComponent<FurniturePlacementController>()?.RequestSettle();
}
