using System.Collections;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public sealed class CardInstance : NetworkBehaviour
    {
        [SerializeField] private BaseCard data;
        [SerializeField] private CardView view;
        [SerializeField] private CardAnimator animator;

        [SyncVar(hook = nameof(OnCardIdChanged))] private string cardId;
        [SyncVar(hook = nameof(OnOwnerNetIdChanged))] public uint ownerNetId;
        [SyncVar(hook = nameof(OnAssignedCharacterNetIdChanged))] private uint assignedCharacterNetId;
        [SyncVar(hook = nameof(OnHandSlotIndexChanged))] private int handSlotIndex;
        [SyncVar] private int ownerPlayerSlot = -1;

        private HandController boundHandController;
        private bool hasBoundToHand;
        private bool isBindingToHand;
        private bool isUsePending;
        private bool isPendingConsume;
        private bool isInSelectionPanel;
        private bool hasResolvedPresentation;
        private bool lastRevealState;
        private bool hasRevealState;

        public BaseCard Data => data;
        public CardAnimator Animator => animator;
        public uint AssignedCharacterNetId => assignedCharacterNetId;
        public int HandSlotIndex => handSlotIndex;
        public int OwnerPlayerSlot => ownerPlayerSlot;
        public bool IsPendingConsume => isPendingConsume;
        public bool IsInSelectionPanel => isInSelectionPanel;

        private void Awake()
        {
            view ??= GetComponent<CardView>();
            animator ??= GetComponent<CardAnimator>();
        }

        [Server]
        public void Initialize(BaseCard cardData, uint ownerId)
        {
            Initialize(cardData, ownerId, 0, 0, -1);
        }

        [Server]
        public void Initialize(BaseCard cardData, uint ownerId, uint characterNetId, int slotIndex, int playerSlot)
        {
            data = cardData;
            cardId = cardData != null ? cardData.CardId : string.Empty;
            ownerNetId = ownerId;
            assignedCharacterNetId = characterNetId;
            handSlotIndex = slotIndex;
            ownerPlayerSlot = playerSlot;
        }

        [Server]
        public void ServerAssignCharacter(uint characterNetId, int slotIndex)
        {
            assignedCharacterNetId = characterNetId;
            handSlotIndex = slotIndex;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveCardData();
            RefreshView(force: true);
            TryBindToHand(animate: true);
        }

        public override void OnStopClient()
        {
            CardSelectionPanel.Instance?.NotifyCardRemoved(this);

            if (boundHandController != null)
            {
                boundHandController.RemoveCard(this);
                boundHandController = null;
            }

            hasBoundToHand = false;
            base.OnStopClient();
        }

        private void LateUpdate()
        {
            if (!isClient)
            {
                return;
            }

            if (data == null && !string.IsNullOrWhiteSpace(cardId))
            {
                ResolveCardData();
            }

            if (!hasBoundToHand)
            {
                TryBindToHand(animate: false);
            }

            RefreshView(force: false);
        }

        public void TryUseFromLocalClient()
        {
            if (!CanUseFromLocalClient())
            {
                return;
            }

            CmdUseCard();
        }

        public void TryDiscardFromLocalClient()
        {
            if (!CanDiscardFromLocalClient())
            {
                return;
            }

            isUsePending = true;
            CmdDiscardCard();
        }

        public bool CanSelectFromLocalClient()
        {
            return isClient && !isUsePending && CanLocalPlayerInteract();
        }

        public bool CanDiscardFromLocalClient()
        {
            return CanSelectFromLocalClient();
        }

        public bool CanUseFromLocalClient()
        {
            if (!CanSelectFromLocalClient() || data == null || !data.IsPlayable)
            {
                return false;
            }

            LocalPlayerController local = LocalPlayerController.Instance;
            if (local == null || local.Player == null || TurnManager.Instance == null)
            {
                return false;
            }

            if (!TurnManager.Instance.CanPlayerSpendDieActionWithMinimum(local.Player.PlayerSlot, data.MinimumDieValue) ||
                !TurnManager.Instance.CanPlayerSelectCharacter(local.Player.PlayerSlot, assignedCharacterNetId))
            {
                return false;
            }

            CardContext context = BuildLocalContext(local.Player);
            return context.character != null && data.CanUse(context);
        }

        public void BeginSelectionPresentation()
        {
            isInSelectionPanel = true;
            animator?.CancelRootAnimation();
            animator?.ClearHoverImmediate();
            boundHandController?.RefreshCard(this, animate: true);
        }

        public void EndSelectionPresentation(bool animate)
        {
            isInSelectionPanel = false;
            if (boundHandController != null)
            {
                boundHandController.RefreshCard(this, animate);
                return;
            }

            TryBindToHand(animate);
        }

        public void ApplyHandLayout(Vector3 localPosition, Quaternion localRotation, Vector3 localScale, bool animate)
        {
            if (animator == null)
            {
                transform.localPosition = localPosition;
                transform.localRotation = localRotation;
                transform.localScale = localScale;
                return;
            }

            animator.SetLayout(localPosition, localRotation, localScale, animate);
        }

        public void SetHovered(bool hovered)
        {
            if (animator == null)
            {
                return;
            }

            if (isBindingToHand || isInSelectionPanel)
            {
                animator.ClearHoverImmediate();
                return;
            }

            if (isUsePending || !CanLocalPlayerInteract())
            {
                if (!hovered)
                {
                    animator.ClearHoverImmediate();
                }

                return;
            }

            animator.SetHovered(hovered);
        }

        [Command(requiresAuthority = false)]
        public void CmdUseCard(NetworkConnectionToClient sender = null)
        {
            CardManager.Instance?.ServerTryUseCard(sender, this);
        }

        [Command(requiresAuthority = false)]
        public void CmdDiscardCard(NetworkConnectionToClient sender = null)
        {
            CardManager.Instance?.ServerTryDiscardCard(sender, this);
        }

        [Server]
        public void ServerMarkPendingConsume()
        {
            isPendingConsume = true;
        }

        [ClientRpc]
        public void RpcPlayUseAnimation()
        {
            isUsePending = true;
            if (animator != null)
            {
                StartCoroutine(animator.PlayUseAnimation());
            }
        }

        private bool CanLocalPlayerInteract()
        {
            return NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == ownerNetId;
        }

        private void ResolveCardData()
        {
            if (data != null && data.CardId == cardId)
            {
                return;
            }

            Deck deck = Deck.Instance != null ? Deck.Instance : FindAnyObjectByType<Deck>();
            if (deck != null && deck.TryResolveCard(cardId, out BaseCard resolvedCard))
            {
                data = resolvedCard;
            }
        }

        private void RefreshView(bool force)
        {
            if (view == null)
            {
                return;
            }

            bool revealFace = CanLocalPlayerInteract();
            if (!force && hasRevealState && revealFace == lastRevealState)
            {
                return;
            }

            view.Bind(data, revealFace);
            lastRevealState = revealFace;
            hasRevealState = true;
        }

        private void TryBindToHand(bool animate)
        {
            if (isBindingToHand || isInSelectionPanel)
            {
                return;
            }

            if (!ShouldPresentInLocalHand())
            {
                if (!hasResolvedPresentation)
                {
                    StartCoroutine(PlayRemotePreviewAnimation(animate));
                }

                return;
            }

            HandController handController = HandController.Resolve(activateIfInactive: true);
            if (handController == null)
            {
                return;
            }

            if (!hasBoundToHand && animate && animator != null)
            {
                StartCoroutine(PlaySpawnAnimation(handController));
                return;
            }

            boundHandController = handController;
            boundHandController.AddCard(this, handSlotIndex, animate);
            hasBoundToHand = true;
            hasResolvedPresentation = true;
        }

        private IEnumerator PlaySpawnAnimation(HandController handController)
        {
            isBindingToHand = true;
            animator?.ClearHoverImmediate();

            Deck deck = Deck.Instance != null ? Deck.Instance : FindAnyObjectByType<Deck>();
            Transform startPoint = deck != null ? deck.DrawOrigin : null;
            if (startPoint != null)
            {
                transform.position = startPoint.position;
                transform.rotation = startPoint.rotation;
            }

            if (deck != null && deck.CameraWaypoint != null)
            {
                yield return animator.PlayDrawAnimation(deck.CameraWaypoint);
            }

            boundHandController = handController;
            boundHandController.AddCard(this, handSlotIndex, animate: true);
            hasBoundToHand = true;
            hasResolvedPresentation = true;
            isBindingToHand = false;
            animator?.ClearHoverImmediate();
        }

        private IEnumerator PlayRemotePreviewAnimation(bool animate)
        {
            isBindingToHand = true;
            animator?.ClearHoverImmediate();

            Deck deck = Deck.Instance != null ? Deck.Instance : FindAnyObjectByType<Deck>();
            Transform startPoint = deck != null ? deck.DrawOrigin : null;
            Transform previewPoint = deck != null && deck.CameraWaypoint != null ? deck.CameraWaypoint : startPoint;
            if (startPoint != null)
            {
                transform.position = startPoint.position;
                transform.rotation = startPoint.rotation;
            }

            if (animate && animator != null && previewPoint != null && previewPoint != startPoint)
            {
                yield return animator.PlayDrawAnimation(previewPoint);
            }
            else if (previewPoint != null)
            {
                transform.position = previewPoint.position;
                transform.rotation = previewPoint.rotation;
            }

            hasResolvedPresentation = true;
            isBindingToHand = false;
            animator?.ClearHoverImmediate();
        }

        private bool ShouldPresentInLocalHand()
        {
            return CanLocalPlayerInteract();
        }

        private CardContext BuildLocalContext(NetworkPlayerConnection player)
        {
            return new CardContext
            {
                player = player,
                character = ResolveLocalAssignedCharacter(),
                diceSystem = DiceManager.Instance,
                board = BoardGraph.Instance,
                owner = player != null ? player.netIdentity : null,
                turnManager = TurnManager.Instance,
                cardManager = CardManager.Instance,
                cardInstance = this
            };
        }

        private NetworkCharacterPawn ResolveLocalAssignedCharacter()
        {
            if (assignedCharacterNetId == 0 ||
                !NetworkClient.spawned.TryGetValue(assignedCharacterNetId, out NetworkIdentity identity))
            {
                return null;
            }

            return identity.GetComponent<NetworkCharacterPawn>();
        }

        private void OnCardIdChanged(string _, string __)
        {
            ResolveCardData();
            RefreshView(force: true);
        }

        private void OnOwnerNetIdChanged(uint _, uint __)
        {
            if (boundHandController != null)
            {
                boundHandController.RemoveCard(this);
                boundHandController = null;
            }

            hasBoundToHand = false;
            hasResolvedPresentation = false;
            RefreshView(force: true);
            TryBindToHand(animate: true);
        }

        private void OnHandSlotIndexChanged(int oldValue, int __)
        {
            if (boundHandController != null)
            {
                boundHandController.RemoveCardFromSlot(this, ownerPlayerSlot, oldValue);
                boundHandController = null;
            }

            hasBoundToHand = false;
            hasResolvedPresentation = false;
            TryBindToHand(animate: true);
        }

        private void OnAssignedCharacterNetIdChanged(uint _, uint __)
        {
            hasResolvedPresentation = false;
            RefreshView(force: true);
        }
    }
}
