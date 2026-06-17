using UnityEngine;

public class KeepUpright : MonoBehaviour
{
    void LateUpdate()
    {
        // 抓取原本的旋轉角度，但強迫把 X 和 Z (前後左右傾斜) 歸零
        Vector3 currentRot = transform.eulerAngles;
        transform.eulerAngles = new Vector3(0, currentRot.y, 0);
    }
}
