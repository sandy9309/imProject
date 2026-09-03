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
    public static readonly HashSet<BoxCollider> PlacementWalls = new HashSet<BoxCollider>();
    public static readonly HashSet<Collider> PlacementFloors = new HashSet<Collider>();
    public static int ActiveWallColliderCount { get; private set; }
    public static bool IsWaitingForChoice { get; private set; }
    public static bool StartupFlowComplete { get; private set; }
    public static event System.Action StartupFlowCompleted;
    public static event System.Action StartupFlowReset;
    [Header("Startup scene choice")]
    [Min(0f)] public float initialLoadWaitSeconds = 2f;
    public bool autoScanWhenNoSavedScene = true;

    [Header("Wall collision")]
    [Tooltip("Thickness of the invisible wall colliders, in metres.")]
    [Min(0.01f)] public float wallColliderThickness = 0.08f;
    [Tooltip("Layer used by generated MRUK wall colliders.")]
    [Range(0, 31)] public int wallColliderLayer = 8;
    [Tooltip("Height of manually created walls, in metres.")]
    [Min(0.5f)] public float manualWallHeight = 2.6f;
    [Tooltip("Maximum distance of the controller ray used for manual wall setup.")]
    [Min(1f)] public float manualSetupRayDistance = 8f;
    [Tooltip("Hold Y for this many seconds to replace saved manual walls.")]
    [Min(0.5f)] public float resetManualWallsHoldSeconds = 2f;

    private bool _isScanning;
    private bool _isWaitingForChoice;
    private bool _manualSetupActive;
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
    private readonly List<GameObject> _manualMarkers = new List<GameObject>();
    private OVRCameraRig _cameraRig;
    private LineRenderer _manualPreviewLine;
    private LineRenderer _manualOutlineLine;
    private Material _manualPreviewMaterial;

    private const int ManualWallDataVersion = 1;
    private const string ManualWallFileName = "manual-walls.json";

    [Serializable]
    private sealed class ManualWallData
    {
        public int version = ManualWallDataVersion;
        public float wallHeight = 2.6f;
        public float wallThickness = 0.08f;
        public List<Vector3> points = new List<Vector3>();
    }

    private void Awake()
    {
        IsWaitingForChoice = false;
        StartupFlowComplete = false;
    }

    private IEnumerator Start()
    {
        Debug.Log("[Scanner] Loading the saved room from this headset...");
        yield return new WaitForSeconds(initialLoadWaitSeconds);

        // MRUK is configured for manual loading in this scene. Wait for its
        // singleton to be initialized instead of racing its Awake/Start flow.
        float waitDeadline = Time.realtimeSinceStartup + 10f;
        while (MRUK.Instance == null && Time.realtimeSinceStartup < waitDeadline)
            yield return null;

        MRUK.LoadDeviceResult loadResult = MRUK.LoadDeviceResult.NotInitialized;
        if (MRUK.Instance != null)
        {
            // Do not let MRUK start Space Setup here. This startup pass only
            // checks for an already-saved room; TriggerNewScan owns setup.
            Task<MRUK.LoadDeviceResult> loadTask =
                MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            while (!loadTask.IsCompleted)
                yield return null;

            if (loadTask.IsCanceled)
                Debug.LogWarning("[Scanner] Saved-room loading was cancelled.");
            else if (loadTask.IsFaulted)
                Debug.LogError("[Scanner] Failed to load the saved room: " + loadTask.Exception);
            else
            {
                loadResult = loadTask.Result;
                Debug.Log("[Scanner] Saved-room load result: " + loadResult);
            }
        }
        else
            Debug.LogError("[Scanner] MRUK.Instance did not initialize within 10 seconds.");

        _roomLoadStatus = loadResult.ToString();
        if (loadResult == MRUK.LoadDeviceResult.Success &&
            MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            RebuildWallColliders();
        }
        else
        {
            Debug.LogWarning("[Scanner] MRUK room unavailable. Checking saved manual walls without entering manual setup.");
            if (TryLoadManualWalls()) _roomLoadStatus += " / saved manual walls";
        }
        yield return AskWhetherToRescan();
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
        // Hold Y to deliberately replace the saved manual calibration.
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
        prompt.transform.localScale = Vector3.one * 0.0025f;

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
            (hasSaved ? "<color=#62E6A5>A: Use this room</color>\n" : "No usable saved walls loaded\n") +
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

            if (loadResult == MRUK.LoadDeviceResult.Success &&
                MRUK.Instance.GetCurrentRoom() != null)
            {
                RebuildWallColliders();
                Debug.Log("[Scanner] The new room was loaded successfully.");
            }
            else
            {
                Debug.LogWarning("[Scanner] Room setup closed, but no usable room was loaded.");
                if (TryLoadManualWalls()) _roomLoadStatus += " / saved manual walls";
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[Scanner] Room scanning failed: " + exception.Message);
            _roomLoadStatus = "Scan/load failed (see device log)";
            if (ActiveWallColliderCount == 0 && TryLoadManualWalls()) _roomLoadStatus += " / saved manual walls";
        }
        finally
        {
            _isScanning = false;
            if (this != null && isActiveAndEnabled) StartCoroutine(AskWhetherToRescan());
        }
    }

    private string ManualWallFilePath => Path.Combine(Application.persistentDataPath, ManualWallFileName);

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
        prompt.transform.localScale = Vector3.one * 0.0025f;
        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 42f;
        _choiceText.rectTransform.sizeDelta = new Vector2(720f, 240f);
        _choiceText.text = "<b>REPLACE SAVED WALLS?</b>\n\n" +
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

        _manualSetupActive = false;
        _isWaitingForChoice = false;
        IsWaitingForChoice = false;
        ClearManualSetupVisuals();
        Debug.Log($"[ManualWalls] Saved and built {ActiveWallColliderCount} walls.");
        SignalStartupFlowComplete();
    }

    private bool SaveManualWalls()
    {
        try
        {
            Transform trackingSpace = TrackingSpace;
            var data = new ManualWallData
            {
                wallHeight = manualWallHeight,
                wallThickness = wallColliderThickness
            };

            foreach (Vector3 worldPoint in _manualWallPoints)
                data.points.Add(trackingSpace != null ? trackingSpace.InverseTransformPoint(worldPoint) : worldPoint);

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
            if (data == null || data.version != ManualWallDataVersion ||
                data.points == null || data.points.Count < 3)
            {
                Debug.LogWarning("[ManualWalls] Saved wall data is missing or incompatible.");
                return false;
            }

            manualWallHeight = Mathf.Max(0.5f, data.wallHeight);
            wallColliderThickness = Mathf.Max(0.01f, data.wallThickness);
            Transform trackingSpace = TrackingSpace;
            var worldPoints = new List<Vector3>(data.points.Count);
            foreach (Vector3 localPoint in data.points)
                worldPoints.Add(trackingSpace != null ? trackingSpace.TransformPoint(localPoint) : localPoint);

            bool built = BuildManualWallColliders(worldPoints);
            Debug.Log(built
                ? $"[ManualWalls] Loaded {ActiveWallColliderCount} saved walls."
                : "[ManualWalls] Saved data could not create valid wall colliders.");
            return built;
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualWalls] Load failed: " + exception.Message);
            return false;
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
        if (ActiveWallColliderCount >= 3) BuildManualFloor(points);
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

    private void DeleteManualWallFile()
    {
        try
        {
            if (File.Exists(ManualWallFilePath))
                File.Delete(ManualWallFilePath);
            Debug.Log("[ManualWalls] Saved manual wall data deleted.");
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualWalls] Could not delete saved data: " + exception.Message);
        }
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
        prompt.transform.localScale = Vector3.one * 0.0025f;
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

        MRUKAnchor.SceneLabels wallLabels =
            MRUKAnchor.SceneLabels.WALL_FACE |
            MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE |
            MRUKAnchor.SceneLabels.INNER_WALL_FACE;

        int createdCount = 0;
        Debug.Log($"[Scanner] Room contains {room.Anchors.Count} anchors before wall filtering.");
        foreach (var anchor in room.Anchors)
        {
            if ((anchor.Label & wallLabels) == 0)
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
        PlacementWalls.RemoveWhere(wall => wall == null);
        ActiveWallColliderCount = 0;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _manualSetupActive = false;
        _resetConfirmationActive = false;
        FinishChoice();
        ClearManualSetupVisuals();
        ClearWallColliders();
    }
}
