using UnityEngine;

public class WebInterface : MonoBehaviour
{
    // 這是給 JavaScript 呼叫的函式
    // 注意：參數必須是 string，如果有多個數值，建議用逗號隔開後再解析
    public void ChangeSizeFromWeb(string data)
    {
        Debug.Log("接收到網頁資料: " + data);

        // 假設資料格式是 "長,寬,高" (例如 "120,60,75")
        string[] dimensions = data.Split(',');
        if (dimensions.Length == 3)
        {
            float x = float.Parse(dimensions[0]);
            float y = float.Parse(dimensions[1]);
            float z = float.Parse(dimensions[2]);

            // 呼叫你修改家具尺寸的邏輯
            ApplyNewSize(x, y, z);
        }
    }

    void ApplyNewSize(float x, float y, float z)
    {
        // 這裡放你縮放家具的程式碼
        // 例如：GameObject.Find("Table").transform.localScale = new Vector3(x, y, z);
    }
}