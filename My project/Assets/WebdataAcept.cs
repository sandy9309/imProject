using UnityEngine;
using System.Globalization;

public class WebInterface : MonoBehaviour
{
    public Transform targetFurniture;
    public bool inputUsesCentimeters = true;

    // 這是給 JavaScript 呼叫的函式
    // 注意：參數必須是 string，如果有多個數值，建議用逗號隔開後再解析
    public void ChangeSizeFromWeb(string data)
    {
        Debug.Log("接收到網頁資料: " + data);

        // 假設資料格式是 "長,寬,高" (例如 "120,60,75")
        string[] dimensions = data.Split(',');
        if (dimensions.Length != 3)
        {
            Debug.LogError("尺寸格式錯誤，預期為 x,y,z。");
            return;
        }

        if (!TryParseDimension(dimensions[0], out float x) ||
            !TryParseDimension(dimensions[1], out float y) ||
            !TryParseDimension(dimensions[2], out float z) ||
            x <= 0f || y <= 0f || z <= 0f)
        {
            Debug.LogError("尺寸必須是三個大於零的數字。");
            return;
        }

        float unitScale = inputUsesCentimeters ? 0.01f : 1f;
        ApplyNewSize(new Vector3(x, y, z) * unitScale);
    }

    private bool TryParseDimension(string value, out float result)
    {
        return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
               float.TryParse(value.Trim(), out result);
    }

    public void ApplyNewSize(Vector3 desiredSizeMeters)
    {
        if (targetFurniture == null)
        {
            Debug.LogError("尚未指定要調整尺寸的家具。");
            return;
        }

        Renderer[] renderers = targetFurniture.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("指定家具沒有 Renderer，無法計算目前尺寸。");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f) return;

        Vector3 factor = new Vector3(
            desiredSizeMeters.x / bounds.size.x,
            desiredSizeMeters.y / bounds.size.y,
            desiredSizeMeters.z / bounds.size.z
        );
        targetFurniture.localScale = Vector3.Scale(targetFurniture.localScale, factor);

        AutoFitCollider autoCollider = targetFurniture.GetComponent<AutoFitCollider>();
        if (autoCollider != null) autoCollider.FitCollider();

        if (ModelLoader.Instance != null) ModelLoader.Instance.TriggerAutoSaveDelay();
        Debug.Log($"家具尺寸已更新為 {desiredSizeMeters} 公尺。");
    }
}
