using UnityEngine;

public class RigSync : MonoBehaviour
{
    [Tooltip("請把 [BuildingBlock] Camera Rig 拖曳到這裡")]
    public Transform targetCameraRig;

    void LateUpdate()
    {
        if (targetCameraRig != null)
        {
            // 強制讓這個物件的位置和旋轉，永遠跟 targetCameraRig 一模一樣
            transform.position = targetCameraRig.position;
            transform.rotation = targetCameraRig.rotation;
        }
    }
}

