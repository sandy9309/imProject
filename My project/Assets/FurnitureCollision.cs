using UnityEngine;

// Compatibility adapter for existing UnityEvents. Only submits grab/release input.
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class VRFurnitureGrab : MonoBehaviour
{
    [HideInInspector] public bool isGrabbed;
    [HideInInspector] public AudioSource collisionAudio;
    [HideInInspector] public Vector3 checkBoxSize = new Vector3(1.5f, 1f, 1.5f);
    public void OnGrab()
    {
        isGrabbed = true;
        GetComponent<FurniturePlacementController>()?.RequestGrabbed(true);
    }
    public void OnRelease()
    {
        isGrabbed = false;
        GetComponent<FurniturePlacementController>()?.RequestGrabbed(false);
    }
}
