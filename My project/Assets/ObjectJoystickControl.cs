using UnityEngine;
using Oculus.Interaction;

public class ObjectJoystickControl : MonoBehaviour
{
    [Tooltip("請把方塊底下代表『外觀模型』的子物件 (例如 PlantPot) 拖曳到這裡")]
    public Transform visualModel;
    
    [Tooltip("前後推拉的速度")]
    public float pushPullSpeed = 1.0f;
    
    [Tooltip("水平旋轉的速度")]
    public float rotationSpeed = 90.0f;

    [Tooltip("防穿牆緩衝距離(公尺)，建議設定為 0.1 (10公分)")]
    public float wallBuffer = 0.1f;

    private IInteractableView _interactableView;
    private InteractableState _lastState;
    private Rigidbody _rb;

    // 儲存原始偏移量與計算完美對齊的錨點
    private Vector3 _initialLocalPos;
    private Quaternion _initialLocalRot;
    private Transform _dummyAnchor;

    void Start()
    {
        _interactableView = GetComponentInChildren<IInteractableView>();
        _rb = GetComponent<Rigidbody>(); 

        if (_rb != null)
        {
            // 🌟 啟動延遲固定機制：給予物理引擎時間解決初始生成的穿模問題
            TriggerDelayFreeze();
        }

        if (visualModel != null)
        {
            // 記錄外觀模型一開始與母物件的相對位置 (解決跳位Bug的關鍵)
            _initialLocalPos = visualModel.localPosition;
            _initialLocalRot = visualModel.localRotation;

            // 建立一個隱形的虛擬錨點，綁在外觀模型底下
            _dummyAnchor = new GameObject("JoystickDummyAnchor").transform;
            _dummyAnchor.SetParent(visualModel);
            // 初始化時，讓這個錨點完美重疊在母物件(碰撞體)的中心
            _dummyAnchor.position = transform.position;
            _dummyAnchor.rotation = transform.rotation;
        }
    }

    // 追蹤合法的上一幀位置，用來計算移動軌跡與防止手把硬塞穿牆
    private Vector3 _lastLegalPos;
    private bool _wasGrabbed = false;

    void LateUpdate()
    {
        if (_interactableView == null || visualModel == null || _dummyAnchor == null) return;

        bool isGrabbed = _interactableView.State == InteractableState.Select;

        if (isGrabbed)
        {
            // 🌟 抓取時銷毀功能：如果玩家按下搖桿 (Thumbstick Click)，就直接把這整個傢俱刪除！
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch) || 
                OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch))
            {
                // 🌟 刪除前手動更新快取
                if (ModelLoader.Instance != null) ModelLoader.Instance.UpdateCacheBeforeDestroy(transform.root);

                Destroy(transform.root.gameObject);
                if (ModelLoader.Instance != null) ModelLoader.Instance.TriggerAutoSaveDelay(); // 延遲一幀自動存檔
                return; // 終止後續的移動運算
            }

            // 如果是剛抓起的瞬間，初始化合法位置，並「解除物理鎖定」
            if (!_wasGrabbed) 
            {
                _lastLegalPos = visualModel.position;
                _wasGrabbed = true;
                
                if (_rb != null) 
                {
                    _rb.constraints = RigidbodyConstraints.None; // 玩家抓取時，徹底解鎖
                }
            }

            // 1. 強制母物件不准傾倒
            Vector3 rot = transform.eulerAngles;
            transform.eulerAngles = new Vector3(0, rot.y, 0);

            // 取得目前的目標位置 (這包含了手把帶動的位移)
            Vector3 desiredPos = visualModel.position;

            // 2. 處理搖桿推拉
            Vector2 joystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (Mathf.Abs(joystick.y) > 0.05f && Camera.main != null)
            {
                Vector3 pushDirection = desiredPos - Camera.main.transform.position;
                pushDirection.y = 0; 
                pushDirection.Normalize();
                
                desiredPos += pushDirection * joystick.y * pushPullSpeed * Time.deltaTime;
            }

            // 3. 實體網格精準防穿牆 (SweepTestAll)
            Vector3 currentPos = _lastLegalPos;
            Vector3 moveDelta = desiredPos - currentPos;
            float moveDist = moveDelta.magnitude;

            if (moveDist > 0.0001f && _rb != null)
            {
                // 因為 SweepTest 只能從 Rigidbody 目前的位置掃描
                // 我們必須先暫時把 _rb 移到 currentPos，掃描完再移回來
                Vector3 originalRbPos = transform.position;
                Quaternion originalRbRot = transform.rotation;

                // 暫時移位，準備發射真實的 Mesh 掃描
                transform.position = currentPos;
                transform.rotation = visualModel.rotation;
                Physics.SyncTransforms(); // 強制物理引擎在這一幀先更新空間樹

                // 發射比實際移動長一點的距離 (多加 5 公分) 來確保浮點數精確度，避免微小推擠穿牆
                RaycastHit[] hits = _rb.SweepTestAll(moveDelta.normalized, moveDist + 0.05f, QueryTriggerInteraction.Ignore);
                
                float closestDist = moveDist;
                bool hitValid = false;

                foreach (var hit in hits)
                {
                    // 過濾無效碰撞
                    if (hit.collider.transform.root == transform.root) continue;
                    if (Mathf.Abs(hit.normal.y) >= 0.85f) continue; // 忽略地板、天花板
                    if (IsPlayerOrHand(hit.collider)) continue; // 忽略玩家的手把和身體

                    // 如果一開始就卡在牆壁裡 (distance == 0)，代表傢俱已經因為旋轉等原因微微吃進牆壁了
                    if (hit.distance <= 0.001f) 
                    {
                        Collider myCol = GetComponent<Collider>();
                        if (myCol != null)
                        {
                            // 使用高階物理運算，算出把傢俱「推回牆外」的安全方向
                            bool isPenetrating = Physics.ComputePenetration(
                                myCol, currentPos, visualModel.rotation,
                                hit.collider, hit.collider.transform.position, hit.collider.transform.rotation,
                                out Vector3 escapeDir, out float escapeDist
                            );

                            if (isPenetrating)
                            {
                                // 如果玩家搖桿推的方向 (moveDelta) 跟逃脫方向 (escapeDir) 夾角是正的，
                                // 代表他正在「把傢俱拉出牆壁」，我們就放行這個動作！
                                if (Vector3.Dot(moveDelta.normalized, escapeDir) > 0)
                                {
                                    continue; 
                                }
                            }
                        }

                        // 否則，他正在把傢俱越推越深！這絕對不允許，我們死死鎖住它。
                        if (0 < closestDist)
                        {
                            closestDist = 0;
                            hitValid = true;
                        }
                        continue;
                    }

                    if (hit.distance < closestDist)
                    {
                        closestDist = hit.distance;
                        hitValid = true;
                    }
                }

                if (hitValid)
                {
                    // 撞到了！精準停在撞擊點 (0 穿透)
                    // 退後 1 mm 避免緊貼造成摩擦卡死
                    desiredPos = currentPos + moveDelta.normalized * Mathf.Max(0, closestDist - 0.001f);
                }

                // 掃描完畢，把 Rigidbody 還原回原本的位置 (Meta XR ISDK 期待的位置)
                transform.position = originalRbPos;
                transform.rotation = originalRbRot;
                Physics.SyncTransforms();
            }

            visualModel.position = desiredPos;
            _lastLegalPos = visualModel.position; // 紀錄為下一次的合法起點

            // 4. 處理搖桿左右旋轉
            if (Mathf.Abs(joystick.x) > 0.05f)
            {
                visualModel.Rotate(Vector3.up, joystick.x * rotationSpeed * Time.deltaTime, Space.World);
            }
        }
        else if (_lastState == InteractableState.Select && !isGrabbed)
        {
            _wasGrabbed = false; // 重設抓取狀態，下一次抓取才會重新抓取初始位置

            // 5. 【修復瞬移 Bug】：放開瞬間，精準對齊物理本體
            // 利用隱形錨點，算出母物件該去的世界座標
            Vector3 targetParentPos = _dummyAnchor.position;
            Quaternion targetParentRot = _dummyAnchor.rotation;
            
            // 先拔除模型，以免跟著母物件亂動
            visualModel.SetParent(null); 
            
            if (_rb != null)
            {
                // 暫時關閉物理模擬，強迫底層物理引擎吃下新的座標，拒絕反彈
                bool wasKinematic = _rb.isKinematic;
                _rb.isKinematic = true; 
                
                _rb.position = targetParentPos;
                _rb.rotation = targetParentRot;
                transform.position = targetParentPos;
                transform.rotation = targetParentRot;
                
                _rb.velocity = Vector3.zero; 
                _rb.angularVelocity = Vector3.zero;
                
                _rb.isKinematic = wasKinematic;
                
                // 啟動強制煞車協程，抵銷 Meta XR 賦予的揮動慣性
                StartCoroutine(ForceZeroVelocity(_rb));

                // 🌟 放手瞬間：自動存檔！(延遲 1 秒，等待物理慣性與位置穩定)
                if (ModelLoader.Instance != null)
                {
                    ModelLoader.Instance.TriggerAutoSaveDelay(1000);
                }
                
                // 🌟 放手瞬間：觸發延遲固定機制
                TriggerDelayFreeze();
            }
            else
            {
                transform.position = targetParentPos;
                transform.rotation = targetParentRot;
            }

            // 把模型裝回去，並恢復它最初始的相對位置，保證視覺上 0 跳動！
            visualModel.SetParent(transform); 
            visualModel.localPosition = _initialLocalPos;
            visualModel.localRotation = _initialLocalRot;
        }

        _lastState = _interactableView.State;
    }

    // 過濾器：判斷碰到的東西是不是玩家自己的頭、手、或控制器
    private bool IsPlayerOrHand(Collider col)
    {
        if (col.isTrigger) return true;
        if (Camera.main != null && col.transform.root == Camera.main.transform.root) return true;
        
        Transform curr = col.transform;
        while (curr != null)
        {
            string n = curr.name.ToLower();
            // 過濾常見的 VR 玩家節點關鍵字
            if (n.Contains("ovr") || n.Contains("xr origin") || n.Contains("interaction"))
            {
                return true;
            }
            curr = curr.parent;
        }
        return false;
    }

    // 強制煞車協程：在放開手把後的 3 個物理幀內，強制把速度歸零，避免 Meta XR 殘留的揮動慣性導致傢俱撞牆彈飛
    private System.Collections.IEnumerator ForceZeroVelocity(Rigidbody targetRb)
    {
        for (int i = 0; i < 3; i++)
        {
            if (targetRb != null)
            {
                targetRb.velocity = Vector3.zero;
                targetRb.angularVelocity = Vector3.zero;
            }
            yield return new UnityEngine.WaitForFixedUpdate();
        }
    }

    // ==========================================
    // 延遲固定機制 (Delay Freeze Constraints)
    // ==========================================
    public void TriggerDelayFreeze()
    {
        StartCoroutine(DelayFreezeRoutine());
    }

    private System.Collections.IEnumerator DelayFreezeRoutine()
    {
        if (_rb == null) yield break;

        // 1. 先徹底解除物理限制，讓 Unity 的物理引擎有能力把「稍微卡在牆壁裡的傢俱」給彈出來
        _rb.constraints = RigidbodyConstraints.None;

        // 2. 如果目前模型還在下載中 (被 ModelLoader 強制設為 isKinematic)，就一直等
        while (_rb.isKinematic)
        {
            yield return null;
        }

        // 3. 解除下載鎖定後，給予物理引擎 1 秒鐘的緩衝時間，讓它自然掉落地板並解決牆壁推擠
        yield return new WaitForSeconds(1.0f);

        // 4. 如果這 1 秒內玩家又把它抓起來了，就馬上終止固定程序！
        if (_interactableView != null && _interactableView.State == InteractableState.Select) yield break;

        // 5. 經過 1 秒的沉澱，傢俱已經穩穩待在地上且沒有穿牆了，這時再把它死死鎖住！
        // 鎖死所有位移 (X/Y/Z) 與所有旋轉，達成完美「防推擠、防上飄」效果。
        _rb.constraints = RigidbodyConstraints.FreezeAll;
    }
}
