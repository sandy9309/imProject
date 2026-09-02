using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using System.Threading.Tasks;
using TMPro;

public class SceneAutoScanner : MonoBehaviour
{
    public static bool IsWaitingForChoice { get; private set; }

    [Header("Startup scene choice")]
    [Min(0f)] public float initialLoadWaitSeconds = 2f;
    public bool autoScanWhenNoSavedScene = true;

    private bool _isScanning = false;
    private bool _isWaitingForChoice;
    private TextMeshPro _choiceText;
    private List<GameObject> _spawnedColliders = new List<GameObject>();

    private void Awake()
    {
        IsWaitingForChoice = false;
    }

    IEnumerator Start()
    {
        Debug.Log("[Scanner] 遊戲啟動，嘗試載入既有房間資料...");
        yield return new WaitForSeconds(initialLoadWaitSeconds);

        if (MRUK.Instance != null)
        {
            MRUK.Instance.LoadSceneFromDevice();
            yield return new WaitForSeconds(1f);
        }

        if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            yield return AskWhetherToRescan();
            yield break;
        }

        Debug.Log("[Scanner] 找不到既有房間資料。");
        if (autoScanWhenNoSavedScene)
            TriggerNewScan();
    }

    private IEnumerator AskWhetherToRescan()
    {
        _isWaitingForChoice = true;
        IsWaitingForChoice = true;
        ShowChoiceText();
        Debug.Log("[Scanner] 已找到既有房間。按 A 沿用，或按 B 重新掃描。");

        while (_isWaitingForChoice)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                Debug.Log("[Scanner] 玩家選擇沿用既有房間。");
                FinishChoice();
                GenerateCollidersAndVisuals();
            }
            else if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                Debug.Log("[Scanner] 玩家選擇重新掃描房間。");
                FinishChoice();
                TriggerNewScan();
            }

            yield return null;
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.RawButton.Y))
        {
            Debug.Log("[Scanner] 玩家按下 Y 鍵，手動觸發掃描！");
            FinishChoice();
            TriggerNewScan();
        }
    }

    private void ShowChoiceText()
    {
        if (Camera.main == null) return;

        GameObject prompt = new GameObject("SceneScanChoice");
        prompt.transform.SetParent(Camera.main.transform, false);
        prompt.transform.localPosition = new Vector3(0f, 0f, 1.2f);
        prompt.transform.localRotation = Quaternion.identity;
        prompt.transform.localScale = Vector3.one * 0.0025f;

        _choiceText = prompt.AddComponent<TextMeshPro>();
        _choiceText.alignment = TextAlignmentOptions.Center;
        _choiceText.fontSize = 44f;
        _choiceText.rectTransform.sizeDelta = new Vector2(600f, 240f);
        _choiceText.text =
            "<b>偵測到已設定的空間</b>\n\n" +
            "<color=#62E6A5>按 A</color>  沿用目前空間\n" +
            "<color=#FFB45E>按 B</color>  重新掃描";
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
        Debug.Log("[Scanner] 啟動空間掃描介面...");

        if (OVRManager.instance != null)
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
        }

        var ptLayer = FindObjectOfType<OVRPassthroughLayer>();
        if (ptLayer != null)
        {
            ptLayer.hidden = false;
        }

        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0, 0, 0, 0);
        }

        GameObject env = GameObject.Find("Environment");
        if (env != null)
        {
            env.SetActive(false);
        }

        try 
        {
            // 呼叫系統掃描 UI (加入 10 秒防死鎖超時保護)
            var setupTask = OVRScene.RequestSpaceSetup();
            
            // 手動監控 OVRTask，最多等待 10 秒防死鎖
            int waitTime = 0;
            while (!setupTask.IsCompleted && waitTime < 10000)
            {
                await Task.Delay(500);
                waitTime += 500;
            }

            if (!setupTask.IsCompleted)
            {
                Debug.LogWarning("[Scanner] SpaceSetup 等待超時，強制往下執行...");
            }

            Debug.Log("[Scanner] 掃描完成或超時，等待 1 秒讓系統同步資料庫...");
            await Task.Delay(1000); 

            if (MRUK.Instance != null)
            {
                Debug.Log("[Scanner] 正在強制刷新 MRUK 場景...");
                MRUK.Instance.ClearScene();
                
                // 必須使用 await 等待載入完成
                await MRUK.Instance.LoadSceneFromDevice();
                Debug.Log("[Scanner] 刷新完成！等待房間資料準備就緒...");
                
                // 動態等待 MRUK 載入房間結構 (最多等待 10 秒)
                int retries = 20;
                while (MRUK.Instance.GetCurrentRoom() == null && retries > 0)
                {
                    await Task.Delay(500);
                    retries--;
                }
                
                GenerateCollidersAndVisuals();
            }
            else
            {
                Debug.LogError("[Scanner] 找不到 MRUK.Instance！");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Scanner] 掃描過程發生錯誤：" + e.Message);
        }
        finally
        {
            _isScanning = false;
        }
    }

    private void GenerateCollidersAndVisuals()
    {
        Debug.Log("[Scanner] 開始生成碰撞體...");
        // 1. 清除舊的碰撞體
        foreach(var obj in _spawnedColliders)
        {
            if (obj != null) Destroy(obj);
        }
        _spawnedColliders.Clear();

        // 2. 獲取掃描到的房間
        if (MRUK.Instance == null) 
        {
            Debug.LogError("[Scanner] 失敗：MRUK.Instance 是 null！");
            return;
        }

        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) 
        {
            Debug.LogError("[Scanner] 失敗：MRUK.Instance.GetCurrentRoom() 是 null！房間資料尚未載入或載入失敗！");
            
            // 嘗試手動呼叫 LoadSceneFromDevice 的另一種方式，或是提醒用戶需要先設定 MRUK
            return;
        }

        Debug.Log($"[Scanner] 成功獲取房間！房間內共有 {room.Anchors.Count} 個錨點！");

        // 3. 建立半透明科技藍色材質
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material bpMat = new Material(shader);
        if (bpMat.HasProperty("_BaseColor")) bpMat.SetColor("_BaseColor", new Color(0.2f, 0.6f, 1f, 0.4f));
        if (bpMat.HasProperty("_Color")) bpMat.SetColor("_Color", new Color(0.2f, 0.6f, 1f, 0.4f));

        // 4. 為房間的每一個牆壁、地板與傢俱生成 3D 方塊與碰撞體
        foreach (var anchor in room.Anchors)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "MRUK_Collider_" + anchor.Label;
            
            // 賦予藍色材質
            box.GetComponent<MeshRenderer>().material = bpMat;
            
            // 保留預設的 BoxCollider (提供物理碰撞)，確保它不是 Trigger
            box.GetComponent<BoxCollider>().isTrigger = false;

            box.transform.SetParent(anchor.transform, false);
            
            if (anchor.VolumeBounds.HasValue)
            {
                // 立體傢俱
                box.transform.localPosition = anchor.VolumeBounds.Value.center;
                box.transform.localScale = anchor.VolumeBounds.Value.size;
            }
            else if (anchor.PlaneRect.HasValue)
            {
                // 平面牆壁或地板
                box.transform.localPosition = new Vector3(anchor.PlaneRect.Value.center.x, anchor.PlaneRect.Value.center.y, 0);
                box.transform.localScale = new Vector3(anchor.PlaneRect.Value.width, anchor.PlaneRect.Value.height, 0.01f);
            }
            
            _spawnedColliders.Add(box);
        }
        
        Debug.Log($"[Scanner] 成功生成 {_spawnedColliders.Count} 個實體藍色碰撞體！");
    }

    private void OnDisable()
    {
        FinishChoice();
    }
}
