using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RRaM.Core.Cards
{
    [RequireComponent(typeof(CardInstance))]
    public sealed class CardInteraction : MonoBehaviour
    {
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
            if (IsPointerOverHud())
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                card?.TryUseFromLocalClient();
            }
        }

        private void OnMouseOver()
        {
            if (IsPointerOverHud())
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
                card?.TryUseFromLocalClient();
            }
            else if (mouse.rightButton.wasPressedThisFrame)
            {
                card?.TryDiscardFromLocalClient();
            }
        }

        private static bool IsPointerOverHud()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
