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

    // 記錄放下前位置（放不下時回彈）
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    // -------------------
    // 抓取開始
    public void OnGrab()
    {
        isGrabbed = true;
        originalPosition = transform.position;
        originalRotation = transform.rotation;

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

            // 回彈到原本位置
            transform.position = originalPosition;
            transform.rotation = originalRotation;

            // 保持抓取狀態（避免穿透後立即物理落下）
            rb.isKinematic = true;
            col.isTrigger = true;
        }
        else
        {
            // 可以放下 → 開啟物理
            rb.isKinematic = false;
            rb.useGravity = true;

            col.isTrigger = false; // 物理阻擋生效
        }
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

            // 判斷是否是其他家具（這裡用有 Rigidbody 判斷）
            if (hit.attachedRigidbody != null)
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