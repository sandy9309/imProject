using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using Box = FurniturePlacementGeometry.Box;

/// <summary>Constrains furniture against scanned walls and other virtual furniture.</summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class FurnitureWallCollisionGuard : MonoBehaviour
{
    [SerializeField, Min(0.001f)] private float wallClearance = 0.01f;
    [SerializeField, Min(0.001f)] private float furnitureClearance = 0.005f;
    [SerializeField, Range(0.1f, 1f)] private float overlapOpacity = 0.35f;
    private static readonly HashSet<FurnitureWallCollisionGuard> instances = new HashSet<FurnitureWallCollisionGuard>();
    private readonly List<Box> blockers = new List<Box>();
    private readonly List<Box> realFurniture = new List<Box>();
    public const string FurnitureLayerName = "Virtual Furniture";
    public const string GeometryLayerName = "Placement Geometry";
    private readonly List<Collider> furnitureParts = new List<Collider>();
    private BoxCollider furnitureCollider;
    private Transform visual;
    private Bounds visualBounds;


    private FurnitureOverlapAppearance appearance;
    private Vector3 safePosition;
    private Quaternion safeRotation;
    private bool hasSafePose;
    private float nextRoomRefresh;
    private MRUKRoom cachedRoom;
    private float nextRecoveryAttempt;

    // Layer overrides exclude raw scan colliders even when they are created later.
    public static bool BlocksFurniturePhysics(Collider other)
    {
        return other != null && !other.isTrigger &&
            (SceneAutoScanner.PlacementFloors.Contains(other) ||
             (other is BoxCollider wall && SceneAutoScanner.PlacementWalls.Contains(wall)) ||
             other.GetComponentInParent<FurnitureWallCollisionGuard>() != null);
    }
    private void FixedUpdate() => RefreshPhysicsContacts();
    public void RefreshPhysicsContacts()
    {
        if (furnitureCollider == null) return;
        int furnitureLayer = LayerMask.NameToLayer(FurnitureLayerName);
        int geometryLayer = LayerMask.NameToLayer(GeometryLayerName);
        if (furnitureLayer < 0 || geometryLayer < 0) return;
        int allowed = (1 << furnitureLayer) | (1 << geometryLayer);
        GetComponentsInChildren<Collider>(true, furnitureParts);
        foreach (Collider part in furnitureParts)
        {
            part.gameObject.layer = furnitureLayer;
            part.includeLayers = allowed;
            part.excludeLayers = ~allowed;
            part.layerOverridePriority = 100;
        }
        foreach (BoxCollider wall in SceneAutoScanner.PlacementWalls)
            if (wall != null) wall.gameObject.layer = geometryLayer;
        foreach (Collider floor in SceneAutoScanner.PlacementFloors)
            if (floor != null) floor.gameObject.layer = geometryLayer;
    }
    public bool Configure(BoxCollider targetCollider, Transform movingVisual)
    {
        furnitureCollider = targetCollider;
        visual = movingVisual != null ? movingVisual : transform;


        // Root-local collider coordinates must be converted to the moving Visuals frame.
        bool first = true;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = targetCollider.center + Vector3.Scale(targetCollider.size * 0.5f, new Vector3(x, y, z));
            Vector3 local = visual.InverseTransformPoint(targetCollider.transform.TransformPoint(corner));
            if (first) { visualBounds = new Bounds(local, Vector3.zero); first = false; }
            else visualBounds.Encapsulate(local);
        }
        foreach (SurfaceSnapper snapper in GetComponentsInChildren<SurfaceSnapper>(true)) snapper.snapEnabled = false;
        instances.Add(this);
        RefreshRoom();
        CollectBlockers();
        if (SceneAutoScanner.ActiveWallColliderCount == 0 && SceneAutoScanner.PlacementWalls.Count == 0)
        {
            Debug.LogWarning("[FurniturePlacement] Room walls are not ready. Complete room setup before placing furniture.");
            instances.Remove(this);
            return false;
        }
        Vector3 candidate = visual.position;
        if (!IsValid(BoxAt(candidate, visual.rotation)) && !FindFreePose(ref candidate, visual.rotation))
        {
            Debug.LogWarning($"[FurniturePlacement] No free space near '{name}'; placement cancelled.");
            instances.Remove(this);
            return false;
        }
        safePosition = candidate;
        safeRotation = visual.rotation;
        hasSafePose = true;

        appearance = GetComponent<FurnitureOverlapAppearance>();
        if (appearance == null) appearance = gameObject.AddComponent<FurnitureOverlapAppearance>();
        appearance.Configure(visual, overlapOpacity);
        var controller = GetComponent<FurniturePlacementController>();
        if (controller == null) controller = gameObject.AddComponent<FurniturePlacementController>();
        controller.Configure(visual, this, new Pose(safePosition, safeRotation));
        RefreshPhysicsContacts();
        UpdateAppearance();
        return true;
    }

    private void OnEnable() { if (visual != null && hasSafePose) instances.Add(this); }
    private void OnDisable()
    {
        instances.Remove(this);
        nextRoomRefresh = 0f;
        if (appearance != null) appearance.SetOverlapping(false);
    }


    public Pose ResolvePose(Pose target, bool previewRotation = false)
    {
        if (visual == null || !hasSafePose) return target;
        RefreshRoom();
        if (SceneAutoScanner.PlacementWalls.Count == 0)
        {
            return new Pose(safePosition, safeRotation);
        }
        CollectBlockers();
        Vector3 requestedPosition = target.position;
        Quaternion requestedRotation = target.rotation;
        Vector3 position = safePosition;
        Quaternion rotation = safeRotation;
        if (!IsValid(BoxAt(position, rotation)))
        {
            bool canRetry = Time.unscaledTime >= nextRecoveryAttempt;
            if (!canRetry || !FindFreePose(ref position, rotation))
            {
                if (canRetry) nextRecoveryAttempt = Time.unscaledTime + 0.5f;
                return new Pose(safePosition, safeRotation);
            }
        }
        // Preserve requested yaw. Make room by moving inward before sweeping
        // translation with the actual requested orientation, never an old preview angle.
        Vector3 rotatedPosition = position;
        if (!FitRotation(ref rotatedPosition, requestedRotation, position, rotation))
            return new Pose(position, rotation);
        position = rotatedPosition;
        rotation = requestedRotation;
        position = MoveWithSliding(position, rotation, requestedPosition - position);
        return new Pose(position, rotation);
    }
    private Vector3 MoveWithSliding(Vector3 position, Quaternion rotation, Vector3 delta)
    {
        for (int pass = 0; pass < 3 && delta.sqrMagnitude > 0.0000001f; pass++)
        {
            Box start = BoxAt(position, rotation);
            float fraction = 1f;
            Box contact = default;
            foreach (Box obstacle in blockers)
                if (FurniturePlacementGeometry.Sweep(start, obstacle, delta, out float hit) && hit < fraction)
                { fraction = hit; contact = obstacle; }
            float advance = fraction < 1f ? Mathf.Max(0f, fraction - 0.0005f / delta.magnitude) : 1f;
            Vector3 next = position + delta * advance;
            if (!IsValid(BoxAt(next, rotation))) break;
            position = next;
            if (fraction >= 1f) break;
            Box touching = start;
            touching.center += delta * fraction;
            Vector3 normal = Vector3.zero;
            float greatestGap = float.NegativeInfinity;
            for (int i = 0; i < 15; i++)
            {
                Vector3 axis = i < 3 ? touching.Axis(i) : i < 6 ? contact.Axis(i - 3)
                    : Vector3.Cross(touching.Axis((i - 6) / 3), contact.Axis((i - 6) % 3));
                if (axis.sqrMagnitude < 0.000001f) continue;
                axis.Normalize();
                float distance = Vector3.Dot(touching.center - contact.center, axis);
                float gap = Mathf.Abs(distance) - touching.Radius(axis) - contact.Radius(axis);
                if (gap > greatestGap) { greatestGap = gap; normal = axis * (distance >= 0 ? 1 : -1); }
            }
            delta *= 1f - advance;
            delta -= normal * Mathf.Min(0f, Vector3.Dot(delta, normal));
        }
        return position;
    }
    private bool FitRotation(ref Vector3 position, Quaternion requested, Vector3 previous, Quaternion previousRotation)
    {
        Box safe = BoxAt(previous, previousRotation);
        for (int pass = 0; pass < 12; pass++)
        {
            if (IsValid(BoxAt(position, requested))) return CorrectionReachable(safe, position - previous);
            bool moved = false;
            foreach (Box obstacle in blockers)
            {
                Box box = BoxAt(position, requested);
                if (!FurniturePlacementGeometry.Overlaps(box, obstacle)) continue;
                float bestDistance = float.PositiveInfinity;
                Vector3 bestMove = Vector3.zero;
                for (int axisIndex = 0; axisIndex < 6; axisIndex++)
                {
                    Vector3 axis = axisIndex < 3 ? safe.Axis(axisIndex) : obstacle.Axis(axisIndex - 3);
                    if (Mathf.Abs(axis.y) > 0.001f) continue;
                    axis.Normalize();
                    float prior = Vector3.Dot(safe.center - obstacle.center, axis);
                    // Stay on the same separating side as the last safe pose.
                    if (Mathf.Abs(prior) < safe.Radius(axis) + obstacle.Radius(axis)) continue;
                    axis *= prior >= 0f ? 1f : -1f;
                    float distance = box.Radius(axis) + obstacle.Radius(axis)
                        - Vector3.Dot(box.center - obstacle.center, axis) + 0.001f;
                    if (distance > 0f && distance < bestDistance)
                    { bestDistance = distance; bestMove = axis * distance; }
                }
                if (float.IsPositiveInfinity(bestDistance)) return false;
                position += bestMove;
                moved = true;
            }
            if (!moved) return false;
        }
        return IsValid(BoxAt(position, requested)) && CorrectionReachable(safe, position - previous);
    }
    private bool CorrectionReachable(Box start, Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.0000001f) return true;
        foreach (Box obstacle in blockers)
            if (FurniturePlacementGeometry.Sweep(start, obstacle, delta, out float hit) && hit < 1f)
                return false;
        return true;
    }

    private Box BoxAt(Vector3 position, Quaternion rotation)
    {
        Vector3 scale = visual.lossyScale;
        Vector3 absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return new Box(position + rotation * Vector3.Scale(visualBounds.center, scale),
            Vector3.Scale(visualBounds.extents, absoluteScale), rotation);
    }
    private void CollectBlockers()
    {
        blockers.Clear();
        foreach (BoxCollider wall in SceneAutoScanner.PlacementWalls)
            if (wall != null && wall.enabled && wall.gameObject.activeInHierarchy)
                blockers.Add(FurniturePlacementGeometry.FromBounds(new Bounds(wall.center, wall.size), wall.transform).Expanded(wallClearance));
        foreach (FurnitureWallCollisionGuard other in instances)
            if (other != this && other != null && other.hasSafePose && other.visual != null && other.isActiveAndEnabled)
                blockers.Add(other.BoxAt(other.safePosition, other.safeRotation).Expanded(furnitureClearance));
    }
    private bool IsValid(Box box)
    {
        foreach (Box obstacle in blockers)
            if (FurniturePlacementGeometry.Overlaps(box, obstacle)) return false;
        if (cachedRoom != null && cachedRoom.FloorAnchors.Count > 0)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = box.center + box.rotation * Vector3.Scale(box.half, new Vector3(x, y, z));
                if (!cachedRoom.IsPositionInRoom(corner, false)) return false;
            }
        }
        return true;
    }
    private bool FindFreePose(ref Vector3 position, Quaternion rotation)
    {
        Vector3 origin = position;
        for (int ring = 1; ring <= 12; ring++)
        for (int step = 0; step < 24; step++)
        {
            float angle = step * Mathf.PI / 12f;
            Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 0.25f);
            if (!IsValid(BoxAt(candidate, rotation))) continue;
            position = candidate;
            return true;
        }
        return false;
    }
    public void AcceptPose(Pose pose)
    {
        if (IsValid(BoxAt(pose.position, pose.rotation)))
        {
            safePosition = pose.position;
            safeRotation = pose.rotation;
            hasSafePose = true;
        }
        UpdateAppearance();
    }
    private void RefreshRoom()
    {
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == cachedRoom && Time.unscaledTime < nextRoomRefresh) return;
        cachedRoom = room;
        nextRoomRefresh = Time.unscaledTime + 0.5f;
        realFurniture.Clear();
        if (room == null) return;
        const MRUKAnchor.SceneLabels physicalFurniture = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH |
            MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.SCREEN |
            MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.OTHER;
        foreach (MRUKAnchor anchor in room.Anchors)
        {
            bool isFurniture = (anchor.Label & physicalFurniture) != 0;
            if (isFurniture && anchor.VolumeBounds.HasValue)
                realFurniture.Add(FurniturePlacementGeometry.FromBounds(anchor.VolumeBounds.Value, anchor.transform));
            else if (isFurniture && anchor.PlaneRect.HasValue)
            {
                Rect plane = anchor.PlaneRect.Value;
                realFurniture.Add(FurniturePlacementGeometry.FromBounds(new Bounds(
                    new Vector3(plane.center.x, plane.center.y, 0), new Vector3(plane.width, plane.height, 0.02f)), anchor.transform));
            }
        }
    }
    private void UpdateAppearance()
    {
        if (appearance == null) return;
        Box box = BoxAt(visual.position, visual.rotation);
        bool overlaps = false;
        foreach (Box real in realFurniture)
            if (FurniturePlacementGeometry.Overlaps(box, real)) { overlaps = true; break; }
        appearance.SetOverlapping(overlaps);
    }
}




