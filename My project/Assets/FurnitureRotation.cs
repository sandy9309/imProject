using UnityEngine;

public class FurnitureSnapping : MonoBehaviour
{
    // 提供給 Unity Event 調用的函數
    public void Rotate90Degrees()
    {
        // 取得目前的旋轉角度並增加 90 度
        Vector3 currentRotation = transform.eulerAngles;
        currentRotation.y += 90f;
        
        // 重新賦值，確保 Y 軸精準旋轉
        transform.eulerAngles = currentRotation;
    }
}