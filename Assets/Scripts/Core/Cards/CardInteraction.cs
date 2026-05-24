using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RRaM.Core.Cards
{
    [RequireComponent(typeof(CardInstance))]
    public sealed class CardInteraction : MonoBehaviour
    {
        private static readonly List<RaycastResult> UiRaycastResults = new();
        private static CardInstance hoveredCard;
        private static int lastHoverFrame = -1;
        private static int lastClickFrame = -1;

        private CardInstance card;

        private void Awake()
        {
            card = GetComponent<CardInstance>();
        }

        private void OnDisable()
        {
            if (hoveredCard == card)
            {
                hoveredCard.SetHovered(false);
                hoveredCard = null;
            }
        }

        private void Update()
        {
            ProcessPointerHover();
            ProcessPointerClick();
        }

        private void OnMouseEnter()
        {
            ProcessPointerHover();
        }

        private void OnMouseExit()
        {
            ProcessPointerHover();
        }

        private void OnMouseDown()
        {
            ProcessPointerClick();
        }

        private void OnMouseOver()
        {
            ProcessPointerHover();
            ProcessPointerClick();
        }

        public static bool IsPointerOverSelectableCard(Vector2 pointerPosition)
        {
            return ResolvePointerCard(pointerPosition, out bool isBlockedByHud) != null && !isBlockedByHud;
        }

        private static void ProcessPointerHover()
        {
            if (lastHoverFrame == Time.frameCount)
            {
                return;
            }

            lastHoverFrame = Time.frameCount;
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                SetHoveredCard(null);
                return;
            }

            CardInstance target = ResolvePointerCard(mouse.position.ReadValue(), out bool isBlockedByHud);
            SetHoveredCard(isBlockedByHud ? null : target);
        }

        private static void ProcessPointerClick()
        {
            if (lastClickFrame == Time.frameCount)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null ||
                (!mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.wasPressedThisFrame))
            {
                return;
            }

            lastClickFrame = Time.frameCount;
            CardInstance target = ResolvePointerCard(mouse.position.ReadValue(), out bool isBlockedByHud);
            if (isBlockedByHud || target == null)
            {
                return;
            }

            CardSelectionPanel.EnsureInitialized().Show(target);
        }

        private static void SetHoveredCard(CardInstance target)
        {
            if (hoveredCard == target)
            {
                return;
            }

            hoveredCard?.SetHovered(false);
            hoveredCard = target;
            hoveredCard?.SetHovered(true);
        }

        private static CardInstance ResolvePointerCard(Vector2 pointerPosition, out bool isBlockedByHud)
        {
            isBlockedByHud = false;
            CardInstance uiCard = ResolveUiPointerCard(pointerPosition, out isBlockedByHud);
            return uiCard != null || isBlockedByHud
                ? uiCard
                : ResolvePhysicsPointerCard(pointerPosition);
        }

        private static CardInstance ResolveUiPointerCard(Vector2 pointerPosition, out bool isBlockedByHud)
        {
            isBlockedByHud = false;
            if (EventSystem.current == null)
            {
                return null;
            }

            PointerEventData pointerData = new(EventSystem.current)
            {
                position = pointerPosition
            };

            UiRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, UiRaycastResults);
            CardInstance bestCard = null;
            float bestScreenDistance = float.PositiveInfinity;
            bool sawCardUi = false;
            for (int i = 0; i < UiRaycastResults.Count; i++)
            {
                GameObject hitObject = UiRaycastResults[i].gameObject;
                if (hitObject == null)
                {
                    continue;
                }

                CardInstance hitCard = hitObject.GetComponentInParent<CardInstance>();
                if (hitCard != null)
                {
                    sawCardUi = true;
                    if (hitCard.CanSelectFromLocalClient())
                    {
                        Camera eventCamera = UiRaycastResults[i].module != null
                            ? UiRaycastResults[i].module.eventCamera
                            : Camera.main;
                        Vector2 screenPosition = eventCamera != null
                            ? eventCamera.WorldToScreenPoint(hitCard.transform.position)
                            : (Vector2)hitCard.transform.position;
                        float screenDistance = (screenPosition - pointerPosition).sqrMagnitude;
                        if (bestCard == null || screenDistance < bestScreenDistance)
                        {
                            bestCard = hitCard;
                            bestScreenDistance = screenDistance;
                        }
                    }

                    continue;
                }

                if (sawCardUi)
                {
                    continue;
                }

                isBlockedByHud = true;
                UiRaycastResults.Clear();
                return null;
            }

            UiRaycastResults.Clear();
            return bestCard;
        }

        private static CardInstance ResolvePhysicsPointerCard(Vector2 pointerPosition)
        {
            Camera cameraToUse = Camera.main;
            if (cameraToUse == null)
            {
                return null;
            }

            Ray ray = cameraToUse.ScreenPointToRay(pointerPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            CardInstance bestCard = null;
            float bestScreenDistance = float.PositiveInfinity;
            float bestRayDistance = float.PositiveInfinity;
            int bestSiblingIndex = int.MinValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                CardInstance hitCard = hitCollider.GetComponentInParent<CardInstance>();
                if (hitCard == null || !hitCard.CanSelectFromLocalClient())
                {
                    continue;
                }

                Vector3 screenPosition = cameraToUse.WorldToScreenPoint(hitCard.transform.position);
                float screenDistance = ((Vector2)screenPosition - pointerPosition).sqrMagnitude;
                float rayDistance = hits[i].distance;
                int siblingIndex = hitCard.transform.GetSiblingIndex();
                bool isBetter =
                    bestCard == null ||
                    screenDistance < bestScreenDistance - 0.01f ||
                    (Mathf.Abs(screenDistance - bestScreenDistance) <= 0.01f &&
                     (rayDistance < bestRayDistance - 0.001f ||
                      (Mathf.Abs(rayDistance - bestRayDistance) <= 0.001f && siblingIndex > bestSiblingIndex)));

                if (!isBetter)
                {
                    continue;
                }

                bestCard = hitCard;
                bestScreenDistance = screenDistance;
                bestRayDistance = rayDistance;
                bestSiblingIndex = siblingIndex;
            }

            return bestCard;
        }
    }
}
