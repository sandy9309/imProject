using System.Collections.Generic;
using UnityEngine;

/// <summary>Uses private fade materials only while overlapping scanned furniture.</summary>
[DisallowMultipleComponent]
public sealed class FurnitureOverlapAppearance : MonoBehaviour
{
    private sealed class Entry
    {
        public Renderer renderer;
        public Material[] original;
        public Material[] faded;
    }
    private readonly List<Entry> entries = new List<Entry>();
    private bool overlapping;

    public void Configure(Transform visuals, float opacity)
    {
        ReleaseMaterials();
        Shader shader = Resources.Load<Shader>("FurnitureOverlapFade");
        if (shader == null)
        {
            Debug.LogWarning("[FurniturePlacement] FurnitureOverlapFade shader is missing.");
            return;
        }
        foreach (Renderer renderer in visuals.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Material[] originals = renderer.sharedMaterials;
            Material[] faded = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material original = originals[i];
                if (original == null) continue;
                Material material = new Material(shader) { name = original.name + " (overlap)", hideFlags = HideFlags.DontSave };
                string colorProperty = original.HasProperty("baseColorFactor") ? "baseColorFactor"
                    : original.HasProperty("_BaseColor") ? "_BaseColor" : original.HasProperty("_Color") ? "_Color" : null;
                Color color = colorProperty != null ? original.GetColor(colorProperty) : Color.white;
                color.a *= opacity;
                material.SetColor("_BaseColor", color);
                string textureProperty = original.HasProperty("baseColorTexture") ? "baseColorTexture"
                    : original.HasProperty("_BaseMap") ? "_BaseMap" : original.HasProperty("_MainTex") ? "_MainTex" : null;
                if (textureProperty != null)
                {
                    material.SetTexture("_BaseMap", original.GetTexture(textureProperty));
                    material.SetTextureScale("_BaseMap", original.GetTextureScale(textureProperty));
                    material.SetTextureOffset("_BaseMap", original.GetTextureOffset(textureProperty));
                }
                faded[i] = material;
            }
            entries.Add(new Entry { renderer = renderer, original = originals, faded = faded });
        }
    }

    public void SetOverlapping(bool value)
    {
        if (overlapping == value) return;
        overlapping = value;
        foreach (Entry entry in entries)
            if (entry.renderer != null) entry.renderer.sharedMaterials = value ? entry.faded : entry.original;
    }

    private void OnDisable() => SetOverlapping(false);
    private void OnDestroy() => ReleaseMaterials();
    private void ReleaseMaterials()
    {
        SetOverlapping(false);
        foreach (Entry entry in entries)
        foreach (Material material in entry.faded)
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        entries.Clear();
    }
}
