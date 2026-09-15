using UnityEngine;

// --- 這裡定義資料模型 ---
[System.Serializable]
public class FurnitureData
{
    public string modelName;
    public float width;
    public float height;
    public float depth;
}

// --- 這裡才是你的管理類別 ---
public class FurnitureManager : MonoBehaviour
{
    public GameObject perfectCubePrefab; // 拖入你的萬用殼 Prefab

    // 修正後的解析函式
    public void ParseAndSpawn(string jsonResponse)
    {
        // 1. 將字串轉成剛才定義的 FurnitureData 物件
        FurnitureData data = JsonUtility.FromJson<FurnitureData>(jsonResponse);

        // 2. 呼叫生成邏輯 (這裡建議另寫一個 Function 處理組裝)
        SpawnProcess(data);
    }

    private void SpawnProcess(FurnitureData data)
    {
        // 這裡寫你之前的 Instantiate(perfectCubePrefab) 等組裝邏輯
        Debug.Log("準備生成家具：" + data.modelName);
    }
}