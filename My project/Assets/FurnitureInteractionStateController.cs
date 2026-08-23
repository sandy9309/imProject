using UnityEngine;

public enum FurnitureInteractionState
{
    Loading,
    Placed,
    Grabbed,
    Validating,
    Frozen
}

[RequireComponent(typeof(Rigidbody))]
public class FurnitureInteractionStateController : MonoBehaviour
{
    public FurnitureInteractionState CurrentState { get; private set; } = FurnitureInteractionState.Loading;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ApplyState(CurrentState);
    }

    public void SetState(FurnitureInteractionState nextState)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        CurrentState = nextState;
        ApplyState(nextState);
    }

    private void ApplyState(FurnitureInteractionState state)
    {
        if (rb == null) return;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        switch (state)
        {
            case FurnitureInteractionState.Loading:
            case FurnitureInteractionState.Validating:
                rb.constraints = RigidbodyConstraints.None;
                rb.useGravity = false;
                rb.isKinematic = true;
                break;

            case FurnitureInteractionState.Grabbed:
                rb.constraints = RigidbodyConstraints.None;
                rb.useGravity = false;
                rb.isKinematic = true;
                break;

            case FurnitureInteractionState.Placed:
                rb.constraints = RigidbodyConstraints.None;
                rb.isKinematic = false;
                rb.useGravity = true;
                break;

            case FurnitureInteractionState.Frozen:
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                break;
        }
    }
}
