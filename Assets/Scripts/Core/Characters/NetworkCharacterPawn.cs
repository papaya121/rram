using System.Collections.Generic;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Characters
{
    /// <summary>
    /// Represents a networked controllable character on the board.
    /// </summary>
    public sealed class NetworkCharacterPawn : NetworkBehaviour
    {
        [SyncVar] public int OwnerSlot;
        [SyncVar] public CharacterType CharacterType;
        [SyncVar] public string SpawnNodeId;
        [SyncVar(hook = nameof(OnNodeChanged))] public string CurrentNodeId;
        [SyncVar] public string DisplayName;

        [SerializeField] private bool useBoardMarkersOnly = true;

        private readonly List<Material> runtimeMaterials = new();
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform selectionHalo;
        [SerializeField] private Renderer selectionRenderer;
        private bool selectionVisualActive;

        /// <summary>
        /// Initializes the pawn after spawning on the server.
        /// </summary>
        [Server]
        public void ServerInitialize(int ownerSlot, CharacterType characterType, string displayName, string startNodeId)
        {
            OwnerSlot = ownerSlot;
            CharacterType = characterType;
            DisplayName = displayName;
            SpawnNodeId = startNodeId;
            CurrentNodeId = startNodeId;
            ApplyWorldPosition();
        }

        /// <summary>
        /// Updates the current node on the server.
        /// </summary>
        [Server]
        public void ServerSetCurrentNode(string nodeId)
        {
            CurrentNodeId = nodeId;
            ApplyWorldPosition();
        }

        public Vector3 SpawnPoint => ResolveNodeWorldPosition(SpawnNodeId);

        [Server]
        public void ServerTeleportToSpawn()
        {
            ServerTeleportToNode(SpawnNodeId);
        }

        [Server]
        public void ServerTeleportToNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            CurrentNodeId = nodeId;
            ApplyWorldPosition();
        }

        /// <summary>
        /// Applies visuals and board position when the pawn appears on a client.
        /// </summary>
        public override void OnStartClient()
        {
            EnsureMarkerOnlyMode();
            if (!useBoardMarkersOnly)
            {
                EnsureVisual();
            }

            ApplyWorldPosition();
        }

        private void Awake()
        {
            EnsureVisual();
        }

        private void Update()
        {
            if (!useBoardMarkersOnly)
            {
                UpdateSelectionVisual();
            }
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

            BuildBase();
            BuildBody();
            BuildAccent();
            BuildSelectionHalo();
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

            selectionHalo = null;
            selectionRenderer = null;
            selectionVisualActive = false;
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

            transform.position = ResolveNodeWorldPosition(CurrentNodeId);
        }

        private Vector3 ResolveNodeWorldPosition(string nodeId)
        {
            if (BoardGraph.Instance == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return transform.position;
            }

            return BoardGraph.Instance.GetWorldPosition(nodeId) + ResolveOffset();
        }

        private Vector3 ResolveOffset()
        {
            float angle = ((int)CharacterType * 72f) * Mathf.Deg2Rad;
            float radius = OwnerSlot == 0 ? 0.78f : 1.38f;
            return new Vector3(Mathf.Cos(angle) * radius, 0.42f, Mathf.Sin(angle) * radius);
        }

        private void BuildBase()
        {
            Material ownerMaterial = CreateMaterial(ResolveOwnerColor(), 0.36f, 0.03f);
            Material accentMaterial = CreateMaterial(ResolveAccentColor(), 0.6f, 0.08f);

            GameObject ring = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Base Ring", visualRoot, ownerMaterial);
            ring.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            ring.transform.localScale = new Vector3(0.54f, 0.045f, 0.54f);

            GameObject gem = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Center Gem", visualRoot, accentMaterial);
            gem.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            gem.transform.localScale = new Vector3(0.22f, 0.03f, 0.22f);
        }

        private void BuildSelectionHalo()
        {
            Material haloMaterial = CreateMaterial(new Color(1f, 0.92f, 0.45f), 0.7f, 0.2f);
            GameObject halo = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Selection Halo", visualRoot, haloMaterial);
            halo.transform.localPosition = new Vector3(0f, -0.235f, 0f);
            halo.transform.localScale = new Vector3(0.72f, 0.018f, 0.72f);

            selectionHalo = halo.transform;
            selectionRenderer = halo.GetComponent<Renderer>();
            if (selectionRenderer != null)
            {
                selectionRenderer.enabled = false;
            }
        }

        private void BuildBody()
        {
            Material ownerMaterial = CreateMaterial(ResolveOwnerColor(), 0.25f, 0.02f);
            GameObject body = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Capsule, "Body", visualRoot, ownerMaterial);
            body.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            body.transform.localScale = CharacterType switch
            {
                CharacterType.Warrior => new Vector3(0.46f, 0.66f, 0.46f),
                CharacterType.Hunter => new Vector3(0.42f, 0.7f, 0.42f),
                CharacterType.Shaman => new Vector3(0.44f, 0.72f, 0.44f),
                _ => new Vector3(0.48f, 0.64f, 0.48f)
            };
        }

        private void BuildAccent()
        {
            Material accentMaterial = CreateMaterial(ResolveAccentColor(), 0.62f, 0.1f);
            switch (CharacterType)
            {
                case CharacterType.Blacksmith:
                    CreateBox("Hammer Head", new Vector3(0.18f, 0.58f, 0f), new Vector3(0.34f, 0.12f, 0.16f), accentMaterial);
                    CreateBox("Hammer Handle", new Vector3(0.04f, 0.4f, 0f), new Vector3(0.08f, 0.34f, 0.08f), accentMaterial);
                    break;
                case CharacterType.BlacksmithAssistant:
                    CreateSphere("Lantern", new Vector3(0f, 0.54f, 0f), new Vector3(0.22f, 0.22f, 0.22f), accentMaterial);
                    CreateBox("Pack", new Vector3(0.18f, 0.08f, -0.18f), new Vector3(0.16f, 0.18f, 0.12f), accentMaterial);
                    break;
                case CharacterType.Warrior:
                    CreateBox("Shield", new Vector3(-0.2f, 0.16f, 0f), new Vector3(0.14f, 0.32f, 0.1f), accentMaterial);
                    CreateBox("Sword", new Vector3(0.22f, 0.44f, 0f), new Vector3(0.08f, 0.4f, 0.08f), accentMaterial);
                    break;
                case CharacterType.Hunter:
                    CreateCylinder("Bow", new Vector3(0.24f, 0.28f, 0f), new Vector3(0.08f, 0.34f, 0.08f), accentMaterial, Quaternion.Euler(0f, 0f, 90f));
                    CreateSphere("Quiver", new Vector3(-0.16f, 0.28f, -0.12f), new Vector3(0.14f, 0.22f, 0.14f), accentMaterial);
                    break;
                case CharacterType.Shaman:
                    CreateCylinder("Staff", new Vector3(0.16f, 0.44f, 0f), new Vector3(0.06f, 0.42f, 0.06f), accentMaterial, Quaternion.identity);
                    CreateSphere("Totem", new Vector3(0.16f, 0.76f, 0f), new Vector3(0.18f, 0.18f, 0.18f), accentMaterial);
                    break;
            }
        }

        private void CreateBox(string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cube, name, visualRoot, material);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }

        private void CreateSphere(string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, name, visualRoot, material);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }

        private void CreateCylinder(string name, Vector3 localPosition, Vector3 localScale, Material material, Quaternion localRotation)
        {
            GameObject part = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, name, visualRoot, material);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
        }

        private Color ResolveOwnerColor()
        {
            return OwnerSlot == 0
                ? new Color(0.23f, 0.53f, 0.92f)
                : new Color(0.9f, 0.46f, 0.24f);
        }

        private Color ResolveAccentColor()
        {
            return CharacterType switch
            {
                CharacterType.Blacksmith => new Color(0.78f, 0.68f, 0.38f),
                CharacterType.BlacksmithAssistant => new Color(0.62f, 0.84f, 0.82f),
                CharacterType.Warrior => new Color(0.83f, 0.26f, 0.24f),
                CharacterType.Hunter => new Color(0.38f, 0.75f, 0.46f),
                CharacterType.Shaman => new Color(0.69f, 0.49f, 0.82f),
                _ => Color.white
            };
        }

        private Material CreateMaterial(Color color, float smoothness, float emissionStrength)
        {
            Material material = PrototypeVisualFactory.CreateSurfaceMaterial(color, smoothness, color * emissionStrength);
            runtimeMaterials.Add(material);
            return material;
        }

        private void UpdateSelectionVisual()
        {
            if (selectionHalo == null || selectionRenderer == null)
            {
                return;
            }

            bool isSelected = IsLocallySelected();
            if (selectionVisualActive != isSelected)
            {
                selectionVisualActive = isSelected;
                selectionRenderer.enabled = isSelected;
            }

            if (!isSelected)
            {
                return;
            }

            float pulse = 0.92f + Mathf.PingPong(Time.time * 0.9f, 0.18f);
            selectionHalo.localScale = new Vector3(0.72f * pulse, 0.018f, 0.72f * pulse);
        }

        private bool IsLocallySelected()
        {
            LocalPlayerController localPlayer = LocalPlayerController.Instance;
            if (localPlayer?.Player == null)
            {
                return false;
            }

            return localPlayer.Player.SelectedCharacterNetId == netId;
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
            selectionHalo = null;
            selectionRenderer = null;
            selectionVisualActive = false;
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

            if (visualRoot == null)
            {
                return false;
            }

            if (selectionHalo == null)
            {
                selectionHalo = visualRoot.Find("Selection Halo");
            }

            if (selectionRenderer == null && selectionHalo != null)
            {
                selectionRenderer = selectionHalo.GetComponent<Renderer>();
            }

            return visualRoot.childCount > 0;
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
