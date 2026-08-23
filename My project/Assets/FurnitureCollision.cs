using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class VRFurnitureGrab : MonoBehaviour
{
    [Header("抓取設定")]
    public bool isGrabbed = false;

    [Header("碰撞音效")]
    public AudioSource collisionAudio;

    [Header("碰撞檢測大小")]
    public Vector3 checkBoxSize = new Vector3(1.5f, 1f, 1.5f);

    private Rigidbody rb;
    private Collider col;

    // 抓取期間持續記錄最後一個沒有和其他家具重疊的位置。
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        if (isGrabbed && !IsOverlappingFurniture())
        {
            lastValidPosition = transform.position;
            lastValidRotation = transform.rotation;
        }
    }

    // -------------------
    // 抓取開始
    public void OnGrab()
    {
        isGrabbed = true;
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;

        rb.isKinematic = true;
        rb.useGravity = false;

        col.isTrigger = true; // 可以穿牆穿家具
    }

    // -------------------
    // 抓取結束 / 放下
    public void OnRelease()
    {
        isGrabbed = false;

        // 先檢查是否重疊其他家具
        if (IsOverlappingFurniture())
        {
            // 播放碰撞音效
            if (collisionAudio != null)
                collisionAudio.Play();

            // 回到抓取過程中的最後合法位置，而不是整段操作的起點。
            rb.position = lastValidPosition;
            rb.rotation = lastValidRotation;
            transform.position = lastValidPosition;
            transform.rotation = lastValidRotation;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 正常放置與回彈後都回到一致的物理狀態，避免家具永久成為 Trigger。
        rb.isKinematic = false;
        rb.useGravity = true;
        col.isTrigger = false;
    }

    // -------------------
    // 偵測與其他家具重疊
    private bool IsOverlappingFurniture()
    {
        Collider[] hits = Physics.OverlapBox(
            transform.position,
            checkBoxSize * 0.5f,
            transform.rotation
        );

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue; // 忽略自己
            if (hit.transform.IsChildOf(transform)) continue; // 忽略子物件

            // 只把有 FurnitureTag 的物件視為家具，避免玩家或其他 Rigidbody 被誤判。
            FurnitureTag otherFurniture = hit.GetComponentInParent<FurnitureTag>();
            if (otherFurniture != null)
                return true;
        }

        return false;
    }

    // -------------------
    // 可視化檢測範圍
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, checkBoxSize);
    }
}
