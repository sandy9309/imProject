using UnityEngine;

public class FurnitureSnapping : MonoBehaviour
{
    [Min(1f)] public float rotationStep = 90f;

    public void Rotate90Degrees()
    {
        Quaternion originalRotation = transform.rotation;
        float targetY = Mathf.Repeat(transform.eulerAngles.y + rotationStep, 360f);
        Quaternion targetRotation = Quaternion.Euler(0f, targetY, 0f);

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.rotation = targetRotation;
            rb.angularVelocity = Vector3.zero;
        }
        transform.rotation = targetRotation;
        Physics.SyncTransforms();

        if (OverlapsOtherFurniture())
        {
            if (rb != null) rb.rotation = originalRotation;
            transform.rotation = originalRotation;
            Physics.SyncTransforms();
        }
    }

    private bool OverlapsOtherFurniture()
    {
        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider == null) return false;

        Bounds bounds = ownCollider.bounds;
        Collider[] hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            transform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            FurnitureTag tag = hit.GetComponentInParent<FurnitureTag>();
            if (tag != null && tag.transform.root != transform.root) return true;
        }

        return false;
    }
}
