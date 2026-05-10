using UnityEngine;
using UnityEngine.EventSystems;
using RRaM.Core.Networking;

namespace RRaM.Core.Cards
{
    [RequireComponent(typeof(Collider))]
    public sealed class DeckInteraction : MonoBehaviour
    {
        private void OnMouseDown()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            LocalPlayerController.Instance?.DrawCard();
        }
    }
}
