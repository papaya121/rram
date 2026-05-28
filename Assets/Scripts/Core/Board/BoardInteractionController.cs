using System.Collections.Generic;
using System.Linq;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace RRaM.Core.Board
{

    [RequireComponent(typeof(BoardGraph))]
    public sealed class BoardInteractionController : MonoBehaviour
    {
        private const float OverlayAlpha = 0.85f;
        private const float HudSafeWidth = 360f;

        [SerializeField] private Transform interactionRoot;
        [SerializeField] private Material highlightBaseMaterial;

        private readonly Dictionary<string, BoardNodeHoverTarget> targetsByNodeId = new();
        private readonly List<Material> runtimeMaterials = new();

        private BoardGraph boardGraph;
        private Camera cachedCamera;
        private BoardNodeHoverTarget hoveredTarget;
        private Material validHighlightMaterial;
        private Material extendedHighlightMaterial;
        private Material invalidHighlightMaterial;
        private string lastHoverDebugState;
        private string lastMovementDebugState;
        private float nextMissingTargetLogTime;

        private void Awake()
        {
            boardGraph = GetComponent<BoardGraph>();
            boardGraph.EnsureInitialized();
            EnsureTargets();
        }

        private void Update()
        {
            EnsureTargets();
            UpdateHoverState();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    DestroyObjectSafe(runtimeMaterials[i]);
                }
            }

            runtimeMaterials.Clear();
        }

        private void EnsureTargets()
        {
            boardGraph ??= GetComponent<BoardGraph>();
            highlightBaseMaterial ??= ResolveHighlightBaseMaterial();

            if (interactionRoot == null)
            {
                Transform existingRoot = transform.Find("Board Interaction");
                interactionRoot = existingRoot != null ? existingRoot : new GameObject("Board Interaction").transform;
                interactionRoot.SetParent(transform, false);
            }

            if (targetsByNodeId.Count == boardGraph.Nodes.Count)
            {
                return;
            }

            RebuildTargets();
        }

        private void RebuildTargets()
        {
            targetsByNodeId.Clear();

            for (int i = interactionRoot.childCount - 1; i >= 0; i--)
            {
                DestroyObjectSafe(interactionRoot.GetChild(i).gameObject);
            }

            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    DestroyObjectSafe(runtimeMaterials[i]);
                }
            }

            runtimeMaterials.Clear();

            bool isAuthoredLayout = boardGraph.UsesAuthoredAnchors;
            float hitboxRadius = 0.62f;
            Vector3 hitboxCenter = new(0f, 0.18f, 0f);
            if (!isAuthoredLayout)
            {
                validHighlightMaterial = CreateHighlightMaterial(new Color(0f, 1f, 0f, OverlayAlpha));
                extendedHighlightMaterial = CreateHighlightMaterial(new Color(1f, 0.86f, 0f, OverlayAlpha));
                invalidHighlightMaterial = CreateHighlightMaterial(new Color(1f, 0f, 0f, OverlayAlpha));
            }
            else
            {
                validHighlightMaterial = null;
                extendedHighlightMaterial = null;
                invalidHighlightMaterial = null;
            }

            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                BoardNode node = boardGraph.Nodes[i];
                if (isAuthoredLayout && boardGraph.TryGetAnchor(node.NodeId, out BoardNodeAnchor anchor) && anchor != null)
                {
                    Collider authoredCollider = ResolveAuthoredCollider(anchor);
                    BoardNodeHoverTarget authoredTarget = CreateAuthoredHoverTarget(node, anchor, authoredCollider);
                    targetsByNodeId[node.NodeId] = authoredTarget;
                    continue;
                }

                GameObject nodeObject = new($"Hover_{node.NodeId}");
                nodeObject.transform.SetParent(interactionRoot, false);
                nodeObject.transform.localPosition = node.LocalPosition;

                SphereCollider hitbox = nodeObject.AddComponent<SphereCollider>();
                hitbox.radius = hitboxRadius;
                hitbox.center = hitboxCenter;

                GameObject overlay = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Highlight", nodeObject.transform, validHighlightMaterial);
                overlay.transform.localPosition = Vector3.zero;
                overlay.transform.localScale = new Vector3(0.04f, 0.0006486487f, 0.04f);

                Renderer renderer = overlay.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                BoardNodeHoverTarget target = nodeObject.AddComponent<BoardNodeHoverTarget>();
                target.Initialize(node.NodeId, null, renderer, validHighlightMaterial, extendedHighlightMaterial, invalidHighlightMaterial);
                targetsByNodeId[node.NodeId] = target;
            }

            LogDebug($"Rebuilt hover targets. AuthoredLayout={isAuthoredLayout}, Nodes={boardGraph.Nodes.Count}, Targets={targetsByNodeId.Count}");
        }

        private void UpdateHoverState()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                UpdateHoverDebugState("Mouse.current == null");
                ClearHoveredTarget();
                return;
            }

            Vector2 mousePosition = mouse.position.ReadValue();
            if (Screen.width > 0 && mousePosition.x <= HudSafeWidth)
            {
                // UpdateHoverDebugState($"Cursor over HUD. MouseX={mousePosition.x:0.##}, SafeWidth={HudSafeWidth}");
                ClearHoveredTarget();
                return;
            }

            Camera cameraToUse = ResolveCamera();
            if (cameraToUse == null)
            {
                UpdateHoverDebugState("No active camera resolved");
                ClearHoveredTarget();
                return;
            }

            if (CardInteraction.IsPointerOverSelectableCard(mousePosition))
            {
                // UpdateHoverDebugState($"Cursor over card. Mouse={mousePosition}");
                ClearHoveredTarget();
                return;
            }

            Ray ray = cameraToUse.ScreenPointToRay(mousePosition);
            if (!TryResolveHoveredTarget(ray, out BoardNodeHoverTarget target, out string raycastSummary))
            {
                if (mouse.leftButton.wasPressedThisFrame || Time.unscaledTime >= nextMissingTargetLogTime)
                {
                    nextMissingTargetLogTime = Time.unscaledTime + 1f;
                    // UpdateHoverDebugState($"No hover target under cursor. Mouse={mousePosition}, Raycast={raycastSummary}");
                }

                ClearHoveredTarget();
                return;
            }

            if (hoveredTarget != target)
            {
                ClearHoveredTarget();
                hoveredTarget = target;
                UpdateHoverDebugState($"Hover target changed to '{target.NodeId}' via '{target.gameObject.name}'");
            }

            if (!TryGetMovementContext(out CharacterSnapshot selectedCharacter, out MovementBudget movementBudget, out string movementState))
            {
                UpdateMovementDebugState(movementState);
                hoveredTarget.SetHighlightState(CanSelectCharacterAtNode(target.NodeId) ? BoardNodeHighlightState.Valid : BoardNodeHighlightState.Invalid);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    LogDebug($"Click on node '{target.NodeId}' without movement context. Trying character selection.");
                    TrySelectCharacterAtNode(target.NodeId);
                }

                return;
            }

            UpdateMovementDebugState(movementState);
            BoardNodeHighlightState movementStateForTarget = ResolveMovementHighlightState(selectedCharacter.CurrentNodeId, target.NodeId, movementBudget, out int usedSteps);
            bool hasOwnedCharacterAtTarget = TryGetOwnedCharacterAtNode(target.NodeId, out CharacterSnapshot ownedCharacterAtTarget);
            bool occupiedByOtherOwnedCharacter = hasOwnedCharacterAtTarget && ownedCharacterAtTarget.NetId != selectedCharacter.NetId;
            bool canSelectCharacterAtTarget = TryGetSelectableCharacterAtNode(target.NodeId, out CharacterSnapshot selectableCharacterAtTarget);
            bool canMoveToTarget = !occupiedByOtherOwnedCharacter &&
                                   (movementStateForTarget == BoardNodeHighlightState.Valid || movementStateForTarget == BoardNodeHighlightState.Extended);
            hoveredTarget.SetHighlightState(
                canSelectCharacterAtTarget
                    ? BoardNodeHighlightState.Valid
                    : occupiedByOtherOwnedCharacter
                        ? BoardNodeHighlightState.Invalid
                        : movementStateForTarget);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (canSelectCharacterAtTarget)
                {
                    LogDebug($"Click on node '{target.NodeId}' selects character '{selectableCharacterAtTarget.DisplayName}' instead of moving onto an occupied allied node.");
                    LocalPlayerController.Instance?.SelectCharacterAtNode(target.NodeId, selectableCharacterAtTarget.NetId, selectableCharacterAtTarget);
                    return;
                }

                if (occupiedByOtherOwnedCharacter)
                {
                    LogDebug($"Click on node '{target.NodeId}' ignored because it is occupied by allied character '{ownedCharacterAtTarget.DisplayName}' and selection is locked.");
                    return;
                }

                LogDebug($"Click on node '{target.NodeId}'. SelectedCharacter='{selectedCharacter.DisplayName}', From='{selectedCharacter.CurrentNodeId}', Steps={usedSteps}, PrimaryBudget={movementBudget.Primary}, TotalBudget={movementBudget.Total}, CanMove={canMoveToTarget}");
                if (canMoveToTarget)
                {
                    LocalPlayerController.Instance?.MoveSelectedCharacter(target.NodeId);
                }
                else
                {
                    TrySelectCharacterAtNode(target.NodeId);
                }
            }
        }

        private BoardNodeHighlightState ResolveMovementHighlightState(string startNodeId, string destinationNodeId, MovementBudget movementBudget, out int usedSteps)
        {
            usedSteps = 0;
            if (string.IsNullOrWhiteSpace(startNodeId) ||
                string.IsNullOrWhiteSpace(destinationNodeId) ||
                !boardGraph.TryGetShortestPath(startNodeId, destinationNodeId, out List<string> path))
            {
                return BoardNodeHighlightState.Invalid;
            }

            usedSteps = Mathf.Max(0, path.Count - 1);
            if (usedSteps <= 0)
            {
                return BoardNodeHighlightState.Invalid;
            }

            if (usedSteps <= movementBudget.Primary)
            {
                return BoardNodeHighlightState.Valid;
            }

            return usedSteps <= movementBudget.Total
                ? BoardNodeHighlightState.Extended
                : BoardNodeHighlightState.Invalid;
        }

        private BoardNodeHoverTarget CreateAuthoredHoverTarget(BoardNode node, BoardNodeAnchor anchor, Collider authoredCollider)
        {
            GameObject nodeObject = new($"Hover_{node.NodeId}");
            nodeObject.transform.SetParent(interactionRoot, false);
            nodeObject.transform.localPosition = node.LocalPosition;

            BoxCollider hitbox = nodeObject.AddComponent<BoxCollider>();
            hitbox.size = ResolveAuthoredHitboxSize(authoredCollider);
            hitbox.center = Vector3.zero;

            BoardNodeHoverTarget target = nodeObject.AddComponent<BoardNodeHoverTarget>();
            target.Initialize(node.NodeId, anchor, null, null, null, null);
            return target;
        }

        private bool TryGetMovementContext(out CharacterSnapshot selectedCharacter, out MovementBudget movementBudget, out string movementState)
        {
            selectedCharacter = default;
            movementBudget = default;
            movementState = null;

            LocalPlayerController local = LocalPlayerController.Instance;
            TurnManager turnManager = TurnManager.Instance;
            DiceManager diceManager = DiceManager.Instance;
            if (local?.Player == null || turnManager == null || diceManager == null)
            {
                movementState =
                    $"Movement unavailable. LocalPlayer={(local != null)}, Player={(local?.Player != null)}, TurnManager={(turnManager != null)}, DiceManager={(diceManager != null)}";
                return false;
            }

            int localSlot = local.Player.PlayerSlot;
            bool isSetupMove = turnManager.IsSetupPhase;
            bool movementPhaseReady =
                turnManager.CanPlayerMove(localSlot) &&
                diceManager.HasRolledThisTurn(localSlot) &&
                turnManager.GetCurrentPhase(localSlot) == TurnPhase.WaitingForMove &&
                turnManager.GetRemainingMoveBudget(localSlot) > 0;
            if (!movementPhaseReady)
            {
                movementState =
                    $"Movement blocked. LocalSlot={localSlot}, Setup={isSetupMove}, HasRolled={diceManager.HasRolledThisTurn(localSlot)}, CanPlayerMove={turnManager.CanPlayerMove(localSlot)}, RemainingBudget={turnManager.GetRemainingMoveBudget(localSlot)}, RemainingDieActions={turnManager.GetRemainingDieActions(localSlot)}, Phase={turnManager.GetCurrentPhase(localSlot)}";
                return false;
            }

            if (!TryResolveSelectedCharacter(local, out selectedCharacter, out string selectedCharacterSource))
            {
                uint effectiveSelectedCharacterNetId = local.EffectiveSelectedCharacterNetId;
                movementState =
                    $"Movement blocked. Failed to resolve selected character. SelectedCharacterNetId={local.Player.SelectedCharacterNetId}, EffectiveSelectedCharacterNetId={effectiveSelectedCharacterNetId}";
                return false;
            }

            if (selectedCharacter.NetId == 0 || string.IsNullOrWhiteSpace(selectedCharacter.CurrentNodeId))
            {
                movementState =
                    $"Movement blocked. SelectedCharacterNetId={local.Player.SelectedCharacterNetId}, ResolvedCharacterNetId={selectedCharacter.NetId}, CurrentNodeId='{selectedCharacter.CurrentNodeId}'";
                return false;
            }

            if (!turnManager.CanPlayerSelectCharacter(localSlot, selectedCharacter.NetId))
            {
                movementState =
                    $"Movement blocked. Selected character '{selectedCharacter.DisplayName}' is not available for the active action. ActiveCharacterNetId={turnManager.GetActiveCharacterNetId(localSlot)}, SelectedCharacterNetId={selectedCharacter.NetId}, Phase={turnManager.GetCurrentPhase(localSlot)}";
                return false;
            }

            if (!boardGraph.TryGetNode(selectedCharacter.CurrentNodeId, out _))
            {
                movementState =
                    $"Movement blocked. SelectedCharacter node '{selectedCharacter.CurrentNodeId}' is missing from BoardGraph.";
                return false;
            }

            movementBudget = new MovementBudget(
                turnManager.GetPrimaryMoveBudget(localSlot),
                turnManager.GetRemainingMoveBudget(localSlot));
            movementState =
                $"Movement ready. SelectedCharacter='{selectedCharacter.DisplayName}', Node='{selectedCharacter.CurrentNodeId}', Source={selectedCharacterSource}, PrimaryBudget={movementBudget.Primary}, TotalBudget={movementBudget.Total}, CanPlayerMove={turnManager.CanPlayerMove(localSlot)}, Phase={turnManager.GetCurrentPhase(localSlot)}";
            return true;
        }

        private void TrySelectCharacterAtNode(string nodeId)
        {
            LocalPlayerController local = LocalPlayerController.Instance;
            if (local?.Player == null || string.IsNullOrWhiteSpace(nodeId))
            {
                LogDebug($"Selection skipped. LocalPlayer={(local != null)}, Player={(local?.Player != null)}, NodeId='{nodeId}'");
                return;
            }

            if (TryGetSelectableCharacterAtNode(nodeId, out CharacterSnapshot characterAtNode))
            {
                LogDebug($"Selecting character '{characterAtNode.DisplayName}' at node '{nodeId}' via server node lookup");
                local.SelectCharacterAtNode(nodeId, characterAtNode.NetId, characterAtNode);
            }
            else
            {
                LogDebug($"Selecting by node '{nodeId}' via server. Local snapshot did not resolve a different owned character.");
                local.SelectCharacterAtNode(nodeId);
            }
        }

        private bool CanSelectCharacterAtNode(string nodeId)
        {
            return TryGetSelectableCharacterAtNode(nodeId, out _);
        }

        private static bool TryGetOwnedCharacterAtNode(string nodeId, out CharacterSnapshot characterAtNode)
        {
            characterAtNode = default;
            LocalPlayerController local = LocalPlayerController.Instance;
            if (local?.Player == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            characterAtNode = local.Player.Characters.FirstOrDefault(character => character.CurrentNodeId == nodeId);
            return characterAtNode.NetId != 0;
        }

        private bool TryGetSelectableCharacterAtNode(string nodeId, out CharacterSnapshot characterAtNode)
        {
            LocalPlayerController local = LocalPlayerController.Instance;
            characterAtNode = default;
            if (local?.Player == null || !TryGetOwnedCharacterAtNode(nodeId, out characterAtNode))
            {
                return false;
            }

            return local.EffectiveSelectedCharacterNetId != characterAtNode.NetId &&
                   (TurnManager.Instance == null ||
                    TurnManager.Instance.CanPlayerSelectCharacter(local.Player.PlayerSlot, characterAtNode.NetId));
        }

        private static bool TryResolveSelectedCharacter(LocalPlayerController local, out CharacterSnapshot selectedCharacter, out string source)
        {
            selectedCharacter = default;
            source = "none";
            uint selectedCharacterNetId = local != null ? local.EffectiveSelectedCharacterNetId : 0;
            if (local?.Player == null || selectedCharacterNetId == 0)
            {
                return false;
            }

            for (int i = 0; i < local.Player.Characters.Count; i++)
            {
                CharacterSnapshot candidate = local.Player.Characters[i];
                if (candidate.NetId != selectedCharacterNetId)
                {
                    continue;
                }

                selectedCharacter = candidate;
                source = "player_snapshot";
                if (!string.IsNullOrWhiteSpace(selectedCharacter.CurrentNodeId))
                {
                    return true;
                }

                break;
            }

            if (local.PredictedSelectedCharacter.NetId == selectedCharacterNetId &&
                !string.IsNullOrWhiteSpace(local.PredictedSelectedCharacter.CurrentNodeId))
            {
                selectedCharacter = local.PredictedSelectedCharacter;
                source = "predicted_snapshot";
                return true;
            }

            NetworkCharacterPawn[] pawns = FindObjectsByType<NetworkCharacterPawn>();
            for (int i = 0; i < pawns.Length; i++)
            {
                NetworkCharacterPawn pawn = pawns[i];
                if (pawn == null || pawn.netId != selectedCharacterNetId)
                {
                    continue;
                }

                selectedCharacter = new CharacterSnapshot
                {
                    NetId = pawn.netId,
                    OwnerSlot = pawn.OwnerSlot,
                    CharacterType = pawn.CharacterType,
                    DisplayName = pawn.DisplayName,
                    CurrentNodeId = pawn.CurrentNodeId
                };
                source = "scene_pawn";
                return true;
            }

            return selectedCharacter.NetId != 0;
        }

        private static bool TryResolveHoveredTarget(Ray ray, out BoardNodeHoverTarget target, out string debugSummary)
        {
            target = null;
            debugSummary = "No hits";
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));
            List<string> hitSummaries = new(hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                BoardNodeHoverTarget directTarget = collider.GetComponent<BoardNodeHoverTarget>();
                BoardNodeHoverTarget parentTarget = collider.GetComponentInParent<BoardNodeHoverTarget>();
                hitSummaries.Add(
                    $"{i}:{collider.name}@{hits[i].distance:0.###}/direct={(directTarget != null ? directTarget.NodeId : "null")}/parent={(parentTarget != null ? parentTarget.NodeId : "null")}");

                target = directTarget;
                if (target != null)
                {
                    debugSummary = string.Join(" | ", hitSummaries);
                    return true;
                }

                target = parentTarget;
                if (target != null)
                {
                    debugSummary = string.Join(" | ", hitSummaries);
                    return true;
                }
            }

            debugSummary = string.Join(" | ", hitSummaries);
            return false;
        }

        private static Collider ResolveAuthoredCollider(BoardNodeAnchor anchor)
        {
            if (anchor == null)
            {
                return null;
            }

            BoxCollider preferredBoxCollider = anchor.GetComponentInChildren<BoxCollider>(true);
            if (preferredBoxCollider != null)
            {
                return preferredBoxCollider;
            }

            Collider[] colliders = anchor.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    return colliders[i];
                }
            }

            return null;
        }

        private Vector3 ResolveAuthoredHitboxSize(Collider authoredCollider)
        {
            const float minimumHorizontalSize = 0.012f;
            const float maximumHorizontalSize = 0.06f;
            const float verticalSize = 0.001f;

            if (authoredCollider == null)
            {
                return new Vector3(minimumHorizontalSize, verticalSize, minimumHorizontalSize);
            }

            Vector3 localSize = transform.InverseTransformVector(authoredCollider.bounds.size);
            return new Vector3(
                Mathf.Clamp(Mathf.Abs(localSize.x), minimumHorizontalSize, maximumHorizontalSize),
                verticalSize,
                Mathf.Clamp(Mathf.Abs(localSize.z), minimumHorizontalSize, maximumHorizontalSize));
        }

        private Camera ResolveCamera()
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                cachedCamera = Camera.main;
            }

            return cachedCamera;
        }

        private void ClearHoveredTarget()
        {
            if (hoveredTarget == null)
            {
                return;
            }

            hoveredTarget.SetHighlightState(BoardNodeHighlightState.None);
            hoveredTarget = null;
        }

        private Material CreateHighlightMaterial(Color color)
        {
            Material baseMaterial = highlightBaseMaterial != null ? highlightBaseMaterial : ResolveHighlightBaseMaterial();
            Material material = baseMaterial != null ? new Material(baseMaterial) : PrototypeVisualFactory.CreateSurfaceMaterial(color, 0.05f, color * 0.2f);
            if (material == null)
            {
                return null;
            }

            ConfigureMaterialAsTransparent(material, color);
            ConfigureHighlightEmission(material, color);
            runtimeMaterials.Add(material);
            return material;
        }

        private Material ResolveHighlightBaseMaterial()
        {
            if (highlightBaseMaterial != null)
            {
                return highlightBaseMaterial;
            }

            if (PrototypeVisualSettings.Instance != null && PrototypeVisualSettings.Instance.BaseMaterial != null)
            {
                return PrototypeVisualSettings.Instance.BaseMaterial;
            }

            PrototypeVisualSettings[] settings = FindObjectsByType<PrototypeVisualSettings>();
            for (int i = 0; i < settings.Length; i++)
            {
                if (settings[i] != null && settings[i].BaseMaterial != null)
                {
                    return settings[i].BaseMaterial;
                }
            }

            return null;
        }

        private static void ConfigureHighlightEmission(Material material, Color color)
        {
            Color emissionColor = color * 6f;
            material.EnableKeyword("_EMISSION");

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emissionColor);
            }

            if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", emissionColor);
            }
        }

        private static void ConfigureMaterialAsTransparent(Material material, Color color)
        {
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            PrototypeVisualFactory.ApplyColor(material, color);
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

        private void UpdateHoverDebugState(string state)
        {
            if (lastHoverDebugState == state)
            {
                return;
            }

            lastHoverDebugState = state;
            // LogDebug(state);
        }

        private void UpdateMovementDebugState(string state)
        {
            if (lastMovementDebugState == state)
            {
                return;
            }

            lastMovementDebugState = state;
            LogDebug(state);
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[BoardInteractionController] {message}", this);
        }
    }

    public enum BoardNodeHighlightState
    {
        None,
        Valid,
        Extended,
        Invalid
    }

    public readonly struct MovementBudget
    {
        public MovementBudget(int primary, int total)
        {
            Primary = Mathf.Max(0, primary);
            Total = Mathf.Max(0, total);
        }

        public int Primary { get; }
        public int Total { get; }
    }

    public sealed class BoardNodeHoverTarget : MonoBehaviour
    {
        private static readonly Color ValidHighlightColor = new(0f, 1f, 0f, 0.85f);
        private static readonly Color ExtendedHighlightColor = new(1f, 0.86f, 0f, 0.85f);
        private static readonly Color InvalidHighlightColor = new(1f, 0f, 0f, 0.85f);

        private BoardNodeAnchor anchor;
        private Renderer overlayRenderer;
        private Material validMaterial;
        private Material extendedMaterial;
        private Material invalidMaterial;

        public string NodeId { get; private set; }

        public void Initialize(string nodeId, BoardNodeAnchor anchor, Renderer renderer, Material valid, Material extended, Material invalid)
        {
            NodeId = nodeId;
            this.anchor = anchor;
            overlayRenderer = renderer;
            validMaterial = valid;
            extendedMaterial = extended;
            invalidMaterial = invalid;
        }

        public void SetHighlightState(BoardNodeHighlightState state)
        {
            if (anchor != null)
            {
                anchor.SetHighlightState(state != BoardNodeHighlightState.None, ResolveColor(state));
            }

            if (overlayRenderer == null)
            {
                return;
            }

            bool isVisible = state != BoardNodeHighlightState.None;
            overlayRenderer.enabled = isVisible;
            if (!isVisible)
            {
                return;
            }

            overlayRenderer.sharedMaterial = ResolveMaterial(state);
        }

        private Color ResolveColor(BoardNodeHighlightState state)
        {
            return state switch
            {
                BoardNodeHighlightState.Valid => ValidHighlightColor,
                BoardNodeHighlightState.Extended => ExtendedHighlightColor,
                _ => InvalidHighlightColor
            };
        }

        private Material ResolveMaterial(BoardNodeHighlightState state)
        {
            return state switch
            {
                BoardNodeHighlightState.Valid => validMaterial,
                BoardNodeHighlightState.Extended => extendedMaterial,
                _ => invalidMaterial
            };
        }
    }
}
