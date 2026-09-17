using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prevents an OVR hand mesh from remaining frozen at its last valid pose
/// after optical hand tracking is lost.
/// </summary>
public sealed class HandTrackingVisibilityGuard : MonoBehaviour
{
    private OVRHand _hand;
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private bool? _lastVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForSceneHands()
    {
        foreach (OVRHand hand in FindObjectsOfType<OVRHand>(true))
        {
            if (hand.GetComponent<HandTrackingVisibilityGuard>() == null)
                hand.gameObject.AddComponent<HandTrackingVisibilityGuard>();
        }
    }

    private void Awake()
    {
        _hand = GetComponent<OVRHand>();
        CacheRenderers();
    }

    private void LateUpdate()
    {
        if (_hand == null) return;
        if (_renderers.Count == 0) CacheRenderers();

        OVRInput.Controller activeController = OVRInput.GetActiveController();
        bool touchControllerActive =
            (activeController & OVRInput.Controller.Touch) != OVRInput.Controller.None;

        bool visible = !touchControllerActive &&
                       _hand.IsTracked &&
                       _hand.HandConfidence == OVRHand.TrackingConfidence.High;
        if (_lastVisible.HasValue && _lastVisible.Value == visible) return;

        foreach (Renderer handRenderer in _renderers)
        {
            if (handRenderer != null)
                handRenderer.enabled = visible;
        }
        _lastVisible = visible;
    }

    private void CacheRenderers()
    {
        _renderers.Clear();
        GetComponentsInChildren(true, _renderers);
        _lastVisible = null;
    }
}
