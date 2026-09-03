using UnityEngine;

// Existing button bindings submit to the same placement entry point.
public class FurnitureSnapping : MonoBehaviour
{
    [Min(1f)] public float rotationStep = 90f;
    public void Rotate90Degrees() => GetComponent<FurniturePlacementController>()?.RequestRotation(rotationStep);
}
