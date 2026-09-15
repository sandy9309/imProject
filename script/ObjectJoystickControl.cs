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

    void LateUpdate()
    {
        if (_interactableView == null || visualModel == null || _dummyAnchor == null) return;

        bool isGrabbed = _interactableView.State == InteractableState.Select;

        if (isGrabbed)
        {
            // 1. 強制母物件不准傾倒
            Vector3 rot = transform.eulerAngles;
            transform.eulerAngles = new Vector3(0, rot.y, 0);

            // 取得目前的目標位置
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

            // 3. 防穿牆神盾
            if (Camera.main != null)
            {
                Vector3 headPos = Camera.main.transform.position;
                Vector3 dirToTarget = desiredPos - headPos;
                float distToTarget = dirToTarget.magnitude;

                Collider col = GetComponent<Collider>();
                if (col != null) col.enabled = false;

                if (Physics.Raycast(headPos, dirToTarget.normalized, out RaycastHit hit, distToTarget))
                {
                    float safeDistance = Mathf.Max(0, hit.distance - wallBuffer);
                    desiredPos = headPos + dirToTarget.normalized * safeDistance;
                }

                if (col != null) col.enabled = true;
            }

            visualModel.position = desiredPos;

            // 4. 處理搖桿左右旋轉
            if (Mathf.Abs(joystick.x) > 0.05f)
            {
                visualModel.Rotate(Vector3.up, joystick.x * rotationSpeed * Time.deltaTime, Space.World);
            }
        }
        else if (_lastState == InteractableState.Select && !isGrabbed)
        {
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
}
