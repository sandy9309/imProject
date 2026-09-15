using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;
using System.Threading.Tasks;

public class SceneAutoScanner : MonoBehaviour
{
    private bool _isScanning = false;

    IEnumerator Start()
    {
        // 使用 Unity 原生的等待機制，比 Task.Delay 更安全，不會導致執行緒迷失
        Debug.Log("[Scanner] 遊戲啟動，等待 2 秒...");
        yield return new WaitForSeconds(2.0f);
        
        TriggerNewScan();
    }

    void Update()
    {
        // 【無敵備用方案】隨時按下右手把的 A 鍵，強制開啟掃描！
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("[Scanner] 玩家按下 A 鍵，手動觸發掃描！");
            TriggerNewScan();
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

        // --- 強制開啟透視 (Passthrough) 與關閉背景遮擋 ---
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
        // ----------------------------------------------

        try 
        {
            var task = OVRScene.RequestSpaceSetup();
            await task;

            Debug.Log("[Scanner] 掃描完成，等待 1 秒讓系統同步資料庫...");
            await Task.Delay(1000); 

            if (MRUK.Instance != null)
            {
                Debug.Log("[Scanner] 正在強制刷新 MRUK 場景...");
                MRUK.Instance.ClearScene();
                MRUK.Instance.LoadSceneFromDevice();
                Debug.Log("[Scanner] 刷新完成！");
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
}
