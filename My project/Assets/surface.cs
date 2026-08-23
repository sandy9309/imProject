using UnityEngine;

public class SurfaceSnapper : MonoBehaviour
{
    public LayerMask surfaceLayer; 
    public float offsetFromWall = 0.05f; 
    public float snapDistance = 0.3f;
    public bool snapEnabled = true;

    private Rigidbody rb;
    private FurnitureInteractionStateController stateController;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        stateController = GetComponent<FurnitureInteractionStateController>();
    }

    void LateUpdate()
    {
        if (snapEnabled) SnapLogic();
    }

    private void SnapLogic()
    {
        if (stateController == null)
            stateController = GetComponent<FurnitureInteractionStateController>();

        if (stateController != null &&
            stateController.CurrentState != FurnitureInteractionState.Grabbed &&
            stateController.CurrentState != FurnitureInteractionState.Validating)
        {
            return;
        }

        if (Physics.Raycast(
            transform.position,
            -transform.forward,
            out RaycastHit hit,
            snapDistance,
            surfaceLayer,
            QueryTriggerInteraction.Ignore))
        {
            Vector3 snappedPosition = hit.point + hit.normal * offsetFromWall;
            Quaternion snappedRotation = Quaternion.LookRotation(hit.normal, Vector3.up);

            if (rb != null && !rb.isKinematic)
            {
                rb.MovePosition(snappedPosition);
                rb.MoveRotation(snappedRotation);
            }
            else
            {
                transform.SetPositionAndRotation(snappedPosition, snappedRotation);
            }
        }
    }

    void FixedUpdate()
    {
        if (rb != null) rb.velocity = Vector3.ClampMagnitude(rb.velocity, 5f);
    }
}

