using System.Collections.Generic;
using RRaM.Core.Board;
using RRaM.Core.Characters;
using RRaM.Core.Match;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.CameraControl
{
    /// <summary>
    /// Smooth camera controller with explicit look targets and simple per-mode offsets.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("RRaM/Camera/Prototype Camera Controller")]
    public sealed class PrototypeCameraController : MonoBehaviour
    {
        private enum CameraPreference : byte
        {
            Characters = 0,
            Map = 1
        }

        [Header("Scene References")]
        [Tooltip("Optional explicit camera reference. If empty, Camera.main is used.")]
        [SerializeField] private Camera targetCamera;

        [Header("Smoothing")]
        [Tooltip("How quickly the camera position moves toward the target.")]
        [Min(0.01f)]
        [SerializeField] private float positionSmoothTime = 0.35f;

        [Tooltip("How quickly the camera rotates toward the target.")]
        [Min(0.01f)]
        [SerializeField] private float rotationSmoothTime = 0.25f;

        [Tooltip("How quickly FOV or orthographic size changes are applied.")]
        [Min(0.01f)]
        [SerializeField] private float zoomSmoothTime = 0.25f;

        [Header("Whole Map")]
        [Tooltip("Point the camera should look at in whole-map mode.")]
        [SerializeField] private Vector3 wholeMapLookPoint = Vector3.zero;

        [Tooltip("World offset from the whole-map look point to the camera position.")]
        [SerializeField] private Vector3 wholeMapOffset = new(0f, 3f, -2.07999992f);

        [Tooltip("Perspective: field of view. Orthographic: orthographic size.")]
        [Min(1f)]
        [SerializeField] private float wholeMapZoom = 45f;

        [Header("All Characters")]
        [Tooltip("World offset from the average point of the visible roster to the camera position.")]
        [SerializeField] private Vector3 rosterOffset = new(0f, 5.5f, -7.5f);

        [Tooltip("Extra world-space margin added around the roster before fitting zoom.")]
        [Min(0f)]
        [SerializeField] private float rosterPadding = 1f;

        [Tooltip("Minimum zoom when fitting a roster. Perspective: minimum FOV. Orthographic: minimum size.")]
        [Min(1f)]
        [SerializeField] private float minRosterZoom = 28f;

        [Tooltip("Maximum zoom when fitting a roster. Perspective: maximum FOV. Orthographic: maximum size.")]
        [Min(1f)]
        [SerializeField] private float maxRosterZoom = 60f;

        [Header("Selected Character")]
        [Tooltip("World offset from the selected character to the camera position.")]
        [SerializeField] private Vector3 selectedCharacterOffset = new(0f, 4f, -5f);

        [Tooltip("Perspective: field of view. Orthographic: orthographic size.")]
        [Min(1f)]
        [SerializeField] private float selectedCharacterZoom = 36f;

        [Header("Debug")]
        [Tooltip("Shows a runtime marker at the point the camera is currently looking at.")]
        [SerializeField] private bool showLookTargetMarker = true;

        [Tooltip("World-space scale of the look target marker.")]
        [Min(0.05f)]
        [SerializeField] private float lookTargetMarkerScale = 0.35f;

        private readonly List<Vector3> framingPoints = new();
        private CameraPreference currentPreference = CameraPreference.Characters;
        private Vector3 positionVelocity;
        private float zoomVelocity;
        private int observedTurnNumber = int.MinValue;
        private bool hadSpawnedCharactersLastFrame;
        private Transform lookTargetMarker;
        private Material lookTargetMarkerMaterial;

        private void LateUpdate()
        {
            Camera cameraToDrive = ResolveCamera();
            if (cameraToDrive == null)
            {
                return;
            }

            UpdateTurnScopedPreference();
            if (!TryResolveView(cameraToDrive, out Vector3 lookPoint, out Vector3 targetPosition, out Quaternion targetRotation, out float targetZoom))
            {
                UpdateLookTargetMarker(false, Vector3.zero);
                return;
            }

            UpdateLookTargetMarker(true, lookPoint);
            ApplyView(cameraToDrive, targetPosition, targetRotation, targetZoom);
        }

        public void FocusCharacters()
        {
            currentPreference = CameraPreference.Characters;
            Debug.Log("[Camera] Focus requested: characters.");
        }

        public void FocusMap()
        {
            currentPreference = CameraPreference.Map;
            Debug.Log("[Camera] Focus requested: map.");
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                targetCamera = Camera.main;
            }

            return targetCamera;
        }

        private void UpdateTurnScopedPreference()
        {
            bool hasSpawnedCharacters = FindObjectsByType<NetworkCharacterPawn>(FindObjectsSortMode.None).Length > 0;
            if (hasSpawnedCharacters && !hadSpawnedCharactersLastFrame)
            {
                currentPreference = CameraPreference.Characters;
            }

            hadSpawnedCharactersLastFrame = hasSpawnedCharacters;

            MatchContext context = MatchContext.Instance;
            TurnManager turnManager = context != null && context.TurnManager != null
                ? context.TurnManager
                : TurnManager.Instance;
            if (turnManager == null)
            {
                return;
            }

            if (observedTurnNumber == turnManager.TurnNumber)
            {
                return;
            }

            observedTurnNumber = turnManager.TurnNumber;
            currentPreference = CameraPreference.Characters;
        }

        private bool TryResolveView(Camera cameraToDrive, out Vector3 lookPoint, out Vector3 targetPosition, out Quaternion targetRotation, out float targetZoom)
        {
            lookPoint = cameraToDrive.transform.position + cameraToDrive.transform.forward * 5f;
            targetPosition = cameraToDrive.transform.position;
            targetRotation = cameraToDrive.transform.rotation;
            targetZoom = cameraToDrive.orthographic ? cameraToDrive.orthographicSize : cameraToDrive.fieldOfView;

            if (ShouldShowWholeMap())
            {
                return TryBuildFixedView(wholeMapLookPoint, wholeMapOffset, wholeMapZoom, cameraToDrive, out lookPoint, out targetPosition, out targetRotation, out targetZoom);
            }

            if (TryGetSelectedFocusCharacter(out NetworkCharacterPawn selectedPawn))
            {
                Vector3 selectedLookPoint = ResolveCharacterLookPoint(selectedPawn);
                return TryBuildFixedView(selectedLookPoint, selectedCharacterOffset, selectedCharacterZoom, cameraToDrive, out lookPoint, out targetPosition, out targetRotation, out targetZoom);
            }

            framingPoints.Clear();
            int focusPlayerSlot = ResolveFocusPlayerSlot();
            if (!TryCollectCharacterPoints(focusPlayerSlot, framingPoints) && !TryCollectCharacterPoints(null, framingPoints))
            {
                return TryBuildFixedView(wholeMapLookPoint, wholeMapOffset, wholeMapZoom, cameraToDrive, out lookPoint, out targetPosition, out targetRotation, out targetZoom);
            }

            Vector3 rosterCenter = CalculateAveragePoint(framingPoints);
            ExpandPointsAroundCenter(framingPoints, rosterCenter, rosterPadding);
            lookPoint = rosterCenter;
            targetPosition = rosterCenter + rosterOffset;
            targetRotation = ResolveLookRotation(rosterCenter, targetPosition, cameraToDrive.transform.rotation);
            targetZoom = CalculateRosterZoom(cameraToDrive, targetPosition, targetRotation, framingPoints);
            return true;
        }

        private bool TryBuildFixedView(
            Vector3 lookPoint,
            Vector3 offset,
            float zoom,
            Camera cameraToDrive,
            out Vector3 resolvedLookPoint,
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out float targetZoom)
        {
            resolvedLookPoint = lookPoint;
            targetPosition = lookPoint + offset;
            targetRotation = ResolveLookRotation(lookPoint, targetPosition, cameraToDrive.transform.rotation);
            targetZoom = zoom;
            return true;
        }

        private bool ShouldShowWholeMap()
        {
            if (currentPreference == CameraPreference.Map)
            {
                return true;
            }

            MatchContext context = MatchContext.Instance;
            MatchManager matchManager = context != null && context.MatchManager != null
                ? context.MatchManager
                : MatchManager.Instance;
            if (matchManager == null)
            {
                return true;
            }

            if (matchManager.State == MatchState.Bootstrapping || matchManager.State == MatchState.Lobby)
            {
                return true;
            }

            return FindObjectsByType<NetworkCharacterPawn>(FindObjectsSortMode.None).Length == 0;
        }

        private bool TryGetSelectedFocusCharacter(out NetworkCharacterPawn pawn)
        {
            pawn = null;
            int focusPlayerSlot = ResolveFocusPlayerSlot();
            if (focusPlayerSlot < 0)
            {
                return false;
            }

            NetworkPlayerConnection[] players = FindObjectsByType<NetworkPlayerConnection>(FindObjectsSortMode.None);
            uint selectedNetId = 0;
            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayerConnection player = players[i];
                if (player == null || player.PlayerSlot != focusPlayerSlot)
                {
                    continue;
                }

                selectedNetId = player.SelectedCharacterNetId;
                break;
            }

            if (selectedNetId == 0)
            {
                return false;
            }

            NetworkCharacterPawn[] pawns = FindObjectsByType<NetworkCharacterPawn>(FindObjectsSortMode.None);
            for (int i = 0; i < pawns.Length; i++)
            {
                NetworkCharacterPawn candidate = pawns[i];
                if (candidate != null && candidate.netId == selectedNetId)
                {
                    pawn = candidate;
                    return true;
                }
            }

            return false;
        }

        private int ResolveFocusPlayerSlot()
        {
            MatchContext context = MatchContext.Instance;
            TurnManager turnManager = context != null && context.TurnManager != null
                ? context.TurnManager
                : TurnManager.Instance;
            if (turnManager != null)
            {
                int activeSlot = turnManager.CurrentPlayerSlot;
                if (activeSlot >= 0)
                {
                    return activeSlot;
                }
            }

            LocalPlayerController localPlayer = LocalPlayerController.Instance;
            if (localPlayer?.Player != null)
            {
                return localPlayer.Player.PlayerSlot;
            }

            return -1;
        }

        private static bool TryCollectCharacterPoints(int? ownerSlot, List<Vector3> points)
        {
            NetworkCharacterPawn[] pawns = FindObjectsByType<NetworkCharacterPawn>(FindObjectsSortMode.None);
            for (int i = 0; i < pawns.Length; i++)
            {
                NetworkCharacterPawn pawn = pawns[i];
                if (pawn == null || !pawn.isActiveAndEnabled || !pawn.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (ownerSlot.HasValue && pawn.OwnerSlot != ownerSlot.Value)
                {
                    continue;
                }

                points.Add(ResolveCharacterLookPoint(pawn));
            }

            return points.Count > 0;
        }

        private static Vector3 CalculateAveragePoint(List<Vector3> points)
        {
            if (points.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                sum += points[i];
            }

            return sum / points.Count;
        }

        private static void ExpandPointsAroundCenter(List<Vector3> points, Vector3 center, float padding)
        {
            if (padding <= 0f)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 direction = points[i] - center;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                points[i] += direction.normalized * padding;
            }
        }

        private float CalculateRosterZoom(Camera cameraToDrive, Vector3 cameraPosition, Quaternion cameraRotation, List<Vector3> points)
        {
            if (cameraToDrive.orthographic)
            {
                float requiredSize = minRosterZoom;
                Quaternion inverseRotation = Quaternion.Inverse(cameraRotation);
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 localPoint = inverseRotation * (points[i] - cameraPosition);
                    requiredSize = Mathf.Max(requiredSize, Mathf.Abs(localPoint.y));
                    requiredSize = Mathf.Max(requiredSize, Mathf.Abs(localPoint.x) / Mathf.Max(0.01f, cameraToDrive.aspect));
                }

                return Mathf.Clamp(requiredSize, minRosterZoom, maxRosterZoom);
            }

            float halfVerticalFov = 0f;
            Quaternion inverse = Quaternion.Inverse(cameraRotation);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 localPoint = inverse * (points[i] - cameraPosition);
                float depth = Mathf.Max(0.01f, localPoint.z);
                float verticalAngle = Mathf.Atan2(Mathf.Abs(localPoint.y), depth) * Mathf.Rad2Deg;
                float horizontalAngle = Mathf.Atan2(Mathf.Abs(localPoint.x), depth) * Mathf.Rad2Deg;
                float horizontalAsVertical = Mathf.Atan(Mathf.Tan(horizontalAngle * Mathf.Deg2Rad) / Mathf.Max(0.01f, cameraToDrive.aspect)) * Mathf.Rad2Deg;
                halfVerticalFov = Mathf.Max(halfVerticalFov, verticalAngle, horizontalAsVertical);
            }

            float targetFov = Mathf.Max(minRosterZoom, halfVerticalFov * 2f);
            return Mathf.Clamp(targetFov, minRosterZoom, maxRosterZoom);
        }

        private void ApplyView(Camera cameraToDrive, Vector3 targetPosition, Quaternion targetRotation, float targetZoom)
        {
            cameraToDrive.transform.position = Vector3.SmoothDamp(
                cameraToDrive.transform.position,
                targetPosition,
                ref positionVelocity,
                positionSmoothTime);

            float rotationT = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, rotationSmoothTime));
            cameraToDrive.transform.rotation = Quaternion.Slerp(cameraToDrive.transform.rotation, targetRotation, rotationT);

            if (cameraToDrive.orthographic)
            {
                cameraToDrive.orthographicSize = Mathf.SmoothDamp(
                    cameraToDrive.orthographicSize,
                    targetZoom,
                    ref zoomVelocity,
                    zoomSmoothTime);
                return;
            }

            cameraToDrive.fieldOfView = Mathf.SmoothDamp(
                cameraToDrive.fieldOfView,
                targetZoom,
                ref zoomVelocity,
                zoomSmoothTime);
        }

        private static Quaternion ResolveLookRotation(Vector3 lookPoint, Vector3 cameraPosition, Quaternion fallbackRotation)
        {
            Vector3 forward = lookPoint - cameraPosition;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return fallbackRotation;
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static Vector3 ResolveCharacterLookPoint(NetworkCharacterPawn pawn)
        {
            if (pawn == null)
            {
                return Vector3.zero;
            }

            BoardGraph boardGraph = MatchContext.Instance != null && MatchContext.Instance.BoardGraph != null
                ? MatchContext.Instance.BoardGraph
                : BoardGraph.Instance;
            if (boardGraph != null && !string.IsNullOrWhiteSpace(pawn.CurrentNodeId))
            {
                return boardGraph.GetWorldPosition(pawn.CurrentNodeId);
            }

            return pawn.transform.position;
        }

        private void UpdateLookTargetMarker(bool shouldShow, Vector3 lookPoint)
        {
            if (!showLookTargetMarker || !shouldShow)
            {
                if (lookTargetMarker != null)
                {
                    lookTargetMarker.gameObject.SetActive(false);
                }

                return;
            }

            EnsureLookTargetMarker();
            if (lookTargetMarker == null)
            {
                return;
            }

            lookTargetMarker.gameObject.SetActive(true);
            lookTargetMarker.position = lookPoint;
            lookTargetMarker.localScale = Vector3.one * lookTargetMarkerScale;
        }

        private void EnsureLookTargetMarker()
        {
            if (lookTargetMarker != null)
            {
                return;
            }

            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "Camera Look Target Marker";
            markerObject.transform.SetParent(transform, false);

            Collider markerCollider = markerObject.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = markerObject.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                Shader markerShader = Shader.Find("Universal Render Pipeline/Lit");
                markerShader ??= Shader.Find("Standard");
                if (markerShader != null)
                {
                    lookTargetMarkerMaterial = new Material(markerShader);
                    lookTargetMarkerMaterial.color = new Color(1f, 0.2f, 0.2f, 1f);
                    if (lookTargetMarkerMaterial.HasProperty("_EmissionColor"))
                    {
                        lookTargetMarkerMaterial.EnableKeyword("_EMISSION");
                        lookTargetMarkerMaterial.SetColor("_EmissionColor", new Color(2.5f, 0.2f, 0.2f));
                    }

                    markerRenderer.sharedMaterial = lookTargetMarkerMaterial;
                }
            }

            lookTargetMarker = markerObject.transform;
        }

        private void OnDestroy()
        {
            if (lookTargetMarkerMaterial != null)
            {
                Destroy(lookTargetMarkerMaterial);
            }
        }
    }
}
