using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using TMPro;
using UnityEngine;

public class SceneAutoScanner : MonoBehaviour
{
    public enum PlacementSpaceKind { None, ScannedRoom, ManualWalls }
    public static readonly HashSet<BoxCollider> PlacementWalls = new HashSet<BoxCollider>();
    public static readonly HashSet<BoxCollider> PlacementObstacles = new HashSet<BoxCollider>();
    public static readonly HashSet<Collider> PlacementFloors = new HashSet<Collider>();
    public static int ActiveWallColliderCount { get; private set; }
    public static PlacementSpaceKind ActivePlacementSpace { get; private set; }
    public static int PlacementGeometryVersion { get; private set; }
    public static bool IsWaitingForChoice { get; private set; }
    public static bool StartupFlowComplete { get; private set; }
    public static event System.Action StartupFlowCompleted;
    public static event System.Action StartupFlowReset;
    [Header("Startup scene choice")]
    [Min(0f)] public float initialLoadWaitSeconds = 2f;
    public bool autoScanWhenNoSavedScene = true;

    [Header("Wall collision")]
    [Tooltip("Thickness of the invisible wall colliders, in metres.")]
    [Min(0.01f)] public float wallColliderThickness = 0.05f;
    [Tooltip("Layer used by generated MRUK wall colliders.")]
    [Range(0, 31)] public int wallColliderLayer = 8;
    [Tooltip("Height of manually created walls, in metres.")]
    [Min(0.5f)] public float manualWallHeight = 2.6f;
    [Tooltip("If checked, uses raycast to find physical floor; if false or no hit, falls back to height heuristic.")]
    public bool useFloorRaycast = true;
    [Tooltip("Assign the OcclusionMaterial from MRTemplateAssets to enable spatial occlusion for manual obstacles.")]
    public Material OcclusionMaterial;
    [Tooltip("Maximum distance of the controller ray used for manual wall setup.")]
    [Min(1f)] public float manualSetupRayDistance = 8f;
    [Tooltip("Hold Y for this many seconds to rebuild the current session's manual walls.")]
    [Min(0.5f)] public float resetManualWallsHoldSeconds = 2f;

    private bool _isScanning;
    private bool _isWaitingForChoice;
    private bool _manualSetupActive;
    private bool _manualObstacleSetupActive;
    private int _obstacleDrawPhase = 0;
    private Vector3 _obsCorner1;
    private Vector3 _obsCorner2;
    private Vector3 _obsCorner3;
    private Vector3 _obsCorner4;
    private GameObject _obsPreviewBox;
    private List<GameObject> _manualObstacleObjects = new List<GameObject>();
    private List<BoxData> _manualObstacleDataList = new List<BoxData>();

    private bool _resetConfirmationActive;
    private float _resetHoldStartedAt = -1f;
    private TextMeshPro _choiceText;
    private string _roomLoadStatus = "Not loaded";
    private bool _canUseSavedRoom;
    private readonly List<GameObject> _roomLabels = new List<GameObject>();
    public enum RoomSetupAction { None, UseSaved, Scan, Manual }

    public static RoomSetupAction ChooseRoomSetup(bool hasSavedRoom, bool useSaved, bool scan, bool manual)
    {
        if (scan) return RoomSetupAction.Scan;
        if (manual) return RoomSetupAction.Manual;
        return hasSavedRoom && useSaved ? RoomSetupAction.UseSaved : RoomSetupAction.None;
    }
    private readonly List<GameObject> _wallColliderObjects = new List<GameObject>();
    private GameObject _manualFloorObject;
    private readonly List<GameObject> _scannedFloorObjects = new List<GameObject>();
    private readonly List<Vector3> _manualWallPoints = new List<Vector3>();
    private static readonly List<Vector3> ActiveManualBoundary = new List<Vector3>();
    private static Vector3 _placementOrigin;
    private static Vector3 _placementRecoveryCenter;
    private static Quaternion _placementRotation = Quaternion.identity;
    private static bool _hasPlacementReference;
    private readonly List<GameObject> _manualMarkers = new List<GameObject>();
    private OVRCameraRig _cameraRig;
    private LineRenderer _manualPreviewLine;
    private LineRenderer _manualOutlineLine;
    private Material _manualPreviewMaterial;

    private const int ManualWallDataVersion = 2;
    private const string ManualWallFileName = "manual-walls.json";
    private const string ManualObstacleFileName = "manual-obstacles.json";

    [Serializable]
    public class BoxData
    {
        public Vector3 center;
        public Vector3 size;
        public Quaternion rotation;
        public Vector3 trackingLocalCenter;
        public Quaternion trackingLocalRotation;
        public Vector3 roomLocalCenter;
        public Quaternion roomLocalRotation;
    }

    [Serializable]
    private sealed class ManualObstacleData
    {
        public int version = 1;
        public List<BoxData> obstacles = new List<BoxData>();
    }

    [Serializable]
    private sealed class ManualWallData
    {
        public int version = ManualWallDataVersion;
        public float wallHeight;
        public float wallThickness;
        // 房間座標在 MRUK 舊房間成功載入時最穩定，重開 App 後優先使用。
        public List<Vector3> roomLocalPoints = new List<Vector3>();
        // 追蹤空間座標作為 MRUK 暫時載入失敗時的備援。
        public List<Vector3> trackingLocalPoints = new List<Vector3>();
        // 舊版資料欄位，用來讀取眼鏡裡尚未升級的 manual-walls.json。
        public List<Vector3> points = new List<Vector3>();
    }

    private string ManualWallFilePath => Path.Combine(Application.persistentDataPath, ManualWallFileName);
    private string ManualObstacleFilePath => Path.Combine(Application.persistentDataPath, ManualObstacleFileName);

    private void Awake()
    {
        ConfigureMixedRealityLighting();
        IsWaitingForChoice = false;
        StartupFlowComplete = false;
        ActivePlacementSpace = PlacementSpaceKind.None;
        ActiveManualBoundary.Clear();
        _hasPlacementReference = false;
    }

    private static void ConfigureMixedRealityLighting()
    {
        // Quest 的透視影像不會把現實房間的光線自動轉成 Unity 光源。
        // 使用均勻的柔和環境補光，避免家具背向場景主光時整件變成黑色，
        // 同時保留 Directional Light 所提供的形狀與方向感。
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f, 1f);
        RenderSettings.ambientIntensity = 1f;
    }

    public static bool IsPointInsideActiveBoundary(Vector3 worldPoint)
    {
        if (ActivePlacementSpace != PlacementSpaceKind.ManualWalls || ActiveManualBoundary.Count < 3)
            return true;
        return IsPointInPolygonXZ(ActiveManualBoundary, worldPoint);
    }

    public static bool IsPointInPolygonXZ(IReadOnlyList<Vector3> polygon, Vector3 point)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[j];
            bool crosses = (a.z > point.z) != (b.z > point.z);
            if (!crosses) continue;
            float crossingX = (b.x - a.x) * (point.z - a.z) / (b.z - a.z) + a.x;
            if (point.x < crossingX) inside = !inside;
        }
        return inside;
    }

    public static bool TryGetPlacementCenter(out Vector3 center)
    {
        if (!_hasPlacementReference) { center = Vector3.zero; return false; }
        center = _placementRecoveryCenter;
        return true;
    }

    public static bool TryWorldToPlacementPose(Vector3 worldPosition, Quaternion worldRotation,
        out Vector3 localPosition, out float localYaw)
    {
        // 家具座標以目前房間為基準保存，Quest 重新定位世界原點時，
        // 家具與牆仍會一起移動，不會只有其中一方發生偏移。
        if (!_hasPlacementReference)
        {
            localPosition = worldPosition;
            localYaw = worldRotation.eulerAngles.y;
            return false;
        }
        localPosition = Quaternion.Inverse(_placementRotation) * (worldPosition - _placementOrigin);
        localYaw = (Quaternion.Inverse(_placementRotation) * worldRotation).eulerAngles.y;
        return true;
    }

    public static bool TryPlacementToWorldPose(Vector3 localPosition, float localYaw,
        out Vector3 worldPosition, out Quaternion worldRotation)
    {
        if (!_hasPlacementReference)
        {
            worldPosition = localPosition;
            worldRotation = Quaternion.Euler(0f, localYaw, 0f);
            return false;
        }
        worldPosition = _placementOrigin + _placementRotation * localPosition;
        worldRotation = _placementRotation * Quaternion.Euler(0f, localYaw, 0f);
        return true;
    }

    private static void SetPlacementReference(PlacementSpaceKind kind, Vector3 origin, Vector3 forward,
        Vector3? recoveryCenter = null)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.5f) flatForward = Vector3.forward;
        ActivePlacementSpace = kind;
        _placementOrigin = origin;
        _placementRecoveryCenter = recoveryCenter ?? origin;
        _placementRotation = Quaternion.LookRotation(flatForward, Vector3.up);
        _hasPlacementReference = true;
        PlacementGeometryVersion++;
    }

    public static bool IsPhysicalWallLabel(MRUKAnchor.SceneLabels label)
    {
        // Meta 的 INVISIBLE_WALL_FACE 只用來概念性切分開放空間，不代表實體牆，
        // 因此不能拿來阻擋家具；一般外牆與內部柱體仍保留碰撞。
        if ((label & MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE) != 0) return false;
        return (label & (MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE)) != 0;
    }

    private IEnumerator Start()
    {
        Debug.Log("[Scanner] Loading the saved room from this headset...");
        // 啟動時先告知正在讀取 Quest 內的房間資料，避免等待期間看起來像沒有反應。
        EnablePassthroughView();
        ShowStartupStatus("<b>CHECKING SAVED ROOM...</b>");
        yield return new WaitForSeconds(initialLoadWaitSeconds);

        // MRUK is configured for manual loading in this scene. Wait for its
        // singleton to be initialized instead of racing its Awake/Start flow.
        float waitDeadline = Time.realtimeSinceStartup + 10f;
        while (MRUK.Instance == null && Time.realtimeSinceStartup < waitDeadline)
            yield return null;

        MRUK.LoadDeviceResult loadResult = MRUK.LoadDeviceResult.NotInitialized;
        if (MRUK.Instance != null)
        {
            // Quest 剛切回 App 時空間服務可能仍在恢復；第一次查詢暫時失敗不能直接
            // 當成沒有舊房間，否則 A 選項會消失。最多重試三次再做最後判定。
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (_choiceText != null)
                    _choiceText.text = $"<b>CHECKING SAVED ROOM...</b>\nAttempt {attempt} / 3";

                // 此處禁止自動開啟 Space Setup；只有使用者按 B 才能進入重新掃描。
                Task<MRUK.LoadDeviceResult> loadTask =
                    MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                while (!loadTask.IsCompleted)
                    yield return null;

                if (loadTask.IsCanceled)
                    Debug.LogWarning($"[Scanner] Saved-room load attempt {attempt} was cancelled.");
                else if (loadTask.IsFaulted)
                    Debug.LogError($"[Scanner] Saved-room load attempt {attempt} failed: {loadTask.Exception}");
                else
                {
                    loadResult = loadTask.Result;
                    Debug.Log($"[Scanner] Saved-room load attempt {attempt}: {loadResult}");
                    if (loadResult == MRUK.LoadDeviceResult.Success)
                        break;
                }

                if (attempt < 3)
                {
                    MRUK.Instance.ClearScene();
                    yield return new WaitForSecondsRealtime(1f);
                }
            }
        }
        else
            Debug.LogError("[Scanner] MRUK.Instance did not initialize within 10 seconds.");

        _roomLoadStatus = loadResult.ToString();
        if (loadResult == MRUK.LoadDeviceResult.Success &&
            MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            // LoadSceneFromDevice 完成時，房間物件可能已存在，但牆面錨點仍在逐幀建立。
            // 等到實體牆出現後才建立碰撞牆，避免重開 App 時誤判成「沒有舊牆壁」。
            float geometryDeadline = Time.realtimeSinceStartup + 10f;
            while (!HasLoadedPhysicalWall() && Time.realtimeSinceStartup < geometryDeadline)
                yield return null;
            if (TryLoadManualWalls())
            {
                _roomLoadStatus += " / saved manual walls";
                TryLoadManualObstacles();
            }
            else
                RebuildWallColliders();
        }
        else
        {
            Debug.LogWarning("[Scanner] MRUK room unavailable. Checking saved manual walls.");
            if (TryLoadManualWalls())
            {
                _roomLoadStatus += " / saved manual walls";
                TryLoadManualObstacles();
            }
        }
        // 即使已找到舊房間，仍讓使用者按 A 確認使用；按 B 才會重新掃描。
        yield return AskWhetherToRescan();
    }

    private static bool HasLoadedPhysicalWall()
    {
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null) return false;
        foreach (MRUKAnchor anchor in room.Anchors)
            if (IsPhysicalWallLabel(anchor.Label) && anchor.PlaneRect.HasValue)
                return true;
        return false;
    }

    private void ShowStartupStatus(string message)
    {
        if (Camera.main == null) return;
        FinishChoice();
        var prompt = new GameObject("RoomStartupStatus");
        prompt.transform.SetParent(Camera.main.transform, false);
        prompt.transform.localPosition = new Vector3(0f, 0.12f, 1.2f);
        prompt.transform.localRotation = Quaternion.identity;
        prompt.transform.localScale = Vector3.one * 0.005f;
        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 36f;
        _choiceText.rectTransform.sizeDelta = new Vector2(700f, 180f);
        _choiceText.text = message;
    }

    private IEnumerator AskWhetherToRescan()
    {
        FinishChoice();
        _canUseSavedRoom = ActiveWallColliderCount > 0;
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;
        EnablePassthroughView();
        ShowChoiceText();
        // Surface labels are temporarily hidden; the menu retains the wall count.
        Debug.Log($"[Scanner] Room selection: result={_roomLoadStatus}, usable walls={ActiveWallColliderCount}. A: use saved, B: scan, X: manual.");
        // Require released buttons after returning from the system scan UI.
        yield return WaitForChoiceButtonsReleased();

        while (_isWaitingForChoice)
        {
            RoomSetupAction action = ChooseRoomSetup(_canUseSavedRoom,
                OVRInput.GetDown(OVRInput.RawButton.A), OVRInput.GetDown(OVRInput.RawButton.B),
                OVRInput.GetDown(OVRInput.RawButton.X));
            if (action == RoomSetupAction.UseSaved)
            {
                Debug.Log("[Scanner] Using the saved room.");
                FinishChoice();
                SignalStartupFlowComplete();
                yield break;
            }
            else if (action == RoomSetupAction.Scan)
            {
                Debug.Log("[Scanner] User requested a new room scan.");
                FinishChoice();
                TriggerNewScan();
                yield break;
            }
            else if (action == RoomSetupAction.Manual)
            {
                FinishChoice();
                yield return ConfirmManualWallSetup();
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator WaitForChoiceButtonsReleased()
    {
        float releasedFor = 0f;
        while (releasedFor < 0.2f)
        {
            bool held = OVRInput.Get(OVRInput.RawButton.A) || OVRInput.Get(OVRInput.RawButton.B) ||
                OVRInput.Get(OVRInput.RawButton.X) || OVRInput.Get(OVRInput.RawButton.Y);
            releasedFor = held ? 0f : releasedFor + Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator ConfirmManualWallSetup()
    {
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;
        ShowChoiceText();
        if (_choiceText != null)
            _choiceText.text = "<b>USE MANUAL WALL SETUP?</b>\n\n" +
                $"Existing usable walls: {ActiveWallColliderCount}\n" +
                "This replaces the current wall layout.\n\n" +
                "A: Confirm manual setup\nB: Back to room selection";
        yield return WaitForChoiceButtonsReleased();
        while (_isWaitingForChoice)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                FinishChoice();
                BeginManualWallSetup("confirmed manual choice");
                yield break;
            }
            if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                FinishChoice();
                yield return AskWhetherToRescan();
                yield break;
            }
            yield return null;
        }
    }

    private void SignalStartupFlowComplete()
    {
        if (StartupFlowComplete) return;
        StartupFlowComplete = true;
        StartupFlowCompleted?.Invoke();
    }

    private void Update()
    {
        if (Camera.main != null)
            foreach (GameObject label in _roomLabels)
                if (label != null) label.transform.rotation = Camera.main.transform.rotation;
        if (_manualSetupActive)
        {
            UpdateManualWallSetup();
            return;
        }

        if (_manualObstacleSetupActive)
        {
            UpdateManualObstacleSetup();
            return;
        }

        if (_resetConfirmationActive)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
            {
                _resetConfirmationActive = false;
                FinishChoice();
                DeleteManualWallFile();
                BeginManualWallSetup("confirmed manual reset");
            }
            else if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
            {
                _resetConfirmationActive = false;
                FinishChoice();
                SignalStartupFlowComplete();
            }
            return;
        }

        if (_isWaitingForChoice || _isScanning) return;
        // 進入專案後 Y 交由截圖功能使用，避免長按時誤開手動牆重設。
        if (ModelLoader.Instance != null && ModelLoader.Instance.HasActiveProject)
        {
            _resetHoldStartedAt = -1f;
            return;
        }
        // Hold Y to deliberately replace the current session's manual calibration.
        if (OVRInput.Get(OVRInput.RawButton.Y))
        {
            if (_resetHoldStartedAt < 0f)
                _resetHoldStartedAt = Time.unscaledTime;
            else if (Time.unscaledTime - _resetHoldStartedAt >= resetManualWallsHoldSeconds)
            {
                _resetHoldStartedAt = float.PositiveInfinity;
                BeginManualWallResetConfirmation();
            }
        }
        else
            _resetHoldStartedAt = -1f;
    }

    private void ShowChoiceText()
    {
        if (Camera.main == null) return;

        var prompt = new GameObject("SceneScanChoice");
        prompt.transform.SetParent(Camera.main.transform, false);
        prompt.transform.localPosition = new Vector3(0f, 0f, 1.2f);
        prompt.transform.localRotation = Quaternion.identity;
        prompt.transform.localScale = Vector3.one * 0.005f;

        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 32f;
        _choiceText.rectTransform.sizeDelta = new Vector2(700f, 360f);
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        string permission = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene)
            ? "granted" : "not granted";
        _choiceText.text = BuildRoomChoiceText(_canUseSavedRoom, _roomLoadStatus, permission,
            ActiveWallColliderCount, room != null ? room.FloorAnchors.Count : 0,
            room != null ? room.CeilingAnchors.Count : 0);
    }

    public static string BuildRoomChoiceText(bool hasSaved, string status, string permission, int walls, int floors, int ceilings)
    {
        return "<b>ROOM SETUP</b>\n" + $"Load: {status}\nSpatial permission: {permission}\n" +
            $"WALL: {walls}\n\n" +
            (hasSaved ? "<color=#62E6A5>A: Use saved room</color>\n" : "No usable saved walls loaded\n") +
            "<color=#FFB45E>B: Scan / scan again</color>\nX: Manual wall setup";
    }

    private void ShowRoomLabels()
    {
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null) return;
        int wall = 0, floor = 0, ceiling = 0;
        foreach (MRUKAnchor anchor in room.Anchors)
        {
            string title;
            Color color;
            if ((anchor.Label & (MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE)) != 0)
            { title = "WALL " + ++wall; color = Color.cyan; }
            else if ((anchor.Label & MRUKAnchor.SceneLabels.FLOOR) != 0)
            { title = "FLOOR " + ++floor; color = Color.green; }
            else if ((anchor.Label & MRUKAnchor.SceneLabels.CEILING) != 0)
            { title = "CEILING " + ++ceiling; color = Color.yellow; }
            else continue;
            if (!anchor.PlaneRect.HasValue) continue;
            Rect plane = anchor.PlaneRect.Value;
            var marker = new GameObject("RoomLabel " + title);
            marker.transform.SetParent(anchor.transform, false);
            marker.transform.localPosition = new Vector3(plane.center.x, plane.center.y, 0);
            if (Camera.main != null)
            {
                marker.transform.position += (Camera.main.transform.position - marker.transform.position).normalized * 0.04f;
                marker.transform.rotation = Camera.main.transform.rotation;
            }
            marker.transform.localScale = Vector3.one * 0.005f;
            var text = marker.AddComponent<TextMeshPro>();
            text.text = title;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 36f;
            text.rectTransform.sizeDelta = new Vector2(250, 80);
            _roomLabels.Add(marker);
        }
    }

    private void FinishChoice()
    {
        foreach (GameObject label in _roomLabels) if (label != null) Destroy(label);
        _roomLabels.Clear();
        _isWaitingForChoice = false;
        IsWaitingForChoice = false;
        if (_choiceText != null)
        {
            Destroy(_choiceText.gameObject);
            _choiceText = null;
        }
    }

    public void ReturnToRoomSetup()
    {
        if (_isScanning) return;
        FinishChoice();
        if (StartupFlowComplete)
        {
            StartupFlowComplete = false;
            StartupFlowReset?.Invoke();
        }
        StartCoroutine(AskWhetherToRescan());
    }

    public async void TriggerNewScan()
    {
        if (_isScanning) return;
        FinishChoice();
        if (StartupFlowComplete)
        {
            StartupFlowComplete = false;
            StartupFlowReset?.Invoke();
        }
        await StartFullScanProcess();
    }

    private async Task StartFullScanProcess()
    {
        _isScanning = true;
        Debug.Log("[Scanner] Opening the room setup interface...");

        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;

        var passthroughLayer = FindObjectOfType<OVRPassthroughLayer>();
        if (passthroughLayer != null)
            passthroughLayer.hidden = false;

        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        GameObject environment = GameObject.Find("Environment");
        if (environment != null)
            environment.SetActive(false);

        try
        {
            bool captured = await OVRScene.RequestSpaceSetup();
            if (!captured)
            {
                _roomLoadStatus = "Scan cancelled or unavailable";
                return;
            }
            await Task.Delay(1000);
            if (this == null || !isActiveAndEnabled) return;

            if (MRUK.Instance == null)
            {
                Debug.LogError("[Scanner] MRUK.Instance was not found.");
                _roomLoadStatus = "MRUK unavailable";
                return;
            }

            ClearWallColliders();
            MRUK.Instance.ClearScene();
            MRUK.LoadDeviceResult loadResult =
                await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            Debug.Log("[Scanner] Post-setup room load result: " + loadResult);
            if (this == null || !isActiveAndEnabled) return;
            _roomLoadStatus = loadResult.ToString();

            // 新增：動態等待 MRUK 載入房間結構 (解決 MRUK 需要幾幀時間建立 Room 的 Bug)
            if (loadResult == MRUK.LoadDeviceResult.Success)
            {
                int retries = 20;
                while (MRUK.Instance.GetCurrentRoom() == null && retries > 0)
                {
                    await Task.Delay(500);
                    retries--;
                }
            }

            if (loadResult == MRUK.LoadDeviceResult.Success &&
                MRUK.Instance.GetCurrentRoom() != null)
            {
                RebuildWallColliders();
                Debug.Log("[Scanner] The new room was loaded successfully.");
            }
            else
            {
                Debug.LogWarning("[Scanner] Room setup closed, but no usable room was loaded.");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[Scanner] Room scanning failed: " + exception.Message);
            _roomLoadStatus = "Scan/load failed (see device log)";
        }
        finally
        {
            _isScanning = false;
            if (this != null && isActiveAndEnabled) StartCoroutine(AskWhetherToRescan());
        }
    }

    private Transform TrackingSpace
    {
        get
        {
            if (_cameraRig == null)
                _cameraRig = FindObjectOfType<OVRCameraRig>();
            return _cameraRig != null ? _cameraRig.trackingSpace : null;
        }
    }

    private void BeginManualWallSetup(string reason)
    {
        if (_manualSetupActive) return;
        Debug.Log($"[ManualWalls] Entry={reason}; last load={_roomLoadStatus}; previous walls={ActiveWallColliderCount}.");

        if (StartupFlowComplete)
        {
            StartupFlowComplete = false;
            StartupFlowReset?.Invoke();
        }
        FinishChoice();
        ClearWallColliders();
        ClearManualSetupVisuals();
        _manualWallPoints.Clear();
        _manualSetupActive = true;
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;
        EnablePassthroughView();
        CreateManualSetupPrompt();
        CreateManualPreviewLine();
        UpdateManualSetupPrompt();
        Debug.Log("[ManualWalls] Setup started. Point the right controller ray at each floor corner and press the right trigger.");
    }

    private void BeginManualWallResetConfirmation()
    {
        if (_manualSetupActive || _resetConfirmationActive) return;
        if (StartupFlowComplete)
        {
            StartupFlowComplete = false;
            StartupFlowReset?.Invoke();
        }
        FinishChoice();
        _resetConfirmationActive = true;
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;

        if (Camera.main == null) return;
        var prompt = new GameObject("ManualWallResetConfirmation");
        prompt.transform.SetParent(Camera.main.transform, false);
        prompt.transform.localPosition = new Vector3(0f, 0.1f, 1.2f);
        prompt.transform.localRotation = Quaternion.identity;
        prompt.transform.localScale = Vector3.one * 0.005f;
        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 42f;
        _choiceText.rectTransform.sizeDelta = new Vector2(720f, 240f);
        _choiceText.text = "<b>REBUILD MANUAL WALLS?</b>\n\n" +
                           "<color=#62E6A5>A: Replace</color>    B: Cancel";
    }

    private void UpdateManualWallSetup()
    {
        bool hasFloorPoint = TryGetManualFloorPoint(out Vector3 rayOrigin, out Vector3 floorPoint);
        if (!hasFloorPoint)
        {
            UpdateManualPreviewLine(rayOrigin, rayOrigin + GetRightControllerForward() * manualSetupRayDistance, false);
        }
        else
            UpdateManualPreviewLine(rayOrigin, floorPoint, true);

        if (hasFloorPoint && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch))
        {
            AddManualWallPoint(floorPoint);
        }
        else if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
        {
            UndoManualWallPoint();
        }
        else if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
        {
            SaveAndFinishManualWalls();
        }
    }

    private bool TryGetManualFloorPoint(out Vector3 rayOrigin, out Vector3 floorPoint)
    {
        Transform trackingSpace = TrackingSpace;
        Vector3 localPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

        if (trackingSpace != null)
        {
            rayOrigin = trackingSpace.TransformPoint(localPosition);
            Vector3 rayDirection = trackingSpace.TransformDirection(localRotation * Vector3.forward);
            Plane floorPlane = new Plane(trackingSpace.up, trackingSpace.position);
            if (floorPlane.Raycast(new Ray(rayOrigin, rayDirection), out float distance) &&
                distance >= 0f && distance <= manualSetupRayDistance)
            {
                floorPoint = rayOrigin + rayDirection * distance;
                return true;
            }
        }
        else
        {
            rayOrigin = localPosition;
            Vector3 rayDirection = localRotation * Vector3.forward;
            Plane floorPlane = new Plane(Vector3.up, Vector3.zero);
            if (floorPlane.Raycast(new Ray(rayOrigin, rayDirection), out float distance) &&
                distance >= 0f && distance <= manualSetupRayDistance)
            {
                floorPoint = rayOrigin + rayDirection * distance;
                return true;
            }
        }

        floorPoint = rayOrigin;
        return false;
    }

    private Vector3 GetRightControllerForward()
    {
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        Transform trackingSpace = TrackingSpace;
        return trackingSpace != null
            ? trackingSpace.TransformDirection(localRotation * Vector3.forward)
            : localRotation * Vector3.forward;
    }

    private void AddManualWallPoint(Vector3 worldPoint)
    {
        if (_manualWallPoints.Count > 0 &&
            Vector3.Distance(_manualWallPoints[_manualWallPoints.Count - 1], worldPoint) < 0.1f)
        {
            Debug.LogWarning("[ManualWalls] Corner ignored because it is too close to the previous corner.");
            return;
        }

        _manualWallPoints.Add(worldPoint);
        CreateManualMarker(worldPoint, _manualWallPoints.Count - 1);
        UpdateManualOutline();
        UpdateManualSetupPrompt();
        Debug.Log($"[ManualWalls] Added corner {_manualWallPoints.Count} at {worldPoint}.");
    }

    private void UndoManualWallPoint()
    {
        if (_manualWallPoints.Count == 0) return;

        int lastIndex = _manualWallPoints.Count - 1;
        _manualWallPoints.RemoveAt(lastIndex);
        if (lastIndex < _manualMarkers.Count)
        {
            Destroy(_manualMarkers[lastIndex]);
            _manualMarkers.RemoveAt(lastIndex);
        }
        UpdateManualOutline();
        UpdateManualSetupPrompt();
        Debug.Log("[ManualWalls] Removed the last corner.");
    }

    private void SaveAndFinishManualWalls()
    {
        if (_manualWallPoints.Count < 3)
        {
            UpdateManualSetupPrompt("Add at least 3 corners before saving.");
            return;
        }

        if (!BuildManualWallColliders(_manualWallPoints))
        {
            UpdateManualSetupPrompt("Could not build walls. Please try again.");
            return;
        }

        if (!SaveManualWalls())
        {
            ClearWallColliders();
            UpdateManualSetupPrompt("Could not save walls. Please try again.");
            return;
        }

        // 儲存成功後進入畫家具(障礙物)模式
        _manualSetupActive = false;
        ClearManualSetupVisuals();
        Debug.Log($"[ManualWalls] Saved and built {ActiveWallColliderCount} walls. Starting obstacle setup.");
        BeginManualObstacleSetup();
    }

    private bool SaveManualWalls()
    {
        try
        {
            Transform trackingSpace = TrackingSpace;
            MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            var data = new ManualWallData
            {
                wallHeight = manualWallHeight,
                wallThickness = wallColliderThickness
            };

            foreach (Vector3 worldPoint in _manualWallPoints)
            {
                // 同時保存兩種相對座標；有 MRUK 房間時優先用房間座標還原。
                if (room != null) data.roomLocalPoints.Add(room.transform.InverseTransformPoint(worldPoint));
                data.trackingLocalPoints.Add(trackingSpace != null
                    ? trackingSpace.InverseTransformPoint(worldPoint) : worldPoint);
            }

            File.WriteAllText(ManualWallFilePath, JsonUtility.ToJson(data, true));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualWalls] Save failed: " + exception.Message);
            return false;
        }
    }

    private bool TryLoadManualWalls()
    {
        if (!File.Exists(ManualWallFilePath)) return false;
        try
        {
            ManualWallData data = JsonUtility.FromJson<ManualWallData>(File.ReadAllText(ManualWallFilePath));
            if (data == null) return false;

            manualWallHeight = Mathf.Max(0.5f, data.wallHeight);
            // 舊版曾保存 8 公分牆厚；載入時統一改用 5 公分，避免牆的內側
            // 吃掉過多可擺放空間。碰撞防穿透仍由家具的連續位置檢查負責。
            wallColliderThickness = 0.05f;
            Transform trackingSpace = TrackingSpace;
            MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            var worldPoints = new List<Vector3>();

            if (data.version >= 2 && room != null && data.roomLocalPoints != null && data.roomLocalPoints.Count >= 3)
                foreach (Vector3 point in data.roomLocalPoints)
                    worldPoints.Add(room.transform.TransformPoint(point));
            else
            {
                // v1 的 points 與 v2 的 trackingLocalPoints 都以 TrackingSpace 為基準。
                List<Vector3> localPoints = data.version >= 2 ? data.trackingLocalPoints : data.points;
                if (localPoints == null || localPoints.Count < 3) return false;
                foreach (Vector3 point in localPoints)
                    worldPoints.Add(trackingSpace != null ? trackingSpace.TransformPoint(point) : point);
            }

            bool built = BuildManualWallColliders(worldPoints);
            Debug.Log(built
                ? $"[ManualWalls] Loaded {ActiveWallColliderCount} saved walls."
                : "[ManualWalls] Saved wall data was invalid.");
            return built;
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualWalls] Load failed: " + exception.Message);
            return false;
        }
    }

    private void DeleteManualWallFile()
    {
        try
        {
            if (File.Exists(ManualWallFilePath)) File.Delete(ManualWallFilePath);
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualWalls] Delete failed: " + exception.Message);
        }
    }

    private bool BuildManualWallColliders(IReadOnlyList<Vector3> points)
    {
        ClearWallColliders();
        Transform trackingSpace = TrackingSpace;
        Vector3 up = trackingSpace != null ? trackingSpace.up : Vector3.up;

        for (int index = 0; index < points.Count; index++)
        {
            Vector3 start = points[index];
            Vector3 end = points[(index + 1) % points.Count];
            Vector3 direction = Vector3.ProjectOnPlane(end - start, up);
            float length = direction.magnitude;
            if (length < 0.1f) continue;

            var wallObject = new GameObject($"ManualWallCollider_{index}");
            wallObject.layer = wallColliderLayer;
            wallObject.transform.position = (start + end) * 0.5f + up * (manualWallHeight * 0.5f);
            wallObject.transform.rotation = Quaternion.FromToRotation(Vector3.right, direction.normalized);

            var wallCollider = wallObject.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(length, manualWallHeight, wallColliderThickness);
            wallCollider.isTrigger = false;
            _wallColliderObjects.Add(wallObject);
            PlacementWalls.Add(wallObject.GetComponent<BoxCollider>());
        }

        ActiveWallColliderCount = _wallColliderObjects.Count;
        if (ActiveWallColliderCount >= 3)
        {
            ActiveManualBoundary.Clear();
            foreach (Vector3 point in points) ActiveManualBoundary.Add(point);
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in points) center += point;
            center /= points.Count;
            Vector3 forward = Vector3.ProjectOnPlane(points[1] - points[0], Vector3.up);
            SetPlacementReference(PlacementSpaceKind.ManualWalls, center, forward);
            BuildManualFloor(points);
        }
        Physics.SyncTransforms();
        return ActiveWallColliderCount >= 3;
    }

    private void BuildManualFloor(IReadOnlyList<Vector3> points)
    {
        Transform basis = TrackingSpace;
        Vector3 origin = basis != null ? basis.position : Vector3.zero;
        Quaternion rotation = basis != null ? basis.rotation : Quaternion.identity;
        Bounds bounds = new Bounds(Quaternion.Inverse(rotation) * (points[0] - origin), Vector3.zero);
        foreach (Vector3 point in points) bounds.Encapsulate(Quaternion.Inverse(rotation) * (point - origin));
        _manualFloorObject = new GameObject("ManualPlacementFloor");
        _manualFloorObject.transform.SetPositionAndRotation(origin, rotation);
        var floor = _manualFloorObject.AddComponent<BoxCollider>();
        // Only a support plane. The perimeter walls still define the room shape.
        floor.center = new Vector3(bounds.center.x, bounds.min.y - 0.02f, bounds.center.z);
        floor.size = new Vector3(Mathf.Max(bounds.size.x, 0.1f), 0.04f, Mathf.Max(bounds.size.z, 0.1f));
        PlacementFloors.Add(floor);
    }

    private void EnablePassthroughView()
    {
        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;
        OVRPassthroughLayer passthroughLayer = FindObjectOfType<OVRPassthroughLayer>();
        if (passthroughLayer != null)
            passthroughLayer.hidden = false;
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
        GameObject environment = GameObject.Find("Environment");
        if (environment != null)
            environment.SetActive(false);
    }

    private void CreateManualSetupPrompt()
    {
        if (Camera.main == null) return;
        var prompt = new GameObject("ManualWallSetupPrompt");
        prompt.transform.SetParent(Camera.main.transform, false);
        prompt.transform.localPosition = new Vector3(0f, 0.18f, 1.2f);
        prompt.transform.localRotation = Quaternion.identity;
        prompt.transform.localScale = Vector3.one * 0.005f;
        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 40f;
        _choiceText.rectTransform.sizeDelta = new Vector2(760f, 300f);
    }

    private void UpdateManualSetupPrompt(string warning = null)
    {
        if (_choiceText == null) return;
        string saveHint = _manualWallPoints.Count >= 3
            ? "<color=#62E6A5>A: Save walls</color>"
            : "A: Save (3 corners required)";
        _choiceText.text =
            "<b>MANUAL WALL SETUP</b>\n" +
            $"Corners: {_manualWallPoints.Count}\n\n" +
            "Point at each floor corner\n" +
            "Right trigger: Add corner    B: Undo\n" + saveHint +
            (string.IsNullOrEmpty(warning) ? string.Empty : $"\n<color=#FFB45E>{warning}</color>");
    }

    private void CreateManualPreviewLine()
    {
        var preview = new GameObject("ManualWallSetupPreview");
        _manualPreviewLine = preview.AddComponent<LineRenderer>();
        _manualPreviewLine.positionCount = 2;
        _manualPreviewLine.startWidth = 0.012f;
        _manualPreviewLine.endWidth = 0.012f;
        _manualPreviewLine.useWorldSpace = true;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _manualPreviewMaterial = new Material(shader);
            _manualPreviewMaterial.color = Color.cyan;
            _manualPreviewLine.material = _manualPreviewMaterial;
        }
        _manualPreviewLine.startColor = Color.cyan;
        _manualPreviewLine.endColor = Color.cyan;

        var outline = new GameObject("ManualWallOutline");
        _manualOutlineLine = outline.AddComponent<LineRenderer>();
        _manualOutlineLine.positionCount = 0;
        _manualOutlineLine.startWidth = 0.025f;
        _manualOutlineLine.endWidth = 0.025f;
        _manualOutlineLine.useWorldSpace = true;
        _manualOutlineLine.startColor = Color.green;
        _manualOutlineLine.endColor = Color.green;
        if (_manualPreviewMaterial != null)
            _manualOutlineLine.material = _manualPreviewMaterial;
    }

    private void UpdateManualPreviewLine(Vector3 start, Vector3 end, bool valid)
    {
        if (_manualPreviewLine == null) return;
        Color color = valid ? Color.cyan : new Color(1f, 0.35f, 0.2f, 1f);
        _manualPreviewLine.startColor = color;
        _manualPreviewLine.endColor = color;
        _manualPreviewLine.SetPosition(0, start);
        _manualPreviewLine.SetPosition(1, end);
    }

    private void CreateManualMarker(Vector3 position, int index)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = $"ManualWallCorner_{index + 1}";
        marker.transform.position = position + (TrackingSpace != null ? TrackingSpace.up : Vector3.up) * 0.035f;
        marker.transform.localScale = Vector3.one * 0.07f;
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) Destroy(markerCollider);
        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = Color.green;
        _manualMarkers.Add(marker);
    }

    private void UpdateManualOutline()
    {
        if (_manualOutlineLine == null) return;
        _manualOutlineLine.positionCount = _manualWallPoints.Count;
        _manualOutlineLine.loop = _manualWallPoints.Count >= 3;
        Vector3 up = TrackingSpace != null ? TrackingSpace.up : Vector3.up;
        for (int index = 0; index < _manualWallPoints.Count; index++)
            _manualOutlineLine.SetPosition(index, _manualWallPoints[index] + up * 0.025f);
    }

    private void ClearManualSetupVisuals()
    {
        if (_choiceText != null)
        {
            Destroy(_choiceText.gameObject);
            _choiceText = null;
        }
        if (_manualPreviewLine != null)
        {
            Destroy(_manualPreviewLine.gameObject);
            _manualPreviewLine = null;
        }
        if (_manualOutlineLine != null)
        {
            Destroy(_manualOutlineLine.gameObject);
            _manualOutlineLine = null;
        }
        if (_manualPreviewMaterial != null)
        {
            Destroy(_manualPreviewMaterial);
            _manualPreviewMaterial = null;
        }
        foreach (GameObject marker in _manualMarkers)
            if (marker != null) Destroy(marker);
        _manualMarkers.Clear();
    }

    private void RebuildWallColliders()
    {
        ClearWallColliders();

        if (MRUK.Instance == null) return;
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return;

        int createdCount = 0;
        Debug.Log($"[Scanner] Room contains {room.Anchors.Count} anchors before wall filtering.");
        foreach (var anchor in room.Anchors)
        {
            if (!IsPhysicalWallLabel(anchor.Label))
                continue;

            if (!anchor.PlaneRect.HasValue)
            {
                Debug.LogWarning($"[Scanner] Wall anchor '{anchor.name}' ({anchor.Label}) has no PlaneRect and was skipped.");
                continue;
            }

            Rect plane = anchor.PlaneRect.Value;
            var wallObject = new GameObject($"MRWallCollider_{createdCount}");
            wallObject.layer = wallColliderLayer;
            wallObject.transform.SetParent(anchor.transform, false);

            var wallCollider = wallObject.AddComponent<BoxCollider>();
            wallCollider.center = new Vector3(plane.center.x, plane.center.y, 0f);
            wallCollider.size = new Vector3(
                Mathf.Max(0.01f, plane.width),
                Mathf.Max(0.01f, plane.height),
                Mathf.Max(0.01f, wallColliderThickness));
            wallCollider.isTrigger = false;

            _wallColliderObjects.Add(wallObject);
            PlacementWalls.Add(wallCollider);
            Debug.Log($"[Scanner] Wall {createdCount}: label={anchor.Label}, position={wallObject.transform.position}, " +
                      $"rotation={wallObject.transform.eulerAngles}, size={wallCollider.size}.");
            createdCount++;
        }

        foreach (MRUKAnchor floorAnchor in room.FloorAnchors)
        {
            if (!floorAnchor.PlaneRect.HasValue) continue;
            Rect plane = floorAnchor.PlaneRect.Value;
            var floorObject = new GameObject("MRPlacementFloor");
            floorObject.transform.SetParent(floorAnchor.transform, false);
            var floor = floorObject.AddComponent<BoxCollider>();
            floor.center = new Vector3(plane.center.x, plane.center.y, -0.02f);
            floor.size = new Vector3(plane.width, plane.height, 0.04f);
            _scannedFloorObjects.Add(floorObject);
            PlacementFloors.Add(floor);
        }
        ActiveWallColliderCount = createdCount;
        if (createdCount > 0)
            SetPlacementReference(PlacementSpaceKind.ScannedRoom, room.transform.position, room.transform.forward,
                room.GetRoomBounds().center);
        Physics.SyncTransforms();
        Debug.Log($"[Scanner] Built {createdCount} invisible MRUK wall colliders.");
    }

    private void ClearWallColliders()
    {
        foreach (GameObject floor in _scannedFloorObjects)
            if (floor != null)
            {
                PlacementFloors.Remove(floor.GetComponent<Collider>());
                Destroy(floor);
            }
        _scannedFloorObjects.Clear();
        if (_manualFloorObject != null)
        {
            PlacementFloors.Remove(_manualFloorObject.GetComponent<Collider>());
            Destroy(_manualFloorObject);
            _manualFloorObject = null;
        }
        foreach (GameObject wallObject in _wallColliderObjects)
        {
            if (wallObject != null)
            {
                PlacementWalls.Remove(wallObject.GetComponent<BoxCollider>());
                Destroy(wallObject);
            }
        }
        _wallColliderObjects.Clear();
        
        foreach (GameObject obsObject in _manualObstacleObjects)
        {
            if (obsObject != null)
            {
                PlacementObstacles.Remove(obsObject.GetComponent<BoxCollider>());
                foreach (var col in obsObject.GetComponentsInChildren<Collider>())
                    PlacementFloors.Remove(col);
                Destroy(obsObject);
            }
        }
        _manualObstacleObjects.Clear();
        _manualObstacleDataList.Clear();

        PlacementWalls.RemoveWhere(wall => wall == null);
        PlacementObstacles.RemoveWhere(obs => obs == null);
        ActiveWallColliderCount = 0;
        ActiveManualBoundary.Clear();
        ActivePlacementSpace = PlacementSpaceKind.None;
        _hasPlacementReference = false;
        PlacementGeometryVersion++;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _manualSetupActive = false;
        _manualObstacleSetupActive = false;
        _resetConfirmationActive = false;
        FinishChoice();
        ClearManualSetupVisuals();
        ClearWallColliders();
    }

    // ==========================================
    // Manual Obstacle Setup
    // ==========================================
    private void BeginManualObstacleSetup()
    {
        if (_manualObstacleSetupActive) return;
        _manualObstacleSetupActive = true;
        _obstacleDrawPhase = 0;
        
        CreateManualPreviewLine();

        if (Camera.main != null)
        {
            var prompt = new GameObject("ObstacleSetupPrompt");
            prompt.transform.SetParent(Camera.main.transform, false);
            prompt.transform.localPosition = new Vector3(0f, 0.1f, 1.2f);
            prompt.transform.localRotation = Quaternion.identity;
            prompt.transform.localScale = Vector3.one * 0.005f;
            _choiceText = prompt.AddComponent<TextMeshPro>();
            _choiceText.alignment = TextAlignmentOptions.Center;
            _choiceText.fontSize = 32f;
            _choiceText.rectTransform.sizeDelta = new Vector2(800f, 400f);
            UpdateObstacleSetupPrompt();
        }
    }

    private void UpdateObstacleSetupPrompt()
    {
        if (_choiceText == null) return;
        string text = "<b>DRAW FURNITURE (OBSTACLES)</b>\n\n";
        if (_obstacleDrawPhase == 0)
            text += "Point to the floor and press <color=#62E6A5>Right Trigger</color> to start drawing.\nPress <color=#62E6A5>A</color> to finish and proceed.";
        else if (_obstacleDrawPhase == 1)
            text += "Drag along the floor to set the <color=#62E6A5>Width</color> and press Right Trigger.";
        else if (_obstacleDrawPhase == 2)
            text += "Drag to set the <color=#62E6A5>Depth</color> and press Right Trigger.";
        else if (_obstacleDrawPhase == 3)
            text += "Move controller UP to set the <color=#62E6A5>Height</color> and press Right Trigger.";
        
        if (_obstacleDrawPhase > 0)
            text += "\n\nPress B to cancel current obstacle.";
            
        _choiceText.text = text;
    }

    private void UpdateManualObstacleSetup()
    {
        bool hasFloorPoint = TryGetManualFloorPoint(out Vector3 rayOrigin, out Vector3 floorPoint);
        
        if (!hasFloorPoint)
        {
            UpdateManualPreviewLine(rayOrigin, rayOrigin + GetRightControllerForward() * manualSetupRayDistance, false);
        }
        else
        {
            UpdateManualPreviewLine(rayOrigin, floorPoint, true);
        }
        
        if (_obstacleDrawPhase > 0)
        {
            if (_obsPreviewBox == null)
            {
                _obsPreviewBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(_obsPreviewBox.GetComponent<Collider>());
                var renderer = _obsPreviewBox.GetComponent<Renderer>();
                if (_manualPreviewMaterial != null) renderer.material = _manualPreviewMaterial;
                else renderer.material.color = new Color(0.2f, 0.8f, 0.2f, 0.4f);
            }
            
            Vector3 center = Vector3.zero;
            Vector3 size = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            if (_obstacleDrawPhase == 1)
            {
                Vector3 widthVec = hasFloorPoint ? floorPoint - _obsCorner1 : Vector3.forward * 0.1f;
                widthVec.y = 0;
                float w = widthVec.magnitude;
                if (w < 0.01f) { widthVec = Vector3.forward * 0.01f; w = 0.01f; }
                rot = Quaternion.LookRotation(widthVec, Vector3.up);
                size = new Vector3(0.01f, 0.01f, w);
                center = _obsCorner1 + widthVec * 0.5f;
            }
            else if (_obstacleDrawPhase == 2)
            {
                Vector3 zAxis = (_obsCorner2 - _obsCorner1).normalized;
                Vector3 xAxis = Vector3.Cross(Vector3.up, zAxis).normalized;
                Vector3 vecToFloor = floorPoint - _obsCorner1;
                float depth = Vector3.Dot(vecToFloor, xAxis);
                float width = Vector3.Distance(_obsCorner1, _obsCorner2);
                size = new Vector3(Mathf.Abs(depth), 0.01f, width);
                center = _obsCorner1 + zAxis * (width * 0.5f) + xAxis * (depth * 0.5f);
                rot = Quaternion.LookRotation(zAxis, Vector3.up);
            }
            else if (_obstacleDrawPhase == 3)
            {
                Vector3 zAxis = (_obsCorner2 - _obsCorner1).normalized;
                Vector3 xAxis = Vector3.Cross(Vector3.up, zAxis).normalized;
                Vector3 vecToFloor = _obsCorner3 - _obsCorner1;
                float depth = Vector3.Dot(vecToFloor, xAxis);
                float width = Vector3.Distance(_obsCorner1, _obsCorner2);
                
                // Calculate height using ray intersection with a vertical plane facing the user
                float height = 0.5f; // default
                Vector3 camPos = Camera.main != null ? Camera.main.transform.position : rayOrigin;
                Vector3 planeNormal = Vector3.ProjectOnPlane(camPos - _obsCorner1, Vector3.up).normalized;
                if (planeNormal.sqrMagnitude < 0.1f) planeNormal = Vector3.forward;
                Plane verticalPlane = new Plane(planeNormal, _obsCorner1);
                
                Vector3 rayDir = GetRightControllerForward();
                if (verticalPlane.Raycast(new Ray(rayOrigin, rayDir), out float distance))
                {
                    Vector3 hitPoint = rayOrigin + rayDir * distance;
                    height = Mathf.Max(0.1f, hitPoint.y - _obsCorner1.y);
                }
                else
                {
                    // Fallback to controller height if ray misses (e.g. looking away)
                    height = Mathf.Max(0.1f, rayOrigin.y - _obsCorner1.y);
                }

                size = new Vector3(Mathf.Abs(depth), height, width);
                center = _obsCorner1 + zAxis * (width * 0.5f) + xAxis * (depth * 0.5f) + Vector3.up * (height * 0.5f);
                rot = Quaternion.LookRotation(zAxis, Vector3.up);
            }

            _obsPreviewBox.transform.position = center;
            _obsPreviewBox.transform.rotation = rot;
            _obsPreviewBox.transform.localScale = size;
        }

        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch))
        {
            if (_obstacleDrawPhase == 0 && hasFloorPoint)
            {
                _obsCorner1 = floorPoint;
                _obstacleDrawPhase = 1;
            }
            else if (_obstacleDrawPhase == 1 && hasFloorPoint)
            {
                _obsCorner2 = floorPoint;
                if (Vector3.Distance(_obsCorner1, _obsCorner2) > 0.1f)
                    _obstacleDrawPhase = 2;
            }
            else if (_obstacleDrawPhase == 2 && hasFloorPoint)
            {
                _obsCorner3 = floorPoint;
                _obstacleDrawPhase = 3;
            }
            else if (_obstacleDrawPhase == 3)
            {
                _obstacleDrawPhase = 0;
                if (_obsPreviewBox != null)
                {
                    SaveSingleManualObstacle(_obsPreviewBox.transform.position, _obsPreviewBox.transform.localScale, _obsPreviewBox.transform.rotation);
                    Destroy(_obsPreviewBox);
                    _obsPreviewBox = null;
                }
            }
            UpdateObstacleSetupPrompt();
        }
        else if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
        {
            if (_obstacleDrawPhase > 0)
            {
                _obstacleDrawPhase = 0;
                if (_obsPreviewBox != null) Destroy(_obsPreviewBox);
                UpdateObstacleSetupPrompt();
            }
            else if (_manualObstacleDataList.Count > 0)
            {
                _manualObstacleDataList.RemoveAt(_manualObstacleDataList.Count - 1);
                var obj = _manualObstacleObjects[_manualObstacleObjects.Count - 1];
                PlacementObstacles.Remove(obj.GetComponent<BoxCollider>());
                foreach (var col in obj.GetComponentsInChildren<Collider>()) PlacementFloors.Remove(col);
                Destroy(obj);
                _manualObstacleObjects.RemoveAt(_manualObstacleObjects.Count - 1);
                SaveManualObstaclesToFile();
            }
        }
        else if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
        {
            if (_obstacleDrawPhase == 0)
            {
                FinishManualObstacles();
            }
        }
    }

    private void SaveSingleManualObstacle(Vector3 center, Vector3 size, Quaternion rotation)
    {
        var boxData = new BoxData
        {
            center = center,
            size = size,
            rotation = rotation
        };
        
        Transform trackingSpace = TrackingSpace;
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        
        boxData.trackingLocalCenter = trackingSpace != null ? trackingSpace.InverseTransformPoint(center) : center;
        boxData.trackingLocalRotation = trackingSpace != null ? Quaternion.Inverse(trackingSpace.rotation) * rotation : rotation;
        
        if (room != null)
        {
            boxData.roomLocalCenter = room.transform.InverseTransformPoint(center);
            boxData.roomLocalRotation = Quaternion.Inverse(room.transform.rotation) * rotation;
        }
        
        _manualObstacleDataList.Add(boxData);
        BuildSingleObstacleCollider(boxData);
        SaveManualObstaclesToFile();
    }

    private void BuildSingleObstacleCollider(BoxData boxData)
    {
        var obj = new GameObject("ManualObstacle");
        obj.transform.SetPositionAndRotation(boxData.center, boxData.rotation);
        
        var col = obj.AddComponent<BoxCollider>();
        col.size = boxData.size;
        PlacementObstacles.Add(col);

        // Add occlusion rendering
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var filter = obj.AddComponent<MeshFilter>();
        filter.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        
        Shader occlusionShader = Shader.Find("AR/Occlusion");
        if (OcclusionMaterial != null)
        {
            renderer.material = OcclusionMaterial;
        }
        else if (occlusionShader != null)
        {
            renderer.material = new Material(occlusionShader);
        }
        else
        {
            Debug.LogWarning("[Scanner] AR/Occlusion shader not found. Occlusion will not work.");
            Destroy(renderer);
            Destroy(filter);
        }
        Destroy(temp);

        obj.transform.localScale = boxData.size;
        col.size = Vector3.one; // because local scale covers it
        var floorObj = new GameObject("ObstacleFloor");
        floorObj.transform.SetParent(obj.transform);
        floorObj.transform.localPosition = new Vector3(0, boxData.size.y / 2f, 0); // top surface
        var floorCol = floorObj.AddComponent<BoxCollider>();
        floorCol.size = new Vector3(boxData.size.x, 0.01f, boxData.size.z);
        PlacementFloors.Add(floorCol);
        
        _manualObstacleObjects.Add(obj);
    }

    private void SaveManualObstaclesToFile()
    {
        var data = new ManualObstacleData { obstacles = _manualObstacleDataList };
        File.WriteAllText(ManualObstacleFilePath, JsonUtility.ToJson(data, true));
    }

    private bool TryLoadManualObstacles()
    {
        if (!File.Exists(ManualObstacleFilePath)) return false;
        try
        {
            var data = JsonUtility.FromJson<ManualObstacleData>(File.ReadAllText(ManualObstacleFilePath));
            if (data == null || data.obstacles == null) return false;

            Transform trackingSpace = TrackingSpace;
            MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;

            foreach (var box in data.obstacles)
            {
                if (room != null && box.roomLocalCenter != Vector3.zero)
                {
                    box.center = room.transform.TransformPoint(box.roomLocalCenter);
                    box.rotation = room.transform.rotation * box.roomLocalRotation;
                }
                else if (trackingSpace != null)
                {
                    box.center = trackingSpace.TransformPoint(box.trackingLocalCenter);
                    box.rotation = trackingSpace.rotation * box.trackingLocalRotation;
                }
                
                _manualObstacleDataList.Add(box);
                BuildSingleObstacleCollider(box);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[ManualObstacles] Load failed: " + e.Message);
            return false;
        }
    }

    private void FinishManualObstacles()
    {
        _manualObstacleSetupActive = false;
        if (_choiceText != null) Destroy(_choiceText.gameObject);
        _isWaitingForChoice = false;
        IsWaitingForChoice = false;
        ClearManualSetupVisuals();
        Debug.Log($"[ManualObstacles] Saved {_manualObstacleDataList.Count} obstacles.");
        SignalStartupFlowComplete();
    }
}
