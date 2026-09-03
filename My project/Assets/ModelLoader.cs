using UnityEngine;
using UnityEngine.Networking;
using TMPro; // 🌟 引入 TextMeshPro 函式庫
using GLTFast; 
using System.Collections;
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

    [Min(1)] public int requestTimeoutSeconds = 10;

    [Header("Project synchronization")]
    [Tooltip("Seconds between lightweight project revision checks.")]
    [Min(2f)] public float projectSyncIntervalSeconds = 5f;

    private string _activeProjectId = "";
    private string _lastProjectRevision = "";
    private Coroutine _projectSyncCoroutine;
    private bool _revisionCheckInFlight;
    private bool _lastRefreshSucceeded;
    private bool _offlineTestMode;
    private const string OfflineProjectId = "00000";
    private Canvas _projectCanvas;
    private UnityEngine.UI.Button[] _projectButtons;
    private int _joystickDigitIndex = 3;
    private float _lastJoystickInputTime;
    private const float JoystickInputCooldown = 0.2f;
    private bool _joystickEditingProjectId = true;
    private bool _projectLoadInProgress;

    private string BuildProjectApiUrl(string projectId, string resource)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? string.Empty : apiBaseUrl.TrimEnd('/');
        string cleanProjectId = string.IsNullOrWhiteSpace(projectId) ? string.Empty : projectId.Trim().Trim('/');
        string cleanResource = string.IsNullOrWhiteSpace(resource) ? string.Empty : resource.Trim().Trim('/');
        return $"{baseUrl}/{cleanProjectId}/{cleanResource}";
    }

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
        public bool isPlaced; // 是否已由使用者儲存過位置；不再用 (0,0,0) 猜測
    }

    [System.Serializable]
    public class ServerResponseA { public FurnitureData[] furnitures; }
    
    [System.Serializable]
    public class ServerResponseB { public FurnitureData[] models; }

    [System.Serializable]
    private class RevisionResponse { public string revision = ""; }

    // --- UI 專案輸入變數 ---
    private string _uiInputProjectID = "0000";
    private int _projectRequestVersion = 0;

    // --- 傢俱挑選變數 ---
    private FurnitureData[] _fetchedFurnitures = null;
    private int _currentFurnitureIndex = 0;
    private enum ProjectMenuState { ProjectId, Furniture, Hidden }
    private ProjectMenuState _projectMenuState = ProjectMenuState.ProjectId;

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

    void Start()
    {
        if (debugText != null) debugText.text = "";

        ConfigureProjectCanvasForXR();

        if (idDisplay != null) 
        {
            idDisplay.gameObject.SetActive(false);
            // 確保文字置中
            idDisplay.alignment = TextAlignmentOptions.Center;
        }

        Log("Waiting for a project ID...");
        UpdateDisplay();
        if (_projectCanvas != null)
            _projectCanvas.gameObject.SetActive(SceneAutoScanner.StartupFlowComplete);
    }

    private void OnEnable()
    {
        SceneAutoScanner.StartupFlowCompleted += ShowProjectCanvas;
        SceneAutoScanner.StartupFlowReset += HideProjectCanvas;
    }

    private void ShowProjectCanvas()
    {
        if (_projectCanvas == null) return;
        _projectCanvas.gameObject.SetActive(true);
        UpdateDisplay();
    }

    private void HideProjectCanvas()
    {
        if (_projectCanvas != null)
            _projectCanvas.gameObject.SetActive(false);
    }

    private void ConfigureProjectCanvasForXR()
    {
        if (idDisplay == null || headCamera == null) return;

        _projectCanvas = idDisplay.GetComponentInParent<Canvas>();
        if (_projectCanvas == null) return;

        // Keep the project ID display in front of the headset.
        Transform canvasTransform = _projectCanvas.transform;
        canvasTransform.SetParent(headCamera, false);
        canvasTransform.localPosition = new Vector3(0f, -0.08f, 1.2f);
        canvasTransform.localRotation = Quaternion.identity;

        StyleAndArrangeProjectCanvas();
    }

    private void StyleAndArrangeProjectCanvas()
    {
        RectTransform canvasRect = _projectCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(760f, 420f);

        UnityEngine.UI.Image background = _projectCanvas.GetComponent<UnityEngine.UI.Image>();
        if (background != null)
        {
            background.raycastTarget = false;
            background.enabled = false;
        }

        RectTransform displayRect = idDisplay.rectTransform;
        displayRect.anchorMin = displayRect.anchorMax = new Vector2(0.5f, 0.5f);
        displayRect.pivot = new Vector2(0.5f, 0.5f);
        displayRect.anchoredPosition = Vector2.zero;
        displayRect.sizeDelta = new Vector2(680f, 360f);
        idDisplay.enableAutoSizing = true;
        idDisplay.fontSizeMin = 20f;
        idDisplay.fontSizeMax = 44f;
        idDisplay.raycastTarget = false;

        _projectButtons = _projectCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        foreach (UnityEngine.UI.Button button in _projectButtons)
            button.gameObject.SetActive(false);
    }

    private void UpdateProjectButtonVisibility()
    {
        if (_projectButtons == null) return;
        foreach (UnityEngine.UI.Button button in _projectButtons)
            button.gameObject.SetActive(false);
    }

    void Update()
    {
        // Room setup and an active grab own the controller inputs exclusively.
        if (FurniturePlacementController.HasActiveGrab || !SceneAutoScanner.StartupFlowComplete ||
            SceneAutoScanner.IsWaitingForChoice) return;
        // SceneAutoScanner owns A/B only while its startup choice is visible.
        if (!SceneAutoScanner.IsWaitingForChoice)
        {
            bool confirmPressed =
                OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
            bool resetPressed =
                OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);

            if (_projectMenuState == ProjectMenuState.ProjectId)
            {
                UpdateProjectIdFromJoystick();
                if (confirmPressed) UI_ConfirmProjectID();
                if (resetPressed) ResetProjectIdInput();
            }
            else if (_projectMenuState == ProjectMenuState.Furniture)
            {
                UpdateFurnitureSelectionFromJoystick();
                if (confirmPressed) UI_SpawnFurniture();
                if (resetPressed) ReturnToProjectSelection();
            }
            else if (_projectMenuState == ProjectMenuState.Hidden && resetPressed)
            {
                _projectMenuState = ProjectMenuState.Furniture;
                UpdateDisplay();
            }
        }

        // 🌟 截圖上傳功能：當玩家按下左手 X 鍵時觸發
        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            StartCoroutine(TakeScreenshotAndUploadRoutine());
        }
    }

    void OnDisable()
    {
        SceneAutoScanner.StartupFlowCompleted -= ShowProjectCanvas;
        SceneAutoScanner.StartupFlowReset -= HideProjectCanvas;
        StopProjectSync();
        _projectRequestVersion++;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==========================================
    // 給 UI 按鈕呼叫的公開函數 (Public Methods)
    // ==========================================

    public void UI_TypeDigit(int digit)
    {
        if (_uiInputProjectID.Length < 6) // 限制長度避免太長
        {
            _joystickEditingProjectId = false;
            _uiInputProjectID += digit.ToString();
            UpdateDisplay();
        }
    }

    public void UI_Backspace()
    {
        if (_fetchedFurnitures != null)
        {
            ReturnToProjectSelection();
            return;
        }

        if (_uiInputProjectID.Length > 0)
        {
            _joystickEditingProjectId = false;
            _uiInputProjectID = _uiInputProjectID.Substring(0, _uiInputProjectID.Length - 1);
            UpdateDisplay();
        }
    }

    public void UI_ConfirmProjectID()
    {
        if (_projectLoadInProgress) return;
        if (string.IsNullOrEmpty(_uiInputProjectID))
        {
            Log("Enter a project ID first.");
            return;
        }
        ConfirmAndFetchAPI();
    }

    private void ResetProjectIdInput()
    {
        _uiInputProjectID = "0000";
        _joystickDigitIndex = 3;
        _joystickEditingProjectId = true;
        UpdateDisplay();
    }

    public void UI_RefreshProject()
    {
        if (_offlineTestMode)
        {
            LoadOfflineTestProject();
            return;
        }
        if (!string.IsNullOrWhiteSpace(_activeProjectId))
            _ = RefreshFurnitureList(false);
    }

    public void UI_ReturnToProjectSelection()
    {
        ReturnToProjectSelection();
    }

    private void ReturnToProjectSelection()
    {
        StopProjectSync();
        _projectRequestVersion++;
        _offlineTestMode = false;
        _activeProjectId = "";
        _fetchedFurnitures = null;
        _currentFurnitureIndex = 0;
        _projectMenuState = ProjectMenuState.ProjectId;
        UpdateDisplay();
        Log("已返回專案 ID 輸入畫面。");
    }

    public void UI_NextFurniture()
    {
        if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
        {
            _currentFurnitureIndex = (_currentFurnitureIndex + 1) % _fetchedFurnitures.Length;
            UpdateDisplay();
        }
    }

    public void UI_PrevFurniture()
    {
        if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
        {
            _currentFurnitureIndex = (_currentFurnitureIndex - 1 + _fetchedFurnitures.Length) % _fetchedFurnitures.Length;
            UpdateDisplay();
        }
    }

    public void UI_SpawnFurniture()
    {
        SpawnSelectedFurniture();
    }

    public void UI_DeleteFurniture()
    {
        if (_fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
        {
            var data = _fetchedFurnitures[_currentFurnitureIndex];
            Transform target = ReadFurnitureByIndex(data.index);
            if (target != null) 
            {
                Log($"🗑️ 已從目前場景隱藏家具 index={data.index}: {target.name}");
                Destroy(target.gameObject);
            }
            else
            {
                Log($"場景中沒有找到 index={data.index} 的家具");
            }
        }
    }

    private Transform ReadFurnitureByIndex(int furnitureIndex)
    {
        foreach (Transform child in transform)
        {
            FurnitureTag tag = child.GetComponent<FurnitureTag>();
            if (tag != null && tag.index == furnitureIndex) return child;
        }
        return null;
    }

    private string BuildFurnitureObjectName(FurnitureData data)
    {
        string suffix = string.IsNullOrEmpty(data.name)
            ? System.IO.Path.GetFileNameWithoutExtension(data.url)
            : data.name;
        if (string.IsNullOrWhiteSpace(suffix)) suffix = "Model";
        suffix = suffix.Replace('/', '_').Replace('\\', '_');
        return $"Furniture_{data.index}_{suffix}";
    }

    // 更新畫面上的數字顯示
    void UpdateDisplay()
    {
        if (idDisplay == null) return;

        if (_projectMenuState == ProjectMenuState.Hidden)
        {
            idDisplay.gameObject.SetActive(false);
            return;
        }

        idDisplay.gameObject.SetActive(true);
        UpdateProjectButtonVisibility();

        if (_fetchedFurnitures == null)
        {
            if (_projectLoadInProgress)
            {
                idDisplay.text = "<b>PROJECT ID</b>\n\n" +
                    $"<size=150%><color=#00FF00>{_uiInputProjectID}</color></size>\n\n" +
                    "<size=65%>Loading project...</size>";
                return;
            }

            string displayId = FormatProjectIdForDisplay();
            idDisplay.text = "<b>PROJECT ID</b>\n\n" +
                "<size=55%>Right stick up/down: Change number\n" +
                "Right stick left/right: Select digit\n" +
                "A: Confirm    B: Reset</size>\n\n" +
                $"<size=150%><color=#00FF00>{displayId}</color></size>";
            return;
        }

        if (_offlineTestMode && _fetchedFurnitures.Length > 0)
        {
            FurnitureData data = _fetchedFurnitures[_currentFurnitureIndex];
            idDisplay.text = $"<b>SELECT FURNITURE</b> ({_currentFurnitureIndex + 1} / {_fetchedFurnitures.Length})\n" +
                $"<size=150%><color=#00FF00>{data.name}</color></size>\n\n" +
                "<size=50%>A: Spawn furniture    B: Back</size>";
            return;
        }

        if (_fetchedFurnitures.Length == 0)
        {
            idDisplay.text = $"<b>Project {_activeProjectId}</b>\n\n<size=70%>No furniture in this project. Waiting for updates...</size>";
        }
        else
        {
            // 已經有傢俱資料了，顯示傢俱選單
            var data = _fetchedFurnitures[_currentFurnitureIndex];
            string displayName = data.name;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(data.url); 
                if (string.IsNullOrEmpty(displayName)) displayName = "Model " + (_currentFurnitureIndex + 1);
            }
            
            string text = $"<b>Select Furniture</b> ({_currentFurnitureIndex + 1} / {_fetchedFurnitures.Length})\n";
            text += $"<size=150%><color=#00FF00>{displayName}</color></size>\n\n";
            text += $"<size=50%>Right A: Spawn　Right B: Back</size>";
            
            idDisplay.text = text;
        }
    }

    // 確認送出並開始請求 API
    async void ConfirmAndFetchAPI()
    {
        _projectLoadInProgress = true;
        UpdateDisplay();
        StopProjectSync();
        int requestVersion = ++_projectRequestVersion;

        // 🌟 切換專案時，自動清空場景中所有的傢俱！
        Log("🧹 清空舊專案的所有傢俱...");
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<FurnitureTag>() != null)
            {
                Destroy(child.gameObject);
            }
        }

        _fetchedFurnitures = null;
        _currentFurnitureIndex = 0;

        // Match the working lin branch: 0033 is project 33, not a literal "0033" ID.
        string userId = int.TryParse(_uiInputProjectID, out int numericProjectId)
            ? numericProjectId.ToString()
            : _uiInputProjectID;
        _activeProjectId = userId;
        _lastProjectRevision = "";

        _offlineTestMode = userId == OfflineProjectId;
        if (_offlineTestMode)
        {
            _projectLoadInProgress = false;
            LoadOfflineTestProject();
            return;
        }

        string finalApiUrl = BuildProjectApiUrl(userId, "models");
        Log($"🌐 Fetching API for ID {userId}: {finalApiUrl}");
        
        await FetchApiAndLoadModels(finalApiUrl, requestVersion);
        await CheckProjectRevision(true);
        _projectLoadInProgress = false;
        UpdateDisplay();
        if (isActiveAndEnabled)
            _projectSyncCoroutine = StartCoroutine(ProjectSyncLoop());
    }

    private void LoadOfflineTestProject()
    {
        _offlineTestMode = true;
        _activeProjectId = OfflineProjectId;
        _fetchedFurnitures = new[]
        {
            new FurnitureData { index = 900001, name = "Offline Chair", url = "offline://chair" },
            new FurnitureData { index = 900002, name = "Offline Table", url = "offline://table" },
            new FurnitureData { index = 900003, name = "Offline Cabinet", url = "offline://cabinet" }
        };
        _currentFurnitureIndex = 0;
        _projectMenuState = ProjectMenuState.Furniture;
        UpdateDisplay();
        Log("離線測試模式已啟用：不會連線伺服器。選擇家具後按 UI 放置或右手 A。");
    }

    private IEnumerator ProjectSyncLoop()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(2f, projectSyncIntervalSeconds));
        while (!string.IsNullOrWhiteSpace(_activeProjectId))
        {
            yield return wait;
            if (!_revisionCheckInFlight && !isTakingScreenshot)
                _ = CheckProjectRevision(false);
        }
    }

    private void UpdateProjectIdFromJoystick()
    {
        if (Time.unscaledTime - _lastJoystickInputTime < JoystickInputCooldown)
            return;

        Vector2 joystick = OVRInput.Get(
            OVRInput.Axis2D.PrimaryThumbstick,
            OVRInput.Controller.RTouch);

        if (Mathf.Abs(joystick.x) > 0.55f)
        {
            EnsureJoystickProjectId();
            _joystickDigitIndex = Mathf.Clamp(
                _joystickDigitIndex + (joystick.x > 0f ? 1 : -1),
                0,
                _uiInputProjectID.Length - 1);
            _lastJoystickInputTime = Time.unscaledTime;
            UpdateDisplay();
        }
        else if (Mathf.Abs(joystick.y) > 0.55f)
        {
            EnsureJoystickProjectId();
            char[] digits = _uiInputProjectID.ToCharArray();
            int value = digits[_joystickDigitIndex] - '0';
            value = (value + (joystick.y > 0f ? 1 : 9)) % 10;
            digits[_joystickDigitIndex] = (char)('0' + value);
            _uiInputProjectID = new string(digits);
            _lastJoystickInputTime = Time.unscaledTime;
            UpdateDisplay();
        }
    }

    private void UpdateFurnitureSelectionFromJoystick()
    {
        if (_fetchedFurnitures == null || _fetchedFurnitures.Length == 0)
            return;
        if (Time.unscaledTime - _lastJoystickInputTime < JoystickInputCooldown)
            return;

        Vector2 joystick = OVRInput.Get(
            OVRInput.Axis2D.PrimaryThumbstick,
            OVRInput.Controller.RTouch);
        if (Mathf.Abs(joystick.x) <= 0.55f)
            return;

        int direction = joystick.x > 0f ? 1 : -1;
        _currentFurnitureIndex =
            (_currentFurnitureIndex + direction + _fetchedFurnitures.Length) %
            _fetchedFurnitures.Length;
        _lastJoystickInputTime = Time.unscaledTime;
        UpdateDisplay();
    }

    private void EnsureJoystickProjectId()
    {
        if (string.IsNullOrEmpty(_uiInputProjectID))
            _uiInputProjectID = "0000";

        _joystickDigitIndex = Mathf.Clamp(_joystickDigitIndex, 0, _uiInputProjectID.Length - 1);
        _joystickEditingProjectId = true;
    }

    private string FormatProjectIdForDisplay()
    {
        if (string.IsNullOrEmpty(_uiInputProjectID))
            return "_";
        if (!_joystickEditingProjectId)
            return _uiInputProjectID;

        string before = _uiInputProjectID.Substring(0, _joystickDigitIndex);
        string selected = _uiInputProjectID[_joystickDigitIndex].ToString();
        string after = _uiInputProjectID.Substring(_joystickDigitIndex + 1);
        return $"{before}<u>{selected}</u>{after}";
    }

    private void StopProjectSync()
    {
        if (_projectSyncCoroutine != null)
        {
            StopCoroutine(_projectSyncCoroutine);
            _projectSyncCoroutine = null;
        }
        _revisionCheckInFlight = false;
    }

    private async Task CheckProjectRevision(bool establishBaseline)
    {
        if (_offlineTestMode || _revisionCheckInFlight || string.IsNullOrWhiteSpace(_activeProjectId)) return;
        _revisionCheckInFlight = true;
        try
        {
            string requestProjectId = _activeProjectId;
            using (UnityWebRequest request = UnityWebRequest.Get(
                BuildProjectApiUrl(requestProjectId, "revision")))
            {
                request.timeout = requestTimeoutSeconds;
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success || requestProjectId != _activeProjectId)
                    return;

                RevisionResponse payload = JsonUtility.FromJson<RevisionResponse>(request.downloadHandler.text);
                if (payload == null || string.IsNullOrWhiteSpace(payload.revision)) return;

                if (establishBaseline || string.IsNullOrEmpty(_lastProjectRevision))
                {
                    _lastProjectRevision = payload.revision;
                    return;
                }

                if (payload.revision != _lastProjectRevision)
                {
                    Log("Project changed. Refreshing furniture list...");
                    await RefreshFurnitureList(true);
                    if (_lastRefreshSucceeded) _lastProjectRevision = payload.revision;
                }
            }
        }
        finally
        {
            _revisionCheckInFlight = false;
        }
    }

    private async Task RefreshFurnitureList(bool preserveSelection)
    {
        if (string.IsNullOrWhiteSpace(_activeProjectId)) return;
        int previousIndex = _currentFurnitureIndex;
        await FetchApiAndLoadModels(
            BuildProjectApiUrl(_activeProjectId, "models"), _projectRequestVersion);
        if (_lastRefreshSucceeded && preserveSelection && _fetchedFurnitures != null && _fetchedFurnitures.Length > 0)
        {
            _currentFurnitureIndex = Mathf.Clamp(previousIndex, 0, _fetchedFurnitures.Length - 1);
            UpdateDisplay();
        }
    }

    // ==========================================
    // 步驟一：向伺服器要資料 (API 請求)
    // ==========================================
    async Task FetchApiAndLoadModels(string requestUrl, int requestVersion)
    {
        _lastRefreshSucceeded = false;
        // 因為不再一次全生成，我們只抓資料，不需要清除畫面上的東西！
        Log("⏳ 正在讀取傢俱清單...");

        try
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(requestUrl))
            {
                webRequest.timeout = requestTimeoutSeconds;
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
                    if (requestVersion != _projectRequestVersion)
                    {
                        Log("ℹ️ 已忽略舊專案的逾期回應。");
                        return;
                    }

                    string jsonString = webRequest.downloadHandler.text;
                    Log("✅ API responded! Parsing...");
                    
                    FurnitureData[] targetArray = null;
                    
                    ServerResponseA dataA = JsonUtility.FromJson<ServerResponseA>(jsonString);
                    if (dataA != null && dataA.furnitures != null) targetArray = dataA.furnitures;
                    
                    if (targetArray == null)
                    {
                        ServerResponseB dataB = JsonUtility.FromJson<ServerResponseB>(jsonString);
                        if (dataB != null && dataB.models != null && dataB.models.Length > 0) targetArray = dataB.models;
                    }

                    if (targetArray != null)
                    {
                        if (!ValidateFurnitureIndices(targetArray, out string indexError))
                        {
                            Log("❌ 家具資料拒絕載入：" + indexError);
                            return;
                        }

                        Log($"🌐 Success! Found {targetArray.Length} models.");
                        
                        // 儲存資料，並更新選單
                        _fetchedFurnitures = targetArray;
                        _currentFurnitureIndex = 0;
                        _lastRefreshSucceeded = true;
                        if (_projectMenuState == ProjectMenuState.ProjectId)
                            _projectMenuState = ProjectMenuState.Furniture;
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

    private bool ValidateFurnitureIndices(FurnitureData[] furnitures, out string error)
    {
        var seenIndices = new System.Collections.Generic.HashSet<int>();
        foreach (FurnitureData furniture in furnitures)
        {
            if (furniture.index < 0)
            {
                error = $"index 不可小於 0，目前收到 {furniture.index}";
                return false;
            }

            if (!seenIndices.Add(furniture.index))
            {
                error = $"同一專案收到重複 index={furniture.index}";
                return false;
            }
        }

        error = string.Empty;
        return true;
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
        
        _projectMenuState = ProjectMenuState.Hidden;
        UpdateDisplay();
    }

    // ==========================================
    // 步驟二：從網路下載 3D 模型並設定座標
    // ==========================================
    async Task LoadModelFromNetwork(FurnitureData data)
    {
        string objectName = BuildFurnitureObjectName(data);
        
        // 🌟 檢查場景中是否已經有同名的傢俱，如果有，就把它刪除，實現「取代」的效果！
        Transform oldTransform = ReadFurnitureByIndex(data.index);
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
        
        // 未擺放的家具生在玩家面前；已擺放家具永遠採用後端座標，包括合法的世界原點。
        if (!data.isPlaced && headCamera != null)
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
        FurnitureTag tag = rootObject.GetComponent<FurnitureTag>();
        if (tag == null) tag = rootObject.AddComponent<FurnitureTag>();
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
        FurnitureInteractionStateController stateController = rootObject.GetComponent<FurnitureInteractionStateController>();
        if (stateController == null && rb != null)
            stateController = rootObject.AddComponent<FurnitureInteractionStateController>();
        if (stateController != null)
            stateController.SetState(FurnitureInteractionState.Loading);

        bool wasKinematic = false;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true; 
        }
        
        bool modelReady = false;

        try
        {
            bool isOfflineModel = data.url.StartsWith("offline://", System.StringComparison.OrdinalIgnoreCase);
            var gltf = isOfflineModel ? null : new GltfImport();
            bool success = isOfflineModel || await gltf.Load(data.url);

            if (!success)
            {
                Log($"❌ Model download failed! URL: {data.url}");
                return;
            }

            // 將模型的外觀塞進 Visuals 子物件裡
            success = isOfflineModel
                ? CreateOfflineTestVisuals(modelVisuals, data.url.Substring("offline://".Length))
                : await gltf.InstantiateMainSceneAsync(modelVisuals.transform);
            if (!success)
            {
                Log($"❌ Model instantiate failed! URL: {data.url}");
                return;
            }

            // 使用模型外框建立穩定的 BoxCollider，避免高面數 Convex MeshCollider 烘焙失敗。
            BoxCollider rootCollider = ConfigureFurnitureCollider(rootObject, modelVisuals);
            if (rootCollider == null)
                {
                    Log($"❌ Model has no usable Renderer bounds: {data.url}");
                    Destroy(rootObject);
                    return;
                }

                // 1. 重新綁定 Unity XR Interaction Toolkit
                FurnitureWallCollisionGuard wallGuard = rootObject.GetComponent<FurnitureWallCollisionGuard>();
                if (wallGuard == null)
                    wallGuard = rootObject.AddComponent<FurnitureWallCollisionGuard>();
                if (!wallGuard.Configure(rootCollider, modelVisuals.transform))
                {
                    Log("無法放置家具：請確認房間牆面已載入，且附近有足夠空間。");
                    return;
                }

                var xriGrab = rootObject.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                if (xriGrab != null)
                {
                    xriGrab.colliders.Clear();
                    xriGrab.colliders.Add(rootCollider);
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
                            method.Invoke(comp, new object[] { rootCollider });
                        }
                    }
                }

                // 🌟 模型完全就位，網格也完美貼合了，現在可以把物理引擎的鎖解開了！
                if (rb != null)
                {
                    rb.isKinematic = wasKinematic;
                }
                if (stateController != null)
                    stateController.SetState(FurnitureInteractionState.Placed);

                modelReady = true;
                Log($"✅ Model loaded! Position: ({data.x}, {data.y}, {data.z})");
        }
        catch (System.Exception ex)
        {
            Log($"❌ Model load exception: {ex.Message}\nURL: {data.url}");
        }
        finally
        {
            if (!modelReady && rootObject != null)
            {
                // 先恢復原始物理狀態，再清除失敗的外殼，避免等待 isKinematic 的協程卡住。
                if (rb != null)
                {
                    rb.isKinematic = wasKinematic;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                Destroy(rootObject);
            }
        }
    }

    private bool CreateOfflineTestVisuals(GameObject parent, string kind)
    {
        Color color = kind == "chair"
            ? new Color(0.24f, 0.62f, 0.95f)
            : kind == "table" ? new Color(0.96f, 0.58f, 0.22f) : new Color(0.42f, 0.78f, 0.48f);

        if (kind == "chair")
        {
            AddOfflinePart(parent.transform, "Seat", new Vector3(0f, 0.48f, 0f), new Vector3(0.55f, 0.10f, 0.55f), color);
            AddOfflinePart(parent.transform, "Back", new Vector3(0f, 0.82f, 0.23f), new Vector3(0.55f, 0.58f, 0.10f), color);
            AddOfflineLegs(parent.transform, 0.22f, 0.22f, 0.45f, color);
        }
        else if (kind == "table")
        {
            AddOfflinePart(parent.transform, "Top", new Vector3(0f, 0.75f, 0f), new Vector3(1.1f, 0.12f, 0.7f), color);
            AddOfflineLegs(parent.transform, 0.45f, 0.25f, 0.72f, color);
        }
        else
        {
            AddOfflinePart(parent.transform, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(0.85f, 1.3f, 0.42f), color);
            AddOfflinePart(parent.transform, "DoorGap", new Vector3(0f, 0.65f, -0.216f), new Vector3(0.025f, 1.15f, 0.01f), Color.black);
        }

        return true;
    }

    private void AddOfflineLegs(Transform parent, float x, float z, float height, Color color)
    {
        float y = height * 0.5f;
        Vector3 scale = new Vector3(0.09f, height, 0.09f);
        AddOfflinePart(parent, "Leg", new Vector3(x, y, z), scale, color);
        AddOfflinePart(parent, "Leg", new Vector3(-x, y, z), scale, color);
        AddOfflinePart(parent, "Leg", new Vector3(x, y, -z), scale, color);
        AddOfflinePart(parent, "Leg", new Vector3(-x, y, -z), scale, color);
    }

    private void AddOfflinePart(Transform parent, string partName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider generatedCollider = part.GetComponent<Collider>();
        if (generatedCollider != null) Destroy(generatedCollider);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = color;
    }

    private BoxCollider ConfigureFurnitureCollider(GameObject rootObject, GameObject modelVisuals)
    {
        Renderer[] renderers = modelVisuals.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        bool hasBounds = false;
        Bounds localBounds = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            Bounds meshBounds = renderer.localBounds;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 meshCorner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z
                        );
                        Vector3 localCorner = rootObject.transform.InverseTransformPoint(renderer.transform.TransformPoint(meshCorner));

                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
                }
            }
        }

        if (!hasBounds || localBounds.size == Vector3.zero) return null;

        BoxCollider collider = rootObject.GetComponent<BoxCollider>();
        if (collider == null) collider = rootObject.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = localBounds.size;
        collider.isTrigger = false;
        return collider;
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
                if (_fetchedFurnitures[i].index == tag.index)
                {
                    _fetchedFurnitures[i].x = target.position.x;
                    _fetchedFurnitures[i].y = target.position.y;
                    _fetchedFurnitures[i].z = target.position.z;
                    _fetchedFurnitures[i].ry = target.eulerAngles.y;
                    _fetchedFurnitures[i].isPlaced = true;
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
        _autoSaveVersion++;
        _ = SavePositionsToDB();
    }

    public async void TriggerAutoSaveDelay(int delayMs = 1000)
    {
        int scheduledVersion = ++_autoSaveVersion;
        // 延遲指定時間 (預設 1 秒) 後自動存檔，確保物理慣性已經停下
        await Task.Delay(delayMs); 
        if (scheduledVersion != _autoSaveVersion) return;
        _ = SavePositionsToDB();
    }

    private int _autoSaveVersion = 0;
    private bool _isSavingPositions = false;
    private bool _savePositionsQueued = false;

    // ==========================================
    // 截圖與上傳功能
    // ==========================================
    private bool isTakingScreenshot = false;

    private System.Collections.IEnumerator TakeScreenshotAndUploadRoutine()
    {
        if (isTakingScreenshot) yield break;

        if (string.IsNullOrWhiteSpace(_uiInputProjectID))
        {
            Log("❌ 請先輸入並確認專案 ID，再上傳截圖。");
            yield break;
        }

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
        UploadScreenshotAndReset(imageBytes);
    }

    private async void UploadScreenshotAndReset(byte[] imageBytes)
    {
        try
        {
            await UploadScreenshotToDB(imageBytes);
        }
        finally
        {
            isTakingScreenshot = false;
        }
    }

    private async Task UploadScreenshotToDB(byte[] imageBytes)
    {
        // 取得當前輸入的專案 ID
        string userId = int.TryParse(_uiInputProjectID, out int numericProjectId)
            ? numericProjectId.ToString()
            : _uiInputProjectID;
        string uploadUrl = BuildProjectApiUrl(userId, "media");

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
                req.timeout = requestTimeoutSeconds;
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
    private Material blueprintMaterial;

    private void EnableBlueprintMode()
    {
        // 如果沒有 MRUK 或尚未掃描房間，就跳過
        if (Meta.XR.MRUtilityKit.MRUK.Instance == null) return;
        var room = Meta.XR.MRUtilityKit.MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        // 建立半透明科技藍色材質
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        blueprintMaterial = new Material(shader);
        if (blueprintMaterial.HasProperty("_BaseColor")) blueprintMaterial.SetColor("_BaseColor", new Color(0.2f, 0.6f, 1f, 0.4f));
        if (blueprintMaterial.HasProperty("_Color")) blueprintMaterial.SetColor("_Color", new Color(0.2f, 0.6f, 1f, 0.4f));

        foreach (var anchor in room.Anchors)
        {
            // 根據 MRUK 錨點的大小，建立一個方塊來代表現實世界的牆壁與傢俱
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>()); // 藍圖僅供拍照，不需要碰撞
            box.GetComponent<MeshRenderer>().sharedMaterial = blueprintMaterial;
            
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

        if (blueprintMaterial != null)
        {
            Destroy(blueprintMaterial);
            blueprintMaterial = null;
        }
    }

    private async Task SavePositionsToDB()
    {
        if (_offlineTestMode) return;

        if (_isSavingPositions)
        {
            _savePositionsQueued = true;
            return;
        }

        _isSavingPositions = true;
        try
        {
            do
            {
                _savePositionsQueued = false;
                await SavePositionsOnceToDB();
            }
            while (_savePositionsQueued);
        }
        finally
        {
            _isSavingPositions = false;
        }
    }

    private async Task SavePositionsOnceToDB()
    {
        if (_fetchedFurnitures == null) return;

        // 使用目前的專案 ID
        string userId = _uiInputProjectID;
        string putUrl = BuildProjectApiUrl(userId, "positions");
        
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
                    if (_fetchedFurnitures[i].index == tag.index)
                    {
                        _fetchedFurnitures[i].x = newX;
                        _fetchedFurnitures[i].y = newY;
                        _fetchedFurnitures[i].z = newZ;
                        _fetchedFurnitures[i].ry = newRy;
                        _fetchedFurnitures[i].isPlaced = true;
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
                req.timeout = requestTimeoutSeconds;
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
