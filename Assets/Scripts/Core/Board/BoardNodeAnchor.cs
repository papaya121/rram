using System.Collections.Generic;
using System.Linq;
using RRaM.Core.Characters;
using TMPro;
using UnityEngine;

namespace RRaM.Core.Board
{
    public sealed class BoardNodeAnchor : MonoBehaviour
    {
        private static readonly Color SelectedColor = new(1f, 0f, 0f, 1f);

        [SerializeField] private string nodeId;
        [SerializeField] private string displayName;
        [SerializeField] private string shortLabel;
        [SerializeField] private BoardNodeKind nodeKind = BoardNodeKind.Normal;
        [SerializeField] private bool isStarterNode;
        [SerializeField] private CharacterType starterCharacterType;
        [SerializeField] private List<BoardNodeAnchor> neighbours = new();
        [Header("Occupancy Visuals")]
        [SerializeField] private TMP_Text occupancyText;
        [SerializeField] private GameObject occupancyVisual;

        public string NodeId => nodeId;
        public string DisplayName => displayName;
        public string ShortLabel => shortLabel;
        public BoardNodeKind NodeKind => nodeKind;
        public bool IsStarterNode => isStarterNode;
        public CharacterType StarterCharacterType => starterCharacterType;
        public IReadOnlyList<BoardNodeAnchor> Neighbours => neighbours;
        public TMP_Text OccupancyText => ResolveOccupancyText();
        public GameObject OccupancyVisual => ResolveOccupancyVisual();

        private Renderer[] occupancyRenderers;
        private MaterialPropertyBlock occupancyPropertyBlock;
        private bool occupied;
        private bool selected;
        private Color occupancyColor = Color.white;
        private bool highlightActive;
        private Color highlightColor = Color.red;

        private void Awake()
        {
            AutoBindOccupancyReferences();
            CleanupLegacySelectionOutline();
            ApplyBaseVisualState();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                nodeId = gameObject.name;
            }

            AutoBindOccupancyReferences();
            CleanupLegacySelectionOutline();
        }

        [ContextMenu("Use GameObject Name As Id")]
        private void UseGameObjectNameAsId()
        {
            nodeId = gameObject.name;
        }

        public void ConfigureIdentity(string id, string fullName, string compactLabel)
        {
            nodeId = id;
            displayName = fullName;
            shortLabel = compactLabel;
        }

        public void SetNeighbours(IEnumerable<BoardNodeAnchor> anchors)
        {
            neighbours = anchors?
                .Where(anchor => anchor != null && anchor != this)
                .Distinct()
                .ToList() ?? new List<BoardNodeAnchor>();
        }

        public void ApplyOccupancyState(string label, bool occupied, Color color, bool isSelected)
        {
            TMP_Text text = ResolveOccupancyText();
            if (text != null)
            {
                text.text = occupied ? label : string.Empty;
            }

            this.occupied = occupied;
            occupancyColor = occupied ? color : Color.white;
            selected = occupied && isSelected;

            if (!highlightActive)
            {
                ApplyBaseVisualState();
            }
        }

        public void SetHighlightState(bool isVisible, Color color)
        {
            highlightActive = isVisible;
            if (isVisible)
            {
                highlightColor = color;
            }

            if (highlightActive)
            {
                ApplyHighlightVisualState();
                return;
            }

            ApplyBaseVisualState();
        }

        private TMP_Text ResolveOccupancyText()
        {
            if (occupancyText == null)
            {
                AutoBindOccupancyReferences();
            }

            return occupancyText;
        }

        private GameObject ResolveOccupancyVisual()
        {
            if (occupancyVisual == null)
            {
                AutoBindOccupancyReferences();
            }

            return occupancyVisual;
        }

        private Renderer[] ResolveOccupancyRenderers()
        {
            if ((occupancyRenderers == null || occupancyRenderers.Length == 0) && ResolveOccupancyVisual() != null)
            {
                occupancyRenderers = FindOccupancyRenderers(occupancyVisual);
            }

            return occupancyRenderers;
        }

        private void AutoBindOccupancyReferences()
        {
            occupancyText ??= GetComponentInChildren<TMP_Text>(true);
            occupancyVisual ??= FindOccupancyVisualObject();
            occupancyRenderers = occupancyVisual != null ? FindOccupancyRenderers(occupancyVisual) : null;
        }

        private GameObject FindOccupancyVisualObject()
        {
            Transform explicitVisual = transform.Find("Visual") ?? transform.Find("Quad");
            if (explicitVisual != null)
            {
                return explicitVisual.gameObject;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (occupancyText != null && renderer.gameObject == occupancyText.gameObject)
                {
                    continue;
                }

                if (renderer.transform == transform)
                {
                    continue;
                }

                return renderer.gameObject;
            }

            return null;
        }

        private void ApplyHighlightVisualState()
        {
            GameObject visual = ResolveOccupancyVisual();
            if (visual == null)
            {
                Debug.LogWarning($"[BoardNodeAnchor] Highlight visual is null for node '{nodeId}'", this);
                return;
            }

            EnsureActive(visual);

            Renderer[] renderers = ResolveOccupancyRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning($"[BoardNodeAnchor] No renderers found for highlight node='{nodeId}' visual='{BuildHierarchyPath(visual.transform)}'", visual);
                return;
            }

            ApplyRendererState(renderers, true, highlightColor);
        }

        private void ApplyBaseVisualState()
        {
            GameObject visual = ResolveOccupancyVisual();
            if (visual == null)
            {
                return;
            }

            bool shouldShow = occupied || selected;
            EnsureActive(visual);

            Renderer[] renderers = ResolveOccupancyRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            Color baseColor = selected ? SelectedColor : occupancyColor;
            ApplyRendererState(renderers, shouldShow, baseColor);
        }

        private void ApplyRendererState(Renderer[] renderers, bool isVisible, Color color)
        {
            occupancyPropertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (isVisible)
                {
                    EnsureActive(renderer.gameObject);
                }

                renderer.enabled = isVisible;
                if (!isVisible)
                {
                    continue;
                }

                renderer.GetPropertyBlock(occupancyPropertyBlock);
                ApplyColorToPropertyBlock(occupancyPropertyBlock, color);
                renderer.SetPropertyBlock(occupancyPropertyBlock);
            }
        }

        private static void EnsureActive(GameObject target)
        {
            if (target != null && !target.activeSelf)
            {
                target.SetActive(true);
            }
        }

        private void CleanupLegacySelectionOutline()
        {
            Transform legacyOutline = transform.Find("Selection Outline");
            if (legacyOutline != null)
            {
                DestroyObjectSafe(legacyOutline.gameObject);
            }
        }

        private Renderer[] FindOccupancyRenderers(GameObject visual)
        {
            if (visual == null)
            {
                return null;
            }

            TMP_Text text = occupancyText != null ? occupancyText : GetComponentInChildren<TMP_Text>(true);
            Renderer textRenderer = text != null ? text.GetComponent<Renderer>() : null;
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            return renderers
                .Where(renderer => renderer != null && renderer != textRenderer)
                .ToArray();
        }

        private static void ApplyColorToPropertyBlock(MaterialPropertyBlock propertyBlock, Color color)
        {
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            propertyBlock.SetColor("_EmissionColor", color * 1.4f);
            propertyBlock.SetColor("_EmissiveColor", color * 1.4f);
        }

        private static string BuildHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return "null";
            }

            System.Text.StringBuilder builder = new(target.name);
            Transform current = target.parent;
            while (current != null)
            {
                builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.ToString();
        }

        private static void DestroyObjectSafe(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
