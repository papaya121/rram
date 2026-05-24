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

        private CardInstance card;

        private void Awake()
        {
            card = GetComponent<CardInstance>();
        }

        private void OnMouseEnter()
        {
            card?.SetHovered(true);
        }

        private void OnMouseExit()
        {
            card?.SetHovered(false);
        }

        private void OnMouseDown()
        {
            if (IsPointerBlockedByHud())
            {
                return;
            }

            OpenSelection();
        }

        private void OnMouseOver()
        {
            if (IsPointerBlockedByHud())
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                OpenSelection();
            }
            else if (mouse.rightButton.wasPressedThisFrame)
            {
                OpenSelection();
            }
        }

        private void OpenSelection()
        {
            if (card == null)
            {
                return;
            }

            CardSelectionPanel.EnsureInitialized().Show(card);
        }

        private bool IsPointerBlockedByHud()
        {
            if (EventSystem.current == null || Mouse.current == null)
            {
                return false;
            }

            PointerEventData pointerData = new(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            UiRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, UiRaycastResults);
            for (int i = 0; i < UiRaycastResults.Count; i++)
            {
                GameObject hitObject = UiRaycastResults[i].gameObject;
                if (hitObject == null)
                {
                    continue;
                }

                Transform hitTransform = hitObject.transform;
                if (hitTransform.IsChildOf(transform) || transform.IsChildOf(hitTransform))
                {
                    continue;
                }

                UiRaycastResults.Clear();
                return true;
            }

            UiRaycastResults.Clear();
            return false;
        }
    }
}
