using UnityEngine;

/// <summary>Oriented boxes in world metres. SAT also sweeps translations without tunnelling.</summary>
public static class FurniturePlacementGeometry
{
    public struct Box
    {
        public Vector3 center;
        public Vector3 half;
        public Quaternion rotation;
        public Box(Vector3 center, Vector3 half, Quaternion rotation)
        {
            this.center = center;
            this.half = half;
            this.rotation = rotation;
        }
        public Vector3 Axis(int index) => rotation * (index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward);
        public float Radius(Vector3 axis) => Mathf.Abs(Vector3.Dot(Axis(0), axis)) * half.x
            + Mathf.Abs(Vector3.Dot(Axis(1), axis)) * half.y + Mathf.Abs(Vector3.Dot(Axis(2), axis)) * half.z;
        public Box Expanded(float margin) => new Box(center, half + Vector3.one * margin, rotation);
    }

    public static Box FromBounds(Bounds bounds, Transform pose)
    {
        Vector3 scale = pose.lossyScale;
        return new Box(pose.TransformPoint(bounds.center), Vector3.Scale(bounds.extents,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))), pose.rotation);
    }

    // Returns the first fraction of a translation at which the two boxes touch.
    public static bool Sweep(Box moving, Box obstacle, Vector3 delta, out float fraction)
    {
        float entry = 0f, exit = 1f;
        Vector3 offset = moving.center - obstacle.center;
        for (int i = 0; i < 15; i++)
        {
            Vector3 axis = i < 3 ? moving.Axis(i) : i < 6 ? obstacle.Axis(i - 3)
                : Vector3.Cross(moving.Axis((i - 6) / 3), obstacle.Axis((i - 6) % 3));
            if (axis.sqrMagnitude < 0.000001f) continue;
            axis.Normalize();
            float radius = moving.Radius(axis) + obstacle.Radius(axis);
            float distance = Vector3.Dot(offset, axis);
            float speed = Vector3.Dot(delta, axis);
            if (Mathf.Abs(speed) < 0.000001f)
            {
                if (Mathf.Abs(distance) > radius) { fraction = 1f; return false; }
                continue;
            }
            float a = (-radius - distance) / speed;
            float b = (radius - distance) / speed;
            entry = Mathf.Max(entry, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            if (entry > exit) { fraction = 1f; return false; }
        }
        fraction = entry;
        return true;
    }

    public static bool Overlaps(Box a, Box b) => Sweep(a, b, Vector3.zero, out _);
}
