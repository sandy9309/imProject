using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using TMPro;
using UnityEngine;

public class SceneAutoScanner : MonoBehaviour
{
    public static bool IsWaitingForChoice { get; private set; }
    public static bool StartupFlowComplete { get; private set; }
    public static event System.Action StartupFlowCompleted;
    [Header("Startup scene choice")]
    [Min(0f)] public float initialLoadWaitSeconds = 2f;
    public bool autoScanWhenNoSavedScene = true;

    [Header("Wall collision")]
    [Tooltip("Thickness of the invisible wall colliders, in metres.")]
    [Min(0.01f)] public float wallColliderThickness = 0.08f;
    [Tooltip("Layer used by generated MRUK wall colliders.")]
    [Range(0, 31)] public int wallColliderLayer = 8;

    private bool _isScanning;
    private bool _isWaitingForChoice;
    private TextMeshPro _choiceText;
    private readonly List<GameObject> _wallColliderObjects = new List<GameObject>();

    private void Awake()
    {
        IsWaitingForChoice = false;
        StartupFlowComplete = false;
    }

    private IEnumerator Start()
    {
        Debug.Log("[Scanner] Loading the saved room from this headset...");
        yield return new WaitForSeconds(initialLoadWaitSeconds);

        if (MRUK.Instance != null)
        {
            MRUK.Instance.LoadSceneFromDevice();
            yield return new WaitForSeconds(1f);
        }

        if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            RebuildWallColliders();
            yield return AskWhetherToRescan();
            yield break;
        }

        Debug.Log("[Scanner] No saved room was found.");
        if (autoScanWhenNoSavedScene)
            TriggerNewScan();
    }

    private IEnumerator AskWhetherToRescan()
    {
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;
        ShowChoiceText();
        Debug.Log("[Scanner] Saved room found. Press A to use it, or B to scan again.");

        while (_isWaitingForChoice)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                Debug.Log("[Scanner] Using the saved room.");
                FinishChoice();
                SignalStartupFlowComplete();
            }
            else if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                Debug.Log("[Scanner] User requested a new room scan.");
                FinishChoice();
                TriggerNewScan();
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
        // Y remains an emergency shortcut for starting a new scan at any time.
        if (OVRInput.GetDown(OVRInput.RawButton.Y))
        {
            FinishChoice();
            TriggerNewScan();
        }
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
        _choiceText.fontSize = 44f;
        _choiceText.rectTransform.sizeDelta = new Vector2(600f, 240f);
        _choiceText.text =
            "<b>Room setup found</b>\n\n" +
            "<color=#62E6A5>Press A</color>  Use saved room\n" +
            "<color=#FFB45E>Press B</color>  Scan again";
    }

    private void FinishChoice()
    {
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
            var setupTask = OVRScene.RequestSpaceSetup();
            await setupTask;
            await Task.Delay(1000);

            if (MRUK.Instance == null)
            {
                Debug.LogError("[Scanner] MRUK.Instance was not found.");
                return;
            }

            MRUK.Instance.ClearScene();
            await MRUK.Instance.LoadSceneFromDevice();
            await Task.Delay(500);

            if (MRUK.Instance.GetCurrentRoom() != null)
            {
                RebuildWallColliders();
                Debug.Log("[Scanner] The new room was loaded successfully.");
            }
            else
                Debug.LogWarning("[Scanner] Room setup closed, but no usable room was loaded.");
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[Scanner] Room scanning failed: " + exception.Message);
        }
        finally
        {
            _isScanning = false;
            SignalStartupFlowComplete();
        }
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
        foreach (var anchor in room.Anchors)
        {
            if ((anchor.Label & wallLabels) == 0 || !anchor.PlaneRect.HasValue)
                continue;

            Rect plane = anchor.PlaneRect.Value;
            var wallObject = new GameObject($"MRWallCollider_{createdCount}");
            wallObject.layer = wallColliderLayer;
            wallObject.transform.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);

            var wallCollider = wallObject.AddComponent<BoxCollider>();
            wallCollider.center = new Vector3(plane.center.x, plane.center.y, 0f);
            wallCollider.size = new Vector3(
                Mathf.Max(0.01f, plane.width),
                Mathf.Max(0.01f, plane.height),
                Mathf.Max(0.01f, wallColliderThickness));
            wallCollider.isTrigger = false;

            _wallColliderObjects.Add(wallObject);
            createdCount++;
        }

        Physics.SyncTransforms();
        Debug.Log($"[Scanner] Built {createdCount} invisible MRUK wall colliders.");
    }

    private void ClearWallColliders()
    {
        foreach (GameObject wallObject in _wallColliderObjects)
        {
            if (wallObject != null)
                Destroy(wallObject);
        }
        _wallColliderObjects.Clear();
    }

    private void OnDisable()
    {
        FinishChoice();
        ClearWallColliders();
    }
}
