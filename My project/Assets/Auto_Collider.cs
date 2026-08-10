using UnityEngine;

public class AutoFitCollider : MonoBehaviour
{
    [Range(0.8f, 1.0f)]
    public float paddingFactor = 0.95f; // 新增：縮減係數，預設縮小 5%

    [ContextMenu("Fit Collider to Children")]
    public void FitCollider()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();

        Bounds bounds = new Bounds();
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        bool hasBounds = false;
        foreach (Renderer render in renderers)
        {
            if (render.gameObject == gameObject) continue;

            if (!hasBounds)
            {
                bounds = render.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(render.bounds);
            }
        }

        if (hasBounds)
        {
            boxCollider.center = transform.InverseTransformPoint(bounds.center);
            
            // --- 關鍵修改：將尺寸乘以縮減係數 ---
            Vector3 localSize = transform.InverseTransformVector(bounds.size);
            boxCollider.size = localSize * paddingFactor; 
        }
    }
}