using UnityEngine;
using Oculus.Interaction;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Owns furniture input, grab/release lifecycle and the single validated pose commit.</summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class FurniturePlacementController : MonoBehaviour
{
    [Header("Input")]
    [Min(0f)] public float pushPullSpeed = 1f;
    [Min(0f)] public float rotationSpeed = 90f;
    [Min(0f)] public float settleSeconds = 1f;
    private FurnitureWallCollisionGuard validator;
    private FurnitureInteractionStateController state;
    private Transform visual;
    private Rigidbody body;
    private Transform grabTarget;
    private Grabbable[] metaGrabs;
    private XRGrabInteractable xrGrab;
    private Transform xrHand;
    private Vector3 xrOffset;
    private Quaternion xrRotationOffset;
    private Vector3 visualRootOffset;
    private Quaternion visualRootRotation;
    private bool externalGrabbed, wasGrabbed, hasRequest;
    private Pose requestedPose;
    private Vector3 grabOffset;
    private float grabYaw, pendingYaw, settleUntil;

    public bool IsReady => validator != null && visual != null;
    public Transform GrabInputTarget => grabTarget;
    public void RequestPose(Pose pose, bool rotationInProgress = false)
    {
        requestedPose = pose;
        hasRequest = true;
    }
    public void RequestGrabbed(bool grabbed) => externalGrabbed = grabbed;
    public void RequestRotation(float degrees) => pendingYaw += degrees;
    public void RequestSettle()
    {
        settleUntil = Time.time + settleSeconds;
        if (state != null) state.SetState(FurnitureInteractionState.Placed);
    }

    public void Configure(Transform model, FurnitureWallCollisionGuard constraints, Pose initialPose)
    {
        visual = model;
        validator = constraints;
        body = GetComponent<Rigidbody>();
        state = GetComponent<FurnitureInteractionStateController>();
        if (state == null && body != null) state = gameObject.AddComponent<FurnitureInteractionStateController>();
        visualRootOffset = transform.InverseTransformPoint(visual.position);
        visualRootRotation = Quaternion.Inverse(transform.rotation) * visual.rotation;
        var legacyInput = GetComponent<ObjectJoystickControl>();
        if (legacyInput != null)
        {
            pushPullSpeed = legacyInput.pushPullSpeed;
            rotationSpeed = legacyInput.rotationSpeed;
        }
        CommitPose(initialPose);
        BindGrabInput();
        RequestSettle();
    }

    private void BindGrabInput()
    {
        var input = new GameObject(name + " Grab Input");
        input.hideFlags = HideFlags.DontSave;
        grabTarget = input.transform;
        grabTarget.SetPositionAndRotation(transform.position, transform.rotation);
        grabTarget.localScale = transform.lossyScale;
        Rigidbody inputBody = input.AddComponent<Rigidbody>();
        inputBody.isKinematic = true;
        inputBody.useGravity = false;
        metaGrabs = GetComponentsInChildren<Grabbable>(true);
        foreach (Grabbable grab in metaGrabs)
        {
            // SDK transformers write an unrendered input target, never the furniture.
            grab.InjectOptionalTargetTransform(grabTarget);
            grab.InjectOptionalRigidbody(inputBody);
            grab.InjectOptionalThrowWhenUnselected(false);
            grab.VelocityThrow.SetRigidBody(inputBody);
        }
        xrGrab = GetComponent<XRGrabInteractable>();
        if (xrGrab != null)
        {
            xrGrab.trackPosition = false;
            xrGrab.trackRotation = false;
            xrGrab.trackScale = false;
            xrGrab.throwOnDetach = false;
        }
    }

    public static Vector2 FilterThumbstick(Vector2 stick)
    {
        if (stick.magnitude < 0.2f) return Vector2.zero;
        return Mathf.Abs(stick.y) >= Mathf.Abs(stick.x)
            ? new Vector2(0f, stick.y) : new Vector2(stick.x, 0f);
    }

    private void LateUpdate() => ProcessFrame(Time.deltaTime);

    public void ProcessFrame(float deltaTime)
    {
        if (!IsReady) return;
        bool metaSelected = false;
        if (metaGrabs != null)
            foreach (Grabbable grab in metaGrabs)
                if (grab != null && grab.GrabPoints != null && grab.GrabPoints.Count > 0) metaSelected = true;
        bool selected = externalGrabbed || metaSelected || (xrGrab != null && xrGrab.isSelected);
        if (selected && !wasGrabbed)
        {
            grabOffset = Vector3.zero;
            grabYaw = 0f;
            if (state != null) state.SetState(FurnitureInteractionState.Grabbed);
        }
        // While released, normal floor settling is the physics input to the same validator.
        Pose target = new Pose(visual.position, visual.rotation);
        if (selected || wasGrabbed)
        {
            Pose rootTarget = new Pose(grabTarget.position, grabTarget.rotation);
            if (xrGrab != null && xrGrab.isSelected && !metaSelected)
            {
                Transform hand = xrGrab.interactorsSelecting[0].GetAttachTransform(xrGrab);
                if (hand != xrHand)
                {
                    xrHand = hand;
                    xrOffset = transform.position - hand.position;
                    xrRotationOffset = Quaternion.Inverse(hand.rotation) * transform.rotation;
                }
                rootTarget = new Pose(hand.position + xrOffset, hand.rotation * xrRotationOffset);
                grabTarget.SetPositionAndRotation(rootTarget.position, rootTarget.rotation);
            }
            if (selected && Application.isPlaying)
            {
                Vector2 stick = FilterThumbstick(OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch));
                if (Camera.main != null && Mathf.Abs(stick.y) > 0.05f)
                {
                    Vector3 direction = rootTarget.position - Camera.main.transform.position;
                    direction.y = 0f;
                    grabOffset += direction.normalized * stick.y * pushPullSpeed * deltaTime;
                }
                if (Mathf.Abs(stick.x) > 0.05f) grabYaw += stick.x * rotationSpeed * deltaTime;
            }
            grabYaw += pendingYaw;
            rootTarget.position += grabOffset;
            rootTarget.rotation = Quaternion.Euler(0, rootTarget.rotation.eulerAngles.y + grabYaw, 0);
            target = new Pose(rootTarget.position + rootTarget.rotation * Vector3.Scale(visualRootOffset, transform.lossyScale),
                rootTarget.rotation * visualRootRotation);
        }
        else if (pendingYaw != 0)
            target.rotation = Quaternion.AngleAxis(pendingYaw, Vector3.up) * target.rotation;
        pendingYaw = 0f;
        if (hasRequest) { target = requestedPose; hasRequest = false; }
        // Input angle is never replaced by an automatically selected angle.
        CommitPose(validator.ResolvePose(target));
        if (wasGrabbed && !selected)
        {
            xrHand = null;
            RequestSettle();
            if (Application.isPlaying && ModelLoader.Instance != null) ModelLoader.Instance.TriggerAutoSaveDelay(1000);
        }
        else if (!selected && state != null && state.CurrentState == FurnitureInteractionState.Placed && Time.time >= settleUntil)
            state.SetState(FurnitureInteractionState.Frozen);
        if (!selected) grabTarget.SetPositionAndRotation(transform.position, transform.rotation);
        wasGrabbed = selected;
    }

    private void CommitPose(Pose pose)
    {
        bool corrected = Vector3.Distance(visual.position, pose.position) > 0.0001f || Quaternion.Angle(visual.rotation, pose.rotation) > 0.01f;
        Quaternion rotation = pose.rotation * Quaternion.Inverse(visualRootRotation);
        Vector3 position = pose.position - rotation * Vector3.Scale(visualRootOffset, transform.lossyScale);
        if (body != null) { body.position = position; body.rotation = rotation; }
        transform.SetPositionAndRotation(position, rotation);
        if (corrected && body != null && !body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        validator.AcceptPose(pose);
    }
    private void OnDestroy()
    {
        if (grabTarget == null) return;
        if (Application.isPlaying) Destroy(grabTarget.gameObject);
        else DestroyImmediate(grabTarget.gameObject);
    }
}
