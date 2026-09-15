using UnityEngine;

public class SurfaceSnapper : MonoBehaviour
{
    public LayerMask surfaceLayer; 
    public float offsetFromWall = 0.05f; 
    public float snapDistance = 0.3f; // 縮短偵測距離，只有靠近時才吸附

    void LateUpdate() // 必須在 LateUpdate 執行，因為它要覆蓋抓取腳本的結果
{
    RaycastHit hit;
    // 往後方偵測牆壁
    if (Physics.Raycast(transform.position, -transform.forward, out hit, 0.2f, surfaceLayer))
    {
        // 如果靠牆太近，強制把座標推回牆面外一點點
        transform.position = hit.point + hit.normal * 0.05f;
    }
}

    void SnapLogic()
    {
        RaycastHit hit;
        // 往後方發射一條射線偵測牆壁
        // 注意：這裡假設你的方塊「背面」朝向牆壁
        if (Physics.Raycast(transform.position, transform.forward * -1, out hit, snapDistance, surfaceLayer))
        {
            // 進入吸附狀態：強制貼齊牆面
            transform.position = hit.point + hit.normal * offsetFromWall;
            transform.rotation = Quaternion.LookRotation(hit.normal);
        }
        // 如果射線沒打到牆（代表你手拉開了，距離超過 snapDistance），就不執行任何動作
        // 這樣方塊就會跟隨原本的抓取系統移動
    }
    void FixedUpdate()
    {
        // 限制物理速度的最大值
        GetComponent<Rigidbody>().velocity = Vector3.ClampMagnitude(GetComponent<Rigidbody>().velocity, 5f);
    }
}

