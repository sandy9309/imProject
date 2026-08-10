using UnityEngine;
using UnityEngine.Networking;
using TMPro; 
using GLTFast; 
using System.Threading.Tasks;

public class ApiTester : MonoBehaviour
{
    [Header("測試設定")]
    public string testApiUrl = "http://163.13.202.116:5050/api/projects/29/models";
    
    [Tooltip("請綁定一個 3D 文字，用來在眼鏡裡顯示除錯訊息")]
    public TextMeshPro debugText; 

    // 定義單一個傢俱的資料結構
    [System.Serializable]
    public class FurnitureData
    {
        public string url;
        public float x;
        public float y;
        public float z;
    }

    // 嘗試定義兩種可能的回傳結構 (因為不知道同學把陣列名稱取叫 furnitures 還是 models)
    [System.Serializable]
    public class ServerResponseA { public FurnitureData[] furnitures; }
    
    [System.Serializable]
    public class ServerResponseB { public FurnitureData[] models; }

    async void Start()
    {
        // 先把字清空
        if (debugText != null) debugText.text = "";
        
        Log("⏳ Testing API: " + testApiUrl);
        await TestApi();
    }

    // 方便把訊息同時印在 Console 和眼鏡裡的 3D 文字上
    void Log(string msg)
    {
        Debug.Log(msg);
        if (debugText != null)
        {
            debugText.text = msg + "\n\n" + debugText.text;
            if (debugText.text.Length > 800) debugText.text = debugText.text.Substring(0, 800);
        }
    }

    async Task TestApi()
    {
        try
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(testApiUrl))
            {
                // 設定 10 秒 Timeout
                webRequest.timeout = 10;
                
                var operation = webRequest.SendWebRequest();
                
                // 使用 TaskCompletionSource 來避免死鎖
                var tcs = new TaskCompletionSource<bool>();
                operation.completed += (op) => { tcs.TrySetResult(true); };

                var timeoutTask = Task.Delay(12000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Log("❌ Timeout! No response after 12s. Check Android HTTP settings or WiFi.");
                    return;
                }

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string jsonString = webRequest.downloadHandler.text;
                    
                    string preview = jsonString.Length > 150 ? jsonString.Substring(0, 150) + "..." : jsonString;
                    Log("✅ Connected! Data:\n<color=#FFFF00>" + preview + "</color>");
                    
                    FurnitureData[] targetArray = null;
                    
                    ServerResponseA dataA = JsonUtility.FromJson<ServerResponseA>(jsonString);
                    if (dataA != null && dataA.furnitures != null && dataA.furnitures.Length > 0) targetArray = dataA.furnitures;
                    
                    if (targetArray == null)
                    {
                        ServerResponseB dataB = JsonUtility.FromJson<ServerResponseB>(jsonString);
                        if (dataB != null && dataB.models != null && dataB.models.Length > 0) targetArray = dataB.models;
                    }

                    if (targetArray != null)
                    {
                        Log($"✅ Parsed! Found {targetArray.Length} items.");
                        Log("⏳ Downloading first model...");
                        
                        _ = LoadModelFromNetwork(targetArray[0]);
                    }
                    else
                    {
                        Log("❌ JSON parse failed! Check array names in yellow text above.");
                    }
                }
                else
                {
                    Log("❌ Connection failed: " + webRequest.error);
                }
            }
        }
        catch (System.Exception e)
        {
            Log("❌ Crash error: " + e.Message);
        }
    }

    async Task LoadModelFromNetwork(FurnitureData data)
    {
        GameObject modelContainer = new GameObject("TestFurniture");
        
        // 為了確保你一定看得到，把它固定生在你正前方 1 公尺，高度與眼睛切齊
        Transform head = Camera.main != null ? Camera.main.transform : transform;
        modelContainer.transform.position = head.position + head.forward * 1.0f;
        
        var gltf = new GltfImport();
        bool success = await gltf.Load(data.url);

        if (success)
        {
            success = await gltf.InstantiateMainSceneAsync(modelContainer.transform);
            if (success) Log($"✅ Model loaded successfully!");
        }
        else
        {
            Log($"❌ Model download failed! URL: {data.url}");
        }
    }
}
