using UnityEngine;
using UnityEngine.Networking;
using TMPro; // 🌟 引入 TextMeshPro 函式庫
using GLTFast; 
using System.Collections;
using System.Threading.Tasks;

public class ModelLoader : MonoBehaviour
{
    public static ModelLoader Instance { get; private set; }
    public bool HasActiveProject => _lastRefreshSucceeded && !string.IsNullOrWhiteSpace(_activeProjectId);

    void Awake()
    {
        Instance = this;
    }

    [Header("API Server Settings")]
    [Tooltip("Base API URL ending with /projects/")]
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
    private const string OfflineProjectId = "99999";
    private Canvas _projectCanvas;
    private UnityEngine.UI.Button[] _projectButtons;
    private int _joystickDigitIndex = 4;
    private float _lastJoystickInputTime;
    private const float JoystickInputCooldown = 0.2f;
    private bool _joystickEditingProjectId = true;
    private bool _projectLoadInProgress;
    private bool _returnInProgress;
    private bool _serverSupportsCoordinateSpace;
    private bool _screenshotYHeld;
    private int _screenshotStatusVersion;
    private Meta.XR.PassthroughCameraAccess _screenshotPassthroughCamera;

    private string BuildProjectApiUrl(string projectId, string resource)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? string.Empty : apiBaseUrl.TrimEnd('/');
        string cleanProjectId = string.IsNullOrWhiteSpace(projectId) ? string.Empty : projectId.Trim().Trim('/');
        string cleanResource = string.IsNullOrWhiteSpace(resource) ? string.Empty : resource.Trim().Trim('/');
        return $"{baseUrl}/{cleanProjectId}/{cleanResource}";
    }

    [Header("VR Controller Input")]
    [Tooltip("Assign the CenterEyeAnchor camera here")]
    public Transform headCamera;
    
    [Tooltip("Assign the project selection TextMeshPro object here")]
    public TextMeshPro idDisplay;

    [Tooltip("Optional debug TextMeshPro output")]
    public TextMeshPro debugText;

    [Header("Furniture Interaction")]
    [Tooltip("Assign the configured grabbable prefab here")]
    public GameObject interactablePrefab;

    // 🌟 定義單一個傢俱的資料結構 (包含網址與座標)
    [System.Serializable]
    public class FurnitureData
    {
        public int item_id;
        public int furniture_id;
        public int index; // 原有的陣列索引
        public string name; 
        public string url;
        public float x;
        public float y;
        public float z;
        public float ry; 
        public bool isPlaced; 
        public string coordinateSpace;
    }

    [System.Serializable]
    public class ServerResponseA { 
        public string projectId;
        public string revision;
        public FurnitureData[] furnitures; 
    }
    
    [System.Serializable]
    public class ServerResponseB { 
        public string projectId;
        public string revision;
        public FurnitureData[] models; 
    }

    [System.Serializable]
    private class RevisionResponse { public string revision = ""; }

    // --- UI 專案輸入變數 ---
    private string _uiInputProjectID = "00000";
    private int _projectRequestVersion = 0;

    // --- 傢俱挑選變數 ---
    private FurnitureData[] _fetchedFurnitures = null;
    private int _currentFurnitureIndex = 0;
    private enum ProjectMenuState { ProjectId, Furniture, Hidden }
    private ProjectMenuState _projectMenuState = ProjectMenuState.ProjectId;
    private System.Collections.Generic.HashSet<int> _knownFurnitureIndices = new System.Collections.Generic.HashSet<int>();
    private bool _isFirstFetchOfProject = true;

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

        ConfigureScreenshotPassthroughCamera();
    }

    private void ConfigureScreenshotPassthroughCamera()
    {
        _screenshotPassthroughCamera = FindObjectOfType<Meta.XR.PassthroughCameraAccess>();
        if (_screenshotPassthroughCamera != null) return;

        // Quest 的現實畫面屬於系統合成層，普通 Unity Camera 無法擷取；
        // 使用左側彩色相機提供可寫入截圖的現實環境影像。
        _screenshotPassthroughCamera = gameObject.AddComponent<Meta.XR.PassthroughCameraAccess>();
        _screenshotPassthroughCamera.CameraPosition =
            Meta.XR.PassthroughCameraAccess.CameraPositionType.Left;
        _screenshotPassthroughCamera.RequestedResolution = new Vector2Int(1280, 960);
    }

    private void OnEnable()
    {
        SceneAutoScanner.StartupFlowCompleted += ShowProjectCanvas;
        SceneAutoScanner.StartupFlowReset += HideProjectCanvas;
    }

    private float _canvasEnableTime;

    private void ShowProjectCanvas()
    {
        if (_projectCanvas == null) return;
        _projectCanvas.gameObject.SetActive(true);
        _canvasEnableTime = Time.time;
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
        canvasTransform.localScale = Vector3.one * 0.0006f;

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
        // 使用按住狀態自行判斷第一次按下，避免部分 Quest 執行環境漏掉 GetDown 事件。
        bool screenshotYHeld = OVRInput.Get(OVRInput.RawButton.Y) ||
            OVRInput.Get(OVRInput.Button.Four, OVRInput.Controller.LTouch);
        bool screenshotPressed = HasActiveProject && screenshotYHeld && !_screenshotYHeld;
        _screenshotYHeld = screenshotYHeld;
        if (screenshotPressed)
            StartCoroutine(TakeScreenshotAndUploadRoutine());

        if (_returnInProgress) return;
        // Room setup and an active grab own the controller inputs exclusively.
        if (FurniturePlacementController.HasActiveGrab || !SceneAutoScanner.StartupFlowComplete ||
            SceneAutoScanner.IsWaitingForChoice) return;
        // SceneAutoScanner owns A/B only while its startup choice is visible.
        if (!SceneAutoScanner.IsWaitingForChoice && Time.time - _canvasEnableTime > 0.5f)
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
                
                if (OVRInput.GetDown(OVRInput.RawButton.X))
                {
                    var scanner = FindObjectOfType<SceneAutoScanner>();
                    if (scanner != null) scanner.ReturnToRoomSetup();
                    return;
                }
            }
            else if (_projectMenuState == ProjectMenuState.Furniture)
            {
                UpdateFurnitureSelectionFromJoystick();
                if (confirmPressed) UI_SpawnFurniture();
                if (resetPressed) ReturnToProjectSelection();
                // 左手 X 保留刪除功能，左手 Y 負責截圖，避免兩個功能互相覆蓋。
                if (OVRInput.GetDown(OVRInput.RawButton.X))
                {
                    UI_DeleteFurniture();
                    return;
                }
            }
            else if (_projectMenuState == ProjectMenuState.Hidden && resetPressed)
            {
                _projectMenuState = ProjectMenuState.Furniture;
                UpdateDisplay();
            }
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
        _uiInputProjectID = "00000";
        _joystickDigitIndex = 4;
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

    private async void ReturnToProjectSelection()
    {
        if (_returnInProgress) return;
        _returnInProgress = true;
        StopProjectSync();
        _projectRequestVersion++;
        Log("Saving the current project layout...");
        // Finish the last drag/rotation save before removing the objects that
        // provide their positions. This makes rapid project switching reliable.
        if (!_offlineTestMode && !string.IsNullOrWhiteSpace(_activeProjectId))
        {
            _autoSaveVersion++;
            while (_isSavingPositions) await Task.Yield();
            await SavePositionsOnceToDB();
        }
        ClearSpawnedFurniture();
        _offlineTestMode = false;
        _activeProjectId = "";
        _fetchedFurnitures = null;
        _currentFurnitureIndex = 0;
        _projectMenuState = ProjectMenuState.ProjectId;
        _returnInProgress = false;
        UpdateDisplay();
        Log("Returned to project ID entry.");
    }

    private void ClearSpawnedFurniture()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<FurnitureTag>() == null) continue;
            // Disable immediately so an object waiting for end-of-frame Destroy cannot
            // remain visible or keep contributing a collision blocker on the ID screen.
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
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
                Log($"🗑️ Furniture removed from the scene. index={data.index}: {target.name}");
                // Destroy 會等到幀末才真正移除；先停用可避免下一次刪除又找到同一件家具。
                target.gameObject.SetActive(false);
                Destroy(target.gameObject);
                UpdateDisplay();
            }
            else
            {
                Log($"Furniture index={data.index} was not found in the scene.");
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
                "A: Confirm    B: Reset    X: Back</size>\n\n" +
                $"<size=150%><color=#00FF00>{displayId}</color></size>";
            return;
        }

        if (_offlineTestMode && _fetchedFurnitures.Length > 0)
        {
            FurnitureData data = _fetchedFurnitures[_currentFurnitureIndex];
            idDisplay.text = $"<b>SELECT FURNITURE</b> ({_currentFurnitureIndex + 1} / {_fetchedFurnitures.Length})\n" +
                $"<size=150%><color=#00FF00>{data.name}</color></size>\n\n" +
                "<size=50%>A: Spawn　B: Back　Left X: Delete\nLeft Y: Screenshot</size>";
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

            int newItemCount = 0;
            foreach (var f in _fetchedFurnitures)
            {
                if (_knownFurnitureIndices != null && !_knownFurnitureIndices.Contains(f.index))
                    newItemCount++;
            }

            if (_knownFurnitureIndices != null && !_knownFurnitureIndices.Contains(data.index))
            {
                displayName = "<color=#FFFF00>[NEW]</color> " + displayName;
            }
            
            string updateHint = newItemCount > 0 ? $"<size=70%><color=#FFA500>Project updated: {newItemCount} new item(s)</color></size>\n" : "";
            string text = $"<b>Select Furniture</b> ({_currentFurnitureIndex + 1} / {_fetchedFurnitures.Length})\n{updateHint}";
            text += $"<size=150%><color=#00FF00>{displayName}</color></size>\n\n";
            text += $"<size=50%>Right A: Spawn　Right B: Back　Left X: Delete\nLeft Y: Screenshot</size>";
            
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
        Log("🧹 Clearing furniture from the previous project...");
        ClearSpawnedFurniture();

        _fetchedFurnitures = null;
        _currentFurnitureIndex = 0;
        _knownFurnitureIndices.Clear();
        _isFirstFetchOfProject = true;

        // The user entered code
        string code = _uiInputProjectID;
        _lastProjectRevision = "";

        _offlineTestMode = code == OfflineProjectId;
        if (_offlineTestMode)
        {
            _activeProjectId = OfflineProjectId;
            _projectLoadInProgress = false;
            LoadOfflineTestProject();
            return;
        }

        string finalApiUrl = BuildProjectApiUrl($"by-code/{code}", "models");
        Log($"🌐 Fetching API by code {code}: {finalApiUrl}");
        
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
        Log("Offline test mode enabled. Select furniture and press the UI button or Right A.");
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
            _uiInputProjectID = "00000";

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
        Log("⏳ Loading furniture list...");

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
                        Log("ℹ️ Ignored an outdated response from the previous project.");
                        return;
                    }

                    string jsonString = webRequest.downloadHandler.text;
                    Log("✅ API responded! Parsing...");
                    // 正式新版 API 會明確回傳 isPlaced 與 coordinateSpace；目前線上舊版
                    // 沒有這兩欄，因此必須先辨識伺服器能力，不能直接相信 bool 預設值 false。
                    bool hasPlacementFlag = jsonString.IndexOf("\"isPlaced\"", System.StringComparison.Ordinal) >= 0;
                    _serverSupportsCoordinateSpace = jsonString.IndexOf(
                        "\"coordinateSpace\"", System.StringComparison.Ordinal) >= 0;
                    
                    FurnitureData[] targetArray = null;
                    string parsedProjectId = null;
                    string parsedRevision = null;
                    
                    ServerResponseA dataA = JsonUtility.FromJson<ServerResponseA>(jsonString);
                    if (dataA != null && dataA.furnitures != null) 
                    {
                        targetArray = dataA.furnitures;
                        parsedProjectId = dataA.projectId;
                        parsedRevision = dataA.revision;
                    }
                    
                    if (targetArray == null)
                    {
                        ServerResponseB dataB = JsonUtility.FromJson<ServerResponseB>(jsonString);
                        if (dataB != null && dataB.models != null && dataB.models.Length > 0) 
                        {
                            targetArray = dataB.models;
                            parsedProjectId = dataB.projectId;
                            parsedRevision = dataB.revision;
                        }
                    }

                    if (targetArray != null)
                    {
                        if (!string.IsNullOrEmpty(parsedProjectId))
                        {
                            _activeProjectId = parsedProjectId;
                        }
                        if (!string.IsNullOrEmpty(parsedRevision) && _isFirstFetchOfProject)
                        {
                            _lastProjectRevision = parsedRevision;
                        }
                        // 舊 API 沒有 isPlaced。舊資料只要任一位置或角度不是 0，就代表曾在
                        // VR 中擺放過；重新進入專案時應自動還原，而不是只留在家具清單。
                        if (!hasPlacementFlag)
                        {
                            foreach (FurnitureData furniture in targetArray)
                                furniture.isPlaced = Mathf.Abs(furniture.x) > 0.0001f ||
                                    Mathf.Abs(furniture.y) > 0.0001f || Mathf.Abs(furniture.z) > 0.0001f ||
                                    Mathf.Abs(furniture.ry) > 0.0001f;
                        }
                        if (!ValidateFurnitureIndices(targetArray, out string indexError))
                        {
                            Log("❌ Furniture data rejected: " + indexError);
                            return;
                        }

                        Log($"🌐 Success! Found {targetArray.Length} models.");
                        
                        bool firstFetch = _isFirstFetchOfProject;
                        if (firstFetch)
                        {
                            foreach (var f in targetArray) _knownFurnitureIndices.Add(f.index);
                            _isFirstFetchOfProject = false;
                        }

                        // 儲存資料，並更新選單
                        _fetchedFurnitures = targetArray;
                        _currentFurnitureIndex = 0;
                        _lastRefreshSucceeded = true;
                        if (_projectMenuState == ProjectMenuState.ProjectId)
                            _projectMenuState = ProjectMenuState.Furniture;
                        UpdateDisplay();

                        // 根據最新的需求：不論家具有沒有座標，都不要自動生成實體。
                        // 讓它們全部保持在選單中隱藏，直到玩家手動按下 A 鍵選擇後才生成。
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
                error = $"Furniture index cannot be negative: {furniture.index}";
                return false;
            }

            if (!seenIndices.Add(furniture.index))
            {
                error = $"Duplicate furniture index in the project: {furniture.index}";
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
        Log($"✨ Spawning furniture: {filename}\nPosition: ({data.x:F2}, {data.y:F2}, {data.z:F2})");
        
        _ = LoadModelFromNetwork(data);
        
        if (_knownFurnitureIndices != null) _knownFurnitureIndices.Add(data.index);
        
        _projectMenuState = ProjectMenuState.Hidden;
        UpdateDisplay();
        // 左手 X 始終保留刪除家具；進入專案後使用左手 Y 截圖。
        Log("Furniture is loading. Press Left Y to take a screenshot. Press Right B to return.");
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
        
        // New saves use the active room as their coordinate reference. Legacy world-space
        // records remain readable and are migrated on the next successful save.
        if (data.isPlaced && data.coordinateSpace == "placement-local-v1")
        {
            SceneAutoScanner.TryPlacementToWorldPose(new Vector3(data.x, data.y, data.z), data.ry,
                out Vector3 worldPosition, out Quaternion worldRotation);
            rootObject.transform.SetPositionAndRotation(worldPosition, worldRotation);
        }
        else if (data.isPlaced)
        {
            rootObject.transform.SetPositionAndRotation(new Vector3(data.x, data.y, data.z),
                Quaternion.Euler(0, data.ry, 0));
        }
        else if (headCamera != null)
        {
            Vector3 spawnForward = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
            if (spawnForward.sqrMagnitude < 0.5f) spawnForward = headCamera.forward;
            Vector3 spawnPosition = headCamera.position + spawnForward;
            rootObject.transform.SetPositionAndRotation(spawnPosition,
                Quaternion.LookRotation(spawnForward, Vector3.up));
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
                // 已儲存的位置可能刻意讓家具重疊；重新載入時不可用其他虛擬家具
                // 改寫原本位置，但牆壁與房間邊界仍需要通過檢查。
                if (!wallGuard.Configure(rootCollider, modelVisuals.transform, data.isPlaced))
                {
                    Log("Cannot place furniture. Check that room walls are loaded and nearby space is clear.");
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
        public int item_id;
        public float x;
        public float y;
        public float z;
        public float ry;
        public string coordinateSpace;
    }

    [System.Serializable]
    public class PosBody
    {
        public System.Collections.Generic.List<PosItem> positions;
    }

    [System.Serializable]
    public class PutPositionsResponse
    {
        public bool success;
        public string message;
        public int revision;
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

    private void ShowScreenshotStatus(string message, bool restoreMenuAfterDelay = false)
    {
        Log(message);
        if (idDisplay == null) return;

        int statusVersion = ++_screenshotStatusVersion;
        idDisplay.gameObject.SetActive(true);
        idDisplay.text = $"<b>{message}</b>";
        if (restoreMenuAfterDelay)
            StartCoroutine(RestoreDisplayAfterScreenshotStatus(statusVersion));
    }

    private IEnumerator RestoreDisplayAfterScreenshotStatus(int statusVersion)
    {
        yield return new WaitForSecondsRealtime(2.5f);
        if (statusVersion == _screenshotStatusVersion)
            UpdateDisplay();
    }

    private System.Collections.IEnumerator TakeScreenshotAndUploadRoutine()
    {
        if (isTakingScreenshot) yield break;

        if (!HasActiveProject)
            yield break;

        isTakingScreenshot = true;

        Debug.Log("[Screenshot] Capturing...");

        // 確保當前幀的畫面已經完全渲染完畢
        yield return new WaitForEndOfFrame();

        // 相機權限核准後仍需等待第一張現實畫面。逾時就取消，不能再上傳藍色背景假裝成功。
        float cameraDeadline = Time.realtimeSinceStartup + 3f;
        while ((_screenshotPassthroughCamera == null || !_screenshotPassthroughCamera.IsPlaying) &&
               Time.realtimeSinceStartup < cameraDeadline)
            yield return null;
        if (_screenshotPassthroughCamera == null || !_screenshotPassthroughCamera.IsPlaying)
        {
            Debug.LogError("[Screenshot] Passthrough camera is unavailable or permission was denied.");
            isTakingScreenshot = false;
            yield break;
        }

        // 場景的 CenterEyeAnchor 不一定有 MainCamera 標籤；優先使用 Inspector 已綁定的頭部相機。
        Camera mainCam = headCamera != null ? headCamera.GetComponent<Camera>() : null;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[Screenshot] Main camera not found.");
            isTakingScreenshot = false;
            yield break;
        }

        // 建立一台臨時的虛擬相機，複製玩家的視角
        GameObject camObj = new GameObject("ScreenshotCamera");
        Camera snapCam = camObj.AddComponent<Camera>();
        snapCam.CopyFrom(mainCam);

        Texture passthroughTexture = _screenshotPassthroughCamera.GetTexture();
        Vector2Int cameraResolution = _screenshotPassthroughCamera.CurrentResolution;
        int captureWidth = Mathf.Max(1, cameraResolution.x);
        int captureHeight = Mathf.Max(1, cameraResolution.y);
        RenderTexture virtualRt = new RenderTexture(captureWidth, captureHeight, 24,
            RenderTextureFormat.ARGB32);
        RenderTexture finalRt = new RenderTexture(captureWidth, captureHeight, 0,
            RenderTextureFormat.ARGB32);

        // 使用實體左相機拍攝當下的姿態與內部參數，使虛擬家具疊在現實影像的正確位置。
        Pose cameraPose = _screenshotPassthroughCamera.GetCameraPose();
        snapCam.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
        ApplyPassthroughProjection(snapCam, _screenshotPassthroughCamera);
        // 虛擬內容獨立畫在透明背景，避免 URP 清除相機時覆蓋現實影像。
        snapCam.clearFlags = CameraClearFlags.SolidColor;
        snapCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        snapCam.targetTexture = virtualRt;

        // Unity 相機只畫家具與 UI，之後再與現實相機畫面合成。
        snapCam.Render();

        Shader compositeShader = Resources.Load<Shader>("PassthroughScreenshotComposite");
        if (compositeShader == null)
        {
            Debug.LogError("[Screenshot] Composite shader is missing.");
            snapCam.targetTexture = null;
            Destroy(virtualRt);
            Destroy(finalRt);
            Destroy(camObj);
            isTakingScreenshot = false;
            yield break;
        }
        Material compositeMaterial = new Material(compositeShader);
        compositeMaterial.SetTexture("_BackgroundTex", passthroughTexture);
        Graphics.Blit(virtualRt, finalRt, compositeMaterial);

        // 將畫布轉換為可處理的 2D 圖片
        RenderTexture.active = finalRt;
        Texture2D screenShot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        screenShot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        screenShot.Apply();

        // 卸載並清除記憶體
        snapCam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(virtualRt);
        Destroy(finalRt);
        Destroy(compositeMaterial);
        Destroy(camObj);

        // 將圖片編碼為 JPG 格式 (85% 品質，在畫質與上傳速度間取得平衡)
        byte[] imageBytes = screenShot.EncodeToJPG(85);
        Destroy(screenShot);

        Debug.Log("[Screenshot] Uploading...");
        UploadScreenshotAndReset(imageBytes);
    }

    private static void ApplyPassthroughProjection(
        Camera camera,
        Meta.XR.PassthroughCameraAccess cameraAccess)
    {
        Meta.XR.PassthroughCameraAccess.CameraIntrinsics intrinsics = cameraAccess.Intrinsics;
        Vector2 sensor = intrinsics.SensorResolution;
        Vector2 output = cameraAccess.CurrentResolution;
        Vector2 scale = new Vector2(output.x / sensor.x, output.y / sensor.y);
        float cropScale = Mathf.Max(scale.x, scale.y);
        Vector2 cropSize = output / cropScale;
        Vector2 cropMin = (sensor - cropSize) * 0.5f;

        float near = camera.nearClipPlane;
        float left = (cropMin.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x * near;
        float right = (cropMin.x + cropSize.x - intrinsics.PrincipalPoint.x) /
            intrinsics.FocalLength.x * near;
        float bottom = (cropMin.y - intrinsics.PrincipalPoint.y) /
            intrinsics.FocalLength.y * near;
        float top = (cropMin.y + cropSize.y - intrinsics.PrincipalPoint.y) /
            intrinsics.FocalLength.y * near;
        camera.projectionMatrix = Matrix4x4.Frustum(left, right, bottom, top, near, camera.farClipPlane);
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
        // 使用已確認並載入的專案 ID；輸入框可能正被編輯，不能讓截圖誤傳到別的專案。
        string userId = _activeProjectId;
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
                    ShowScreenshotStatus("SCREENSHOT SAVED", true);
                    Debug.Log("Upload Response: " + req.downloadHandler.text);
                }
                else
                {
                    Debug.LogError($"[Screenshot] Upload failed: {req.error}");
                    Debug.LogError("Upload Error: " + req.error + "\nResponse: " + req.downloadHandler.text);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Screenshot] Error: {e.Message}");
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

        // Capture the active project. The editable ID can already point at the
        // next project while a queued save from the previous project is finishing.
        string userId = _activeProjectId;
        if (string.IsNullOrWhiteSpace(userId)) return;
        string putUrl = BuildProjectApiUrl(userId, "positions");
        
        var list = new System.Collections.Generic.List<PosItem>();
        
        foreach (Transform child in this.transform)
        {
            if (idDisplay != null && child == idDisplay.transform) continue;

            FurnitureTag tag = child.GetComponent<FurnitureTag>();
            if (tag != null)
            {
                Vector3 savedPosition = child.position;
                float savedYaw = child.eulerAngles.y;
                Vector3 localPosition = Vector3.zero;
                float localYaw = 0f;
                // 只有新版伺服器能保存 coordinateSpace 標記。若舊伺服器收到房間
                // 相對座標卻遺失標記，下次會誤當世界座標，家具便會跑到錯誤位置。
                bool hasPlacementReference = _serverSupportsCoordinateSpace &&
                    SceneAutoScanner.TryWorldToPlacementPose(
                        child.position, child.rotation, out localPosition, out localYaw);
                if (hasPlacementReference)
                {
                    savedPosition = localPosition;
                    savedYaw = localYaw;
                }

                // 🌟 同步更新記憶體裡的暫存資料，這樣刪除後重新叫出才會是最新的位置！
                int currentItemId = 0;
                for (int i = 0; i < _fetchedFurnitures.Length; i++)
                {
                    if (_fetchedFurnitures[i].index == tag.index)
                    {
                        currentItemId = _fetchedFurnitures[i].item_id;
                        _fetchedFurnitures[i].x = savedPosition.x;
                        _fetchedFurnitures[i].y = savedPosition.y;
                        _fetchedFurnitures[i].z = savedPosition.z;
                        _fetchedFurnitures[i].ry = savedYaw;
                        _fetchedFurnitures[i].isPlaced = true;
                        _fetchedFurnitures[i].coordinateSpace = hasPlacementReference ? "placement-local-v1" : "world-v0";
                        break;
                    }
                }

                list.Add(new PosItem {
                    item_id = currentItemId,
                    x = savedPosition.x,
                    y = savedPosition.y,
                    z = savedPosition.z,
                    ry = savedYaw,
                    coordinateSpace = hasPlacementReference ? "placement-local-v1" : "world-v0"
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
                    Log("✅ Furniture position saved.");
                    PutPositionsResponse res = JsonUtility.FromJson<PutPositionsResponse>(req.downloadHandler.text);
                    if (res != null && res.revision > 0)
                    {
                        _lastProjectRevision = res.revision.ToString();
                    }
                }
                else
                {
                    Log("❌ Failed to save furniture position: " + req.error);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log("❌ Furniture position save error: " + ex.Message);
        }
    }
}
