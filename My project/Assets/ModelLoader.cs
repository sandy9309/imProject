using UnityEngine;
using UnityEngine.Networking;
using TMPro; // 🌟 引入 TextMeshPro 函式庫
using GLTFast; 
using System.Threading.Tasks;

public class ModelLoader : MonoBehaviour
{
    public static ModelLoader Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    [Header("API 伺服器設定")]
    [Tooltip("基礎 API 網址 (請包含 /furnitures/，但不要包含後面的數字)")]
    public string apiBaseUrl = "http://163.13.202.116:5050/api/projects/"; 

    [Header("VR 搖桿輸入設定")]
    [Tooltip("請把 Unity 裡的 CenterEyeAnchor (頭部攝影機) 拖曳到這裡")]
    public Transform headCamera;
    
    [Tooltip("請在場景建立一個 3D Text (TextMeshPro) 拖拉到這裡 (選號 UI)")]
    public TextMeshPro idDisplay;

    [Tooltip("如果需要除錯，把 ApiTester 用的那個文字拖進來這裡 (非必填)")]
    public TextMeshPro debugText;

    [Header("傢俱互動設定")]
    [Tooltip("請把你設定好「可抓取」的空殼 Prefab 拖曳到這裡！")]
    public GameObject interactablePrefab;

    // 🌟 定義單一個傢俱的資料結構 (包含網址與座標)
    [System.Serializable]
    public class FurnitureData
    {
        public int index; // 新增：資料庫的流水編號
        public string name; // 如果 API 有給 name 就讀得出來
        public string url;
        public float x;
        public float y;
        public float z;
        public float ry; // 新增：Y 軸旋轉
    }

    [System.Serializable]
    public class ServerResponseA { public FurnitureData[] furnitures; }
    
    [System.Serializable]
    public class ServerResponseB { public FurnitureData[] models; }

    // --- 搖桿選號器變數 ---
    private enum MenuState { Closed, SelectProjectID, SelectFurniture }
    private MenuState _menuState = MenuState.Closed;

    private int[] idDigits = new int[4] { 0, 0, 0, 0 };
    private int currentDigitIndex = 3; // 預設停在個位數 (0=千, 1=百, 2=十, 3=個)
    private float joystickCooldown = 0.2f; // 防止搖桿推太快
    private float lastInputTime = 0f;
    
    // --- 傢俱挑選變數 ---
    private FurnitureData[] _fetchedFurnitures = null;
    private int _currentFurnitureIndex = 0;

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

    async void Start()
    {
        if (debugText != null) debugText.text = "";

        if (idDisplay != null) 
        {
            idDisplay.gameObject.SetActive(false);
            // 確保文字置中
            idDisplay.alignment = TextAlignmentOptions.Center;
        }

        Log("⏳ Auto-summoning ID selector in 4 seconds...");
        await Task.Delay(4000); 
        SummonUI();
    }

    void SummonUI()
    {
        if (idDisplay == null || headCamera == null)
        {
            Log("❌ Summon failed: TextMeshPro or CenterEyeAnchor not assigned!");
            return;
        }

        _menuState = MenuState.SelectProjectID;
        idDisplay.gameObject.SetActive(true);
        
        // 放在攝影機前方 0.8 公尺
        idDisplay.transform.position = headCamera.position + headCamera.forward * 0.8f;
        idDisplay.transform.rotation = Quaternion.LookRotation(idDisplay.transform.position - headCamera.position);
        
        UpdateDisplay();
        Log("🎯 Joystick selector summoned! Select ID and press B to confirm.");
    }

    void Update()
    {
        // 啟動時的房間選擇畫面需要獨佔 A/B，避免 B 同時開啟專案選單。
        if (SceneAutoScanner.IsWaitingForChoice) return;

        if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch) || OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            if (_menuState == MenuState.Closed)
            {
                // 如果已經有抓過傢俱資料，B 鍵直接叫出傢俱選單；如果沒有，就叫出 ID 選單
                if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
                {
                    _menuState = MenuState.SelectFurniture;
                    if (idDisplay != null) 
                    {
                        idDisplay.gameObject.SetActive(true);
                        // 更新位置到玩家面前
                        if (headCamera != null)
                        {
                            idDisplay.transform.position = headCamera.position + headCamera.forward * 0.8f;
                            idDisplay.transform.rotation = Quaternion.LookRotation(idDisplay.transform.position - headCamera.position);
                        }
                    }
                    UpdateDisplay();
                }
                else
                {
                    SummonUI();
                }
            }
            else if (_menuState == MenuState.SelectProjectID)
            {
                ConfirmAndFetchAPI();
            }
            else if (_menuState == MenuState.SelectFurniture)
            {
                SpawnSelectedFurniture();
            }
        }

        // 🌟 截圖上傳功能：當玩家按下左手 X 鍵時觸發
        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            StartCoroutine(TakeScreenshotAndUploadRoutine());
        }

        // 處理搖桿邏輯
        if (_menuState != MenuState.Closed && Time.time - lastInputTime > joystickCooldown)
        {
            Vector2 joystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

            if (_menuState == MenuState.SelectProjectID)
            {
                if (Mathf.Abs(joystick.x) > 0.5f)
                {
                    // 左右推動：切換要調整的位數
                    if (joystick.x > 0) currentDigitIndex = Mathf.Min(3, currentDigitIndex + 1);
                    if (joystick.x < 0) currentDigitIndex = Mathf.Max(0, currentDigitIndex - 1);
                    
                    lastInputTime = Time.time;
                    UpdateDisplay();
                }
                else if (Mathf.Abs(joystick.y) > 0.5f)
                {
                    // 上下推動：調整數字 (0~9 循環)
                    if (joystick.y > 0) 
                    {
                        idDigits[currentDigitIndex] = (idDigits[currentDigitIndex] + 1) % 10;
                    }
                    if (joystick.y < 0) 
                    {
                        idDigits[currentDigitIndex] = (idDigits[currentDigitIndex] - 1 + 10) % 10;
                    }

                    lastInputTime = Time.time;
                    UpdateDisplay();
                }
            }
            else if (_menuState == MenuState.SelectFurniture)
            {
                if (joystick.y < -0.5f) // 往下推：返回上一頁 (專案 ID 選擇)
                {
                    _menuState = MenuState.SelectProjectID;
                    lastInputTime = Time.time;
                    UpdateDisplay();
                }
                else if (joystick.y > 0.5f) // 往上推：刪除場景中的這個傢俱
                {
                    if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
                    {
                        var data = _fetchedFurnitures[_currentFurnitureIndex];
                        string suffix = string.IsNullOrEmpty(data.name) ? System.IO.Path.GetFileNameWithoutExtension(data.url) : data.name;
                        string objName = "Furniture_" + suffix;
                        
                        Transform target = this.transform.Find(objName);
                        if (target != null) 
                        {
                            Log($"🗑️ 已從場景中遠端刪除: {objName}");
                            // 🌟 刪除前強制寫入快取，否則它被銷毀後 SavePositionsToDB 會找不到它，導致它下次重生在舊位置！
                            UpdateCacheBeforeDestroy(target);
                            Destroy(target.gameObject);
                            TriggerAutoSaveDelay(); // 延遲存檔
                        }
                        
                        lastInputTime = Time.time;
                    }
                }
                else if (Mathf.Abs(joystick.x) > 0.5f) // 左右推：切換傢俱
                {
                    if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
                    {
                        if (joystick.x > 0) _currentFurnitureIndex = (_currentFurnitureIndex + 1) % _fetchedFurnitures.Length;
                        if (joystick.x < 0) _currentFurnitureIndex = (_currentFurnitureIndex - 1 + _fetchedFurnitures.Length) % _fetchedFurnitures.Length;
                        
                        lastInputTime = Time.time;
                        UpdateDisplay();
                    }
                }
            }
        }
    }

    // 更新畫面上的數字顯示
    void UpdateDisplay()
    {
        if (idDisplay == null) return;

        if (_menuState == MenuState.SelectProjectID)
        {
            string text = "Select Project ID\n<size=50%>(Joystick:↑↓ Adjust ID, ← → Switch, B Confirm)</size>\n\n";
            
            for (int i = 0; i < 4; i++)
            {
                if (i == currentDigitIndex)
                {
                    // 被選中的位數變成綠色，並加上括號標示
                    text += $"<color=#00FF00>[{idDigits[i]}]</color> ";
                }
                else
                {
                    text += $" {idDigits[i]}  ";
                }
            }
            idDisplay.text = text;
        }
        else if (_menuState == MenuState.SelectFurniture)
        {
            if (_fetchedFurnitures == null || _fetchedFurnitures.Length == 0) return;
            
            var data = _fetchedFurnitures[_currentFurnitureIndex];
            string displayName = data.name;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(data.url); 
                if (string.IsNullOrEmpty(displayName)) displayName = "Model " + (_currentFurnitureIndex + 1);
            }
            
            string text = $"Select Furniture ({_currentFurnitureIndex + 1} / {_fetchedFurnitures.Length})\n";
            text += $"<size=50%>(Joystick: ← → Switch, ↑ Delete, ↓ Back, B Spawn)</size>\n\n";
            text += $"<color=#00FF00>{displayName}</color>";
            
            idDisplay.text = text;
        }
    }

    // 確認送出並開始請求 API
    async void ConfirmAndFetchAPI()
    {
        // 🌟 切換專案時，自動清空場景中所有的傢俱！
        Log("🧹 清空舊專案的所有傢俱...");
        foreach (Transform child in this.transform)
        {
            if (child != idDisplay.transform) // 避免不小心把看板自己刪除 (防呆)
            {
                Destroy(child.gameObject);
            }
        }

        // 將 4 個數字組合成真實的 ID (例如 0, 1, 2, 3 -> 123)
        int finalId = idDigits[0] * 1000 + idDigits[1] * 100 + idDigits[2] * 10 + idDigits[3];
        string userId = finalId.ToString();

        string finalApiUrl = apiBaseUrl + userId + "/models";
        Log($"🌐 Fetching API for ID {userId}: {finalApiUrl}");
        
        await FetchApiAndLoadModels(finalApiUrl);
    }

    // ==========================================
    // 步驟一：向伺服器要資料 (API 請求)
    // ==========================================
    async Task FetchApiAndLoadModels(string requestUrl)
    {
        // 因為不再一次全生成，我們只抓資料，不需要清除畫面上的東西！
        Log("⏳ 正在讀取傢俱清單...");

        try
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
            {
                webRequest.timeout = 10;
                var operation = webRequest.SendWebRequest();
                
                var tcs = new TaskCompletionSource<bool>();
                operation.completed += (op) => { tcs.TrySetResult(true); };

                var timeoutTask = Task.Delay(12000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Log("❌ Timeout! No response from server after 12 seconds.");
                    return;
                }

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string jsonString = webRequest.downloadHandler.text;
                    Log("✅ API responded! Parsing...");
                    
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
                        Log($"🌐 Success! Found {targetArray.Length} models.");
                        
                        // 儲存資料，並切換到傢俱挑選模式
                        _fetchedFurnitures = targetArray;
                        _currentFurnitureIndex = 0;
                        _menuState = MenuState.SelectFurniture;
                        UpdateDisplay();
                    } 
                    else 
                    {
                        Log("⚠️ Server responded, but no furniture data found (array empty or name mismatch).");
                    }
                }
                else
                {
                    Log("❌ Failed to connect to API: " + webRequest.error);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log("❌ Crash error: " + ex.Message);
        }
    }

    // 當玩家在選單中按下 B 鍵，就生成目前選中的這個傢俱
    void SpawnSelectedFurniture()
    {
        if (_fetchedFurnitures == null || _fetchedFurnitures.Length == 0) return;
        
        var data = _fetchedFurnitures[_currentFurnitureIndex];
        string filename = string.IsNullOrEmpty(data.name) ? System.IO.Path.GetFileNameWithoutExtension(data.url) : data.name;
        
        // 🌟 印出座標來證明 Unity 是 100% 聽從 API/快取 的數據！
        Log($"✨ 正在生成傢俱: {filename}\n座標: ({data.x:F2}, {data.y:F2}, {data.z:F2})");
        
        _ = LoadModelFromNetwork(data);
        
        // 🌟 關閉選單
        _menuState = MenuState.Closed;
        if (idDisplay != null) idDisplay.gameObject.SetActive(false);
    }

    // ==========================================
    // 步驟二：從網路下載 3D 模型並設定座標
    // ==========================================
    async Task LoadModelFromNetwork(FurnitureData data)
    {
        string objectName = "Furniture";
        string suffix = string.IsNullOrEmpty(data.name) ? System.IO.Path.GetFileNameWithoutExtension(data.url) : data.name;
        try { objectName += "_" + suffix; } catch { }
        
        // 🌟 檢查場景中是否已經有同名的傢俱，如果有，就把它刪除，實現「取代」的效果！
        Transform oldTransform = this.transform.Find(objectName);
        if (oldTransform != null)
        {
            Destroy(oldTransform.gameObject);
        }
        
        GameObject rootObject;
        
        // 🌟 如果你有放入設定好抓取功能的 Prefab，就以此 Prefab 作為外殼！
        if (interactablePrefab != null)
        {
            rootObject = Instantiate(interactablePrefab);
            rootObject.name = objectName;
        }
        else
        {
            rootObject = new GameObject(objectName);
        }
        
        // 將外殼設定為 NetworkModelManager 的子物件
        rootObject.transform.SetParent(this.transform); 
        
        // 🌟 智慧座標判斷：如果資料庫傳來的是 (0,0,0)，代表這是全新沒擺過的傢俱，我們把它生在玩家面前！
        // 如果不是 (0,0,0)，代表有存過位置，就乖乖待在存過的世界座標上。
        if (data.x == 0f && data.y == 0f && data.z == 0f && headCamera != null)
        {
            Vector3 spawnPos = headCamera.position + headCamera.forward * 1.0f;
            rootObject.transform.position = spawnPos;
            
            // 讓新傢俱面向玩家
            Vector3 lookDir = rootObject.transform.position - headCamera.position;
            lookDir.y = 0; 
            if (lookDir != Vector3.zero) rootObject.transform.rotation = Quaternion.LookRotation(lookDir);
        }
        else
        {
            rootObject.transform.position = new Vector3(data.x, data.y, data.z);
            rootObject.transform.rotation = Quaternion.Euler(0, data.ry, 0);
        }

        // 🌟 掛上標籤，記錄這件傢俱在資料庫裡的流水號 (index)
        FurnitureTag tag = rootObject.AddComponent<FurnitureTag>();
        tag.index = data.index;
        tag.url = data.url;
        
        // 🌟 強制將外殼的縮放比例重置為 1，避免 Prefab 殘留的縮小設定影響到新傢俱
        rootObject.transform.localScale = Vector3.one;
        
        // 🌟 如果樣板裡已經有預留叫 Visuals 的空物件，就直接使用它；否則才新建
        Transform existingVisuals = rootObject.transform.Find("Visuals");
        GameObject modelVisuals;
        if (existingVisuals != null)
        {
            modelVisuals = existingVisuals.gameObject;
        }
        else
        {
            modelVisuals = new GameObject("Visuals");
            modelVisuals.transform.SetParent(rootObject.transform);
            modelVisuals.transform.localPosition = Vector3.zero;
            modelVisuals.transform.localRotation = Quaternion.identity;
        }
        // 🌟 【超級防禦機制】：在模型下載與組裝期間，強制關閉物理引擎！
        // 為什麼要這樣做？因為預設的方塊碰撞體 (BoxCollider) 可能會跟地板或牆壁重疊。
        // 如果在下載這幾秒內沒關物理，Unity 會以為傢俱卡在牆裡，把它猛力「彈飛」！這就是桌子隨機出現的元凶！
        Rigidbody rb = rootObject.GetComponent<Rigidbody>();
        bool wasKinematic = false;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true; 
        }
        
        var gltf = new GltfImport();
        bool success = await gltf.Load(data.url);

        if (success)
        {
            // 將模型的外觀塞進 Visuals 子物件裡
            success = await gltf.InstantiateMainSceneAsync(modelVisuals.transform);
            
            if (success)
            {
                // 🌟 【單一完美網格合併系統】(CombineMeshes)
                // 把所有散落的小零件熔合在一起，生成一個完美的單一網格碰撞體
                MeshFilter[] meshFilters = modelVisuals.GetComponentsInChildren<MeshFilter>();
                CombineInstance[] combine = new CombineInstance[meshFilters.Length];

                int i = 0;
                while (i < meshFilters.Length)
                {
                    if (meshFilters[i].sharedMesh != null)
                    {
                        combine[i].mesh = meshFilters[i].sharedMesh;
                        // 轉換矩陣，確保合併後的小零件都在正確的相對位置上
                        combine[i].transform = rootObject.transform.worldToLocalMatrix * meshFilters[i].transform.localToWorldMatrix;
                    }
                    i++;
                }

                // 建立一個全新的、合併後的數學網格
                Mesh combinedMesh = new Mesh();
                // 開啟 32-bit index 支援，避免高精度模型頂點數超過 65535 時報錯
                combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; 
                combinedMesh.CombineMeshes(combine, true, true);

                // 在根目錄加上單一的 MeshCollider
                MeshCollider rootMeshCol = rootObject.AddComponent<MeshCollider>();
                rootMeshCol.sharedMesh = combinedMesh;
                rootMeshCol.convex = true; // 必須開啟 convex 才能與其他剛體碰撞、被物理系統支援

                // 1. 重新綁定 Unity XR Interaction Toolkit
                var xriGrab = rootObject.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (xriGrab != null)
                {
                    xriGrab.colliders.Clear();
                    xriGrab.colliders.Add(rootMeshCol);
                }

                // 2. 黑客級修復：用 Reflection 強制綁定 Meta XR ISDK
                Component[] allComponents = rootObject.GetComponentsInChildren<Component>(true);
                foreach (Component comp in allComponents)
                {
                    if (comp == null) continue;

                    // 尋找名為 ColliderSurface 的元件，這是 Meta XR 處理射線抓取的核心
                    if (comp.GetType().Name == "ColliderSurface")
                    {
                        // 找出 InjectCollider 方法
                        var method = comp.GetType().GetMethod("InjectCollider");
                        if (method != null) 
                        {
                            // 強行把我們剛生成的網格碰撞體塞給它！
                            method.Invoke(comp, new object[] { rootMeshCol });
                        }
                    }
                }

                // 3. 安全大掃除：移除舊有大方形 BoxCollider，避免它擋住我們精細的網格
                BoxCollider boxCol = rootObject.GetComponent<BoxCollider>();
                if (boxCol != null) Destroy(boxCol);

                // 🌟 模型完全就位，網格也完美貼合了，現在可以把物理引擎的鎖解開了！
                if (rb != null)
                {
                    rb.isKinematic = wasKinematic;
                }

                Log($"✅ Model loaded! Position: ({data.x}, {data.y}, {data.z})");
            }
        }
        else
        {
            Log($"❌ Model download failed! URL: {data.url}");
            Destroy(rootObject); 
        }
    }

    // 🌟 在物件被刪除 (Destroy) 之前，強制把它的最後位置寫入記憶體快取
    public void UpdateCacheBeforeDestroy(Transform target)
    {
        if (_fetchedFurnitures == null) return;
        
        FurnitureTag tag = target.GetComponent<FurnitureTag>();
        if (tag != null)
        {
            for (int i = 0; i < _fetchedFurnitures.Length; i++)
            {
                // 嚴謹雙重比對：確保即時快取正確更新
                if (_fetchedFurnitures[i].index == tag.index && _fetchedFurnitures[i].url == tag.url)
                {
                    _fetchedFurnitures[i].x = target.position.x;
                    _fetchedFurnitures[i].y = target.position.y;
                    _fetchedFurnitures[i].z = target.position.z;
                    _fetchedFurnitures[i].ry = target.eulerAngles.y;
                    break;
                }
            }
        }
    }

    // ==========================================
    // 自動儲存座標 API
    // ==========================================
    [System.Serializable]
    public class PosItem
    {
        public int index;
        public float x;
        public float y;
        public float z;
        public float ry;
    }

    [System.Serializable]
    public class PosBody
    {
        public System.Collections.Generic.List<PosItem> positions;
    }

    public void TriggerAutoSave()
    {
        _ = SavePositionsToDB();
    }

    public async void TriggerAutoSaveDelay(int delayMs = 1000)
    {
        // 延遲指定時間 (預設 1 秒) 後自動存檔，確保物理慣性已經停下
        await Task.Delay(delayMs); 
        _ = SavePositionsToDB();
    }

    // ==========================================
    // 截圖與上傳功能
    // ==========================================
    private bool isTakingScreenshot = false;

    private System.Collections.IEnumerator TakeScreenshotAndUploadRoutine()
    {
        if (isTakingScreenshot) yield break;
        isTakingScreenshot = true;

        Log("📸 正在擷取畫面，請保持頭部穩定...");

        // 確保當前幀的畫面已經完全渲染完畢
        yield return new WaitForEndOfFrame();

        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Log("❌ 找不到主相機，無法截圖！");
            isTakingScreenshot = false;
            yield break;
        }

        // 建立一台臨時的虛擬相機，複製玩家的視角
        GameObject camObj = new GameObject("ScreenshotCamera");
        Camera snapCam = camObj.AddComponent<Camera>();
        snapCam.CopyFrom(mainCam);
        
        // 🌟 藍圖模式：將背景換成深藍色設計圖風格
        snapCam.clearFlags = CameraClearFlags.SolidColor;
        snapCam.backgroundColor = new Color(0.04f, 0.15f, 0.28f, 1f); // 深色藍圖藍

        // 🌟 藍圖模式：為真實房間的牆壁與障礙物產生藍色方塊
        EnableBlueprintMode();

        // 建立一張 1920x1080 的高畫質渲染畫布
        RenderTexture rt = new RenderTexture(1920, 1080, 24);
        snapCam.targetTexture = rt;

        // 命令相機拍下這一瞬間的畫面
        snapCam.Render();

        // 拍完立刻清理藍圖方塊
        DisableBlueprintMode();

        // 將畫布轉換為可處理的 2D 圖片
        RenderTexture.active = rt;
        Texture2D screenShot = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        screenShot.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        screenShot.Apply();

        // 卸載並清除記憶體
        snapCam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        Destroy(camObj);

        // 將圖片編碼為 JPG 格式 (85% 品質，在畫質與上傳速度間取得平衡)
        byte[] imageBytes = screenShot.EncodeToJPG(85);
        Destroy(screenShot);

        Log("🚀 畫面擷取完成，準備上傳至伺服器...");

        // 呼叫非同步上傳 API
        _ = UploadScreenshotToDB(imageBytes);
        
        isTakingScreenshot = false;
    }

    private async Task UploadScreenshotToDB(byte[] imageBytes)
    {
        // 取得當前輸入的專案 ID
        int finalId = idDigits[0] * 1000 + idDigits[1] * 100 + idDigits[2] * 10 + idDigits[3];
        string userId = finalId.ToString();
        string uploadUrl = $"http://163.13.202.116:5050/api/projects/{userId}/media";

        // 準備 MultipartFormData
        var formData = new System.Collections.Generic.List<IMultipartFormSection>();
        formData.Add(new MultipartFormDataSection("type", "screenshot"));
        
        // 檔名加上當前時間戳記
        string fileName = $"vr_screenshot_{System.DateTime.Now:yyyyMMdd_HHmmss}.jpg";
        formData.Add(new MultipartFormFileSection("file", imageBytes, fileName, "image/jpeg"));

        try
        {
            using (UnityWebRequest req = UnityWebRequest.Post(uploadUrl, formData))
            {
                var operation = req.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Log("✅ 截圖上傳成功！網頁端已可預覽");
                    Debug.Log("Upload Response: " + req.downloadHandler.text);
                }
                else
                {
                    Log($"❌ 上傳失敗: {req.error}");
                    Debug.LogError("Upload Error: " + req.error + "\nResponse: " + req.downloadHandler.text);
                }
            }
        }
        catch (System.Exception e)
        {
            Log($"❌ 上傳發生例外錯誤: {e.Message}");
        }
    }

    // ==========================================
    // 藍圖模式 (Blueprint Mode) 工具
    // ==========================================
    private System.Collections.Generic.List<GameObject> blueprintBoxes = new System.Collections.Generic.List<GameObject>();

    private void EnableBlueprintMode()
    {
        // 如果沒有 MRUK 或尚未掃描房間，就跳過
        if (Meta.XR.MRUtilityKit.MRUK.Instance == null) return;
        var room = Meta.XR.MRUtilityKit.MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        // 建立半透明科技藍色材質
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material bpMat = new Material(shader);
        if (bpMat.HasProperty("_BaseColor")) bpMat.SetColor("_BaseColor", new Color(0.2f, 0.6f, 1f, 0.4f));
        if (bpMat.HasProperty("_Color")) bpMat.SetColor("_Color", new Color(0.2f, 0.6f, 1f, 0.4f));

        foreach (var anchor in room.Anchors)
        {
            // 根據 MRUK 錨點的大小，建立一個方塊來代表現實世界的牆壁與傢俱
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>()); // 藍圖僅供拍照，不需要碰撞
            box.GetComponent<MeshRenderer>().material = bpMat;
            
            box.transform.SetParent(anchor.transform, false);
            
            if (anchor.VolumeBounds.HasValue)
            {
                // 如果是立體的傢俱 (如桌子、沙發)
                box.transform.localPosition = anchor.VolumeBounds.Value.center;
                box.transform.localScale = anchor.VolumeBounds.Value.size;
            }
            else if (anchor.PlaneRect.HasValue)
            {
                // 如果是平面的牆壁、地板
                box.transform.localPosition = new Vector3(anchor.PlaneRect.Value.center.x, anchor.PlaneRect.Value.center.y, 0);
                box.transform.localScale = new Vector3(anchor.PlaneRect.Value.width, anchor.PlaneRect.Value.height, 0.01f);
            }
            
            blueprintBoxes.Add(box);
        }
    }

    private void DisableBlueprintMode()
    {
        foreach (var box in blueprintBoxes)
        {
            if (box != null) Destroy(box);
        }
        blueprintBoxes.Clear();
    }

    private async Task SavePositionsToDB()
    {
        if (_fetchedFurnitures == null) return;

        // 將 4 個數字組合成真實的 ID
        int finalId = idDigits[0] * 1000 + idDigits[1] * 100 + idDigits[2] * 10 + idDigits[3];
        string userId = finalId.ToString();
        string putUrl = apiBaseUrl + userId + "/positions";
        
        var list = new System.Collections.Generic.List<PosItem>();
        
        foreach (Transform child in this.transform)
        {
            if (idDisplay != null && child == idDisplay.transform) continue;

            FurnitureTag tag = child.GetComponent<FurnitureTag>();
            if (tag != null)
            {
                float newX = child.position.x;
                float newY = child.position.y;
                float newZ = child.position.z;
                float newRy = child.eulerAngles.y;

                // 🌟 同步更新記憶體裡的暫存資料，這樣刪除後重新叫出才會是最新的位置！
                // 這裡改用 index + url 雙重嚴謹比對，絕對不會把 A 桌子的座標存到 B 椅子身上！
                for (int i = 0; i < _fetchedFurnitures.Length; i++)
                {
                    if (_fetchedFurnitures[i].index == tag.index && _fetchedFurnitures[i].url == tag.url)
                    {
                        _fetchedFurnitures[i].x = newX;
                        _fetchedFurnitures[i].y = newY;
                        _fetchedFurnitures[i].z = newZ;
                        _fetchedFurnitures[i].ry = newRy;
                        break;
                    }
                }

                list.Add(new PosItem {
                    index = tag.index,
                    x = newX,
                    y = newY,
                    z = newZ,
                    ry = newRy
                });
            }
        }

        string json = JsonUtility.ToJson(new PosBody { positions = list });
        Log($"💾 Auto-saving {list.Count} items...");

        try
        {
            using (var req = new UnityWebRequest(putUrl, "PUT"))
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                var operation = req.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Log("✅ 座標已自動儲存成功！");
                }
                else
                {
                    Log("❌ 座標自動儲存失敗: " + req.error);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log("❌ 自動儲存發生錯誤: " + ex.Message);
        }
    }
}

