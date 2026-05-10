using System.Collections.Generic;
using Mirror;
using RRaM.Core.Board;
using UnityEngine;

namespace RRaM.Core.Dwarfs
{
    /// <summary>
    /// Represents a server-driven dwarf moving along a predefined route.
    /// </summary>
    public sealed class NetworkDwarfPawn : NetworkBehaviour
    {
        [SyncVar] public string RouteId;
        [SyncVar] public int RouteIndex;
        [SyncVar] public int StepIndex;
        [SyncVar(hook = nameof(OnNodeChanged))] public string CurrentNodeId;

        [SerializeField] private bool useBoardMarkersOnly = true;

        private readonly List<Material> runtimeMaterials = new();
        [SerializeField] private Transform visualRoot;

        /// <summary>
        /// Initializes the dwarf after a server spawn.
        /// </summary>
        [Server]
        public void ServerInitialize(string routeId, int routeIndex, string nodeId)
        {
            RouteId = routeId;
            RouteIndex = routeIndex;
            StepIndex = 0;
            CurrentNodeId = nodeId;
            ApplyWorldPosition();
        }

        /// <summary>
        /// Moves the dwarf to the next route step on the server.
        /// </summary>
        [Server]
        public void ServerAdvanceTo(string nodeId, int stepIndex)
        {
            StepIndex = stepIndex;
            CurrentNodeId = nodeId;
            ApplyWorldPosition();
        }

        /// <summary>
        /// Applies visuals and board position when the dwarf appears on a client.
        /// </summary>
        public override void OnStartClient()
        {
            EnsureMarkerOnlyMode();
            if (!useBoardMarkersOnly)
            {
                EnsureMarkerOnlyMode();
            if (!useBoardMarkersOnly)
            {
                EnsureVisual();
            }
            }

            ApplyWorldPosition();
        }

        private void Awake()
        {
            EnsureVisual();
        }

        private void OnNodeChanged(string oldNodeId, string newNodeId)
        {
            ApplyWorldPosition();
        }

        private void EnsureVisual()
        {
            if (useBoardMarkersOnly)
            {
                return;
            }

            if (TryBindExistingVisual())
            {
                return;
            }

            visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(transform, false);

            Material armorMaterial = CreateMaterial(new Color(0.36f, 0.38f, 0.42f), 0.55f, 0.02f);
            Material beardMaterial = CreateMaterial(new Color(0.72f, 0.56f, 0.28f), 0.15f, 0.01f);
            Material gemMaterial = CreateMaterial(new Color(0.22f, 0.63f, 0.74f), 0.75f, 0.1f);

            GameObject baseDisk = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Base", visualRoot, armorMaterial);
            baseDisk.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            baseDisk.transform.localScale = new Vector3(0.46f, 0.05f, 0.46f);

            GameObject body = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cube, "Body", visualRoot, armorMaterial);
            body.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            body.transform.localScale = new Vector3(0.56f, 0.56f, 0.56f);

            GameObject helmet = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Helmet", visualRoot, armorMaterial);
            helmet.transform.localPosition = new Vector3(0f, 0.46f, 0f);
            helmet.transform.localScale = new Vector3(0.34f, 0.12f, 0.34f);

            GameObject beard = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Beard", visualRoot, beardMaterial);
            beard.transform.localPosition = new Vector3(0f, -0.02f, 0.16f);
            beard.transform.localScale = new Vector3(0.26f, 0.24f, 0.18f);

            GameObject gem = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Gem", visualRoot, gemMaterial);
            gem.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            gem.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        }


        private void EnsureMarkerOnlyMode()
        {
            if (!useBoardMarkersOnly)
            {
                return;
            }

            if (visualRoot == null)
            {
                Transform existingRoot = transform.Find("Visual");
                if (existingRoot != null)
                {
                    visualRoot = existingRoot;
                }
            }

            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
            }
        }

        [ContextMenu("Rebuild Visuals")]
        private void RebuildVisualsContextMenu()
        {
            RebuildVisuals();
        }

        private void ApplyWorldPosition()
        {
            if (BoardGraph.Instance == null || string.IsNullOrWhiteSpace(CurrentNodeId))
            {
                return;
            }

            transform.position = BoardGraph.Instance.GetWorldPosition(CurrentNodeId) + new Vector3(0f, 0.42f, RouteIndex * 0.5f - 0.25f);
        }

        private Material CreateMaterial(Color color, float smoothness, float emissionStrength)
        {
            Material material = PrototypeVisualFactory.CreateSurfaceMaterial(color, smoothness, color * emissionStrength);
            runtimeMaterials.Add(material);
            return material;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    DestroyObjectImmediateSafe(runtimeMaterials[i]);
                }
            }

            runtimeMaterials.Clear();
        }

        public void RebuildVisuals()
        {
            if (visualRoot != null)
            {
                if (useBoardMarkersOnly)
                {
                    visualRoot.gameObject.SetActive(false);
                    return;
                }

                DestroyObjectImmediateSafe(visualRoot.gameObject);
            }

            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    DestroyObjectImmediateSafe(runtimeMaterials[i]);
                }
            }

            runtimeMaterials.Clear();
            visualRoot = null;
            EnsureVisual();
        }

        private bool TryBindExistingVisual()
        {
            if (visualRoot == null)
            {
                Transform existingRoot = transform.Find("Visual");
                if (existingRoot != null)
                {
                    visualRoot = existingRoot;
                }
            }

            return visualRoot != null && visualRoot.childCount > 0;
        }

        private static void DestroyObjectImmediateSafe(Object target)
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
