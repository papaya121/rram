using UnityEngine;
using UnityEngine.Rendering;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Creates runtime-safe materials and small decorative primitives for the prototype scene.
    /// </summary>
    public static class PrototypeVisualFactory
    {
        private static Material baseMaterialOverride;

        /// <summary>
        /// Sets the template material used by all runtime prototype visuals.
        /// </summary>
        public static void Configure(Material baseMaterial)
        {
            baseMaterialOverride = baseMaterial;
        }

        /// <summary>
        /// Creates a material compatible with the active render pipeline.
        /// </summary>
        public static Material CreateSurfaceMaterial(Color color, float smoothness = 0.15f, Color? emission = null)
        {
            Material baseMaterial = GetPrimitiveBaseMaterial();
            Material material = baseMaterial != null ? new Material(baseMaterial) : null;

            if (material == null)
            {
                Debug.LogError("Failed to create runtime surface material.");
                return null;
            }

            ApplyColor(material, color);
            ApplySmoothness(material, smoothness);

            if (emission.HasValue)
            {
                EnableEmission(material, emission.Value);
            }

            return material;
        }

        /// <summary>
        /// Applies the given color to common shader color properties.
        /// </summary>
        public static void ApplyColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_TintColor", color);
        }

        /// <summary>
        /// Creates a primitive with shared setup for board visuals.
        /// </summary>
        public static GameObject CreatePrimitive(PrimitiveType primitiveType, string name, Transform parent, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(primitiveType);
            visual.name = name;
            visual.transform.SetParent(parent, false);

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectImmediateSafe(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return visual;
        }

        /// <summary>
        /// Adds a floating label above a world element.
        /// </summary>
        public static TextMesh CreateLabel(string text, Transform parent, Vector3 localPosition, Color color, int fontSize = 32)
        {
            GameObject labelObject = new("Label");
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation = Quaternion.identity;
            labelObject.AddComponent<BillboardLabel>();

            TextMesh textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.fontSize = fontSize;
            textMesh.characterSize = 0.16f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;
            textMesh.fontStyle = FontStyle.Bold;
            return textMesh;
        }

        private static void ApplySmoothness(Material material, float smoothness)
        {
            SetFloatIfPresent(material, "_Smoothness", smoothness);
            SetFloatIfPresent(material, "_Glossiness", smoothness);
        }

        private static void EnableEmission(Material material, Color emission)
        {
            material.EnableKeyword("_EMISSION");
            SetColorIfPresent(material, "_EmissionColor", emission);
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static Material GetPrimitiveBaseMaterial()
        {
            if (baseMaterialOverride != null)
            {
                return baseMaterialOverride;
            }

            if (PrototypeVisualSettings.Instance != null && PrototypeVisualSettings.Instance.BaseMaterial != null)
            {
                return PrototypeVisualSettings.Instance.BaseMaterial;
            }

            GameObject tempObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Renderer renderer = tempObject.GetComponent<Renderer>();
                return renderer != null ? renderer.sharedMaterial : null;
            }
            finally
            {
                DestroyObjectImmediateSafe(tempObject);
            }
        }

        private static void DestroyObjectImmediateSafe(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
