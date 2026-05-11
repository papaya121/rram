using System.Collections;
using System.Collections.Generic;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Cards
{
    /// <summary>
    /// Owns server-authoritative deck draws and card usage.
    /// </summary>
    public sealed class CardManager : NetworkBehaviour
    {
        public static CardManager Instance { get; private set; }

        private readonly Dictionary<int, List<CardInstance>> cardsByPlayerSlot = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Cards] Duplicate card manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        [Server]
        public void ServerInitialize(IReadOnlyList<NetworkPlayerConnection> players)
        {
            ServerResetState(players);
            ServerDealStarterCards(players);
        }

        [Server]
        public void ServerResetState(IReadOnlyList<NetworkPlayerConnection> players)
        {
            DestroyAllCards();
            cardsByPlayerSlot.Clear();
            GetDeck()?.ResetRuntimeState();

            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                {
                    players[i].SetCardSnapshots(System.Array.Empty<CardSnapshot>());
                }
            }
        }

        [Server]
        public bool ServerTryDrawCard(NetworkPlayerConnection player)
        {
            if (player == null ||
                TurnManager.Instance == null ||
                !TurnManager.Instance.ServerIsCurrentPlayer(player) ||
                !TurnManager.Instance.CanPlayerSpendDieAction(player.PlayerSlot))
            {
                return false;
            }

            Deck deck = GetDeck();
            if (deck == null || deck.CardPrefab == null)
            {
                Debug.LogWarning("[Cards] Draw rejected. Deck or card prefab is missing.");
                return false;
            }

            if (!deck.HasCards)
            {
                return false;
            }

            if (!TryResolveTurnCharacter(player, out NetworkCharacterPawn actingCharacter))
            {
                Debug.LogWarning("[Cards] Draw rejected. No selected active character for this turn.");
                return false;
            }

            if (!TryResolveDeckNode(actingCharacter, out string deckNodeId))
            {
                Debug.LogWarning($"[Cards] Draw rejected. Character '{actingCharacter.DisplayName}' is not standing on a deck point.");
                return false;
            }

            if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, actingCharacter.netId))
            {
                Debug.LogWarning($"[Cards] Draw rejected. Turn is locked to another character. Slot={player.PlayerSlot}, CharacterNetId={actingCharacter.netId}, ActiveCharacterNetId={TurnManager.Instance.ActiveCharacterNetId}");
                return false;
            }

            player.SetSelectedCharacter(actingCharacter.netId);

            if (!TurnManager.Instance.ServerTryConsumeDeckDrawAction(player.PlayerSlot, deckNodeId))
            {
                return false;
            }

            if (!deck.TryDraw(out BaseCard cardData))
            {
                Debug.LogWarning("[Cards] Draw aborted. Deck was exhausted before the card could be created.");
                return false;
            }

            CardContext drawContext = BuildContext(player, actingCharacter, null);
            int handSlotIndex = Mathf.Clamp(cardData.ResolveHandSlotIndex(drawContext), 0, HandSlotCount - 1);
            ServerSpawnOwnedCard(player, cardData, actingCharacter, handSlotIndex);
            SyncPlayerCards(player);
            return true;
        }

        [Server]
        public bool ServerTryUseOwnedCard(NetworkPlayerConnection player, uint cardNetId)
        {
            return TryGetServerCard(cardNetId, out CardInstance cardInstance) &&
                   ServerTryUseCard(player, cardInstance);
        }

        [Server]
        public bool ServerTryDiscardOwnedCard(NetworkPlayerConnection player, uint cardNetId)
        {
            return TryGetServerCard(cardNetId, out CardInstance cardInstance) &&
                   ServerTryDiscardCard(player, cardInstance);
        }

        [Server]
        public bool ServerTryTransferOwnedCard(NetworkPlayerConnection player, uint cardNetId, uint targetCharacterNetId)
        {
            return TryGetServerCard(cardNetId, out CardInstance cardInstance) &&
                   ServerTryTransferCard(player, cardInstance, targetCharacterNetId);
        }

        [Server]
        public bool ServerTryUseCard(NetworkConnectionToClient sender, CardInstance cardInstance)
        {
            return TryResolvePlayer(sender, out NetworkPlayerConnection player) &&
                   ServerTryUseCard(player, cardInstance);
        }

        [Server]
        public bool ServerTryDiscardCard(NetworkConnectionToClient sender, CardInstance cardInstance)
        {
            return TryResolvePlayer(sender, out NetworkPlayerConnection player) &&
                   ServerTryDiscardCard(player, cardInstance);
        }

        [Server]
        public void ServerConsumeCard(CardInstance cardInstance)
        {
            if (cardInstance == null || !TryResolveOwner(cardInstance.ownerNetId, out NetworkPlayerConnection owner))
            {
                return;
            }

            if (cardsByPlayerSlot.TryGetValue(owner.PlayerSlot, out List<CardInstance> cards))
            {
                cards.Remove(cardInstance);
            }

            SyncPlayerCards(owner);
            NetworkServer.Destroy(cardInstance.gameObject);
        }

        [Server]
        private bool ServerTryUseCard(NetworkPlayerConnection player, CardInstance cardInstance)
        {
            if (player == null ||
                cardInstance == null ||
                cardInstance.IsPendingConsume ||
                cardInstance.Data == null ||
                TurnManager.Instance == null ||
                !TurnManager.Instance.ServerIsCurrentPlayer(player) ||
                !TurnManager.Instance.CanPlayerSpendDieAction(player.PlayerSlot) ||
                !cardInstance.Data.IsPlayable ||
                cardInstance.ownerNetId != player.netId)
            {
                return false;
            }

            CardContext context = BuildContext(player, cardInstance);
            if (context.character == null || !cardInstance.Data.CanUse(context))
            {
                return false;
            }

            if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, context.character.netId))
            {
                return false;
            }

            player.SetSelectedCharacter(context.character.netId);

            if (!TurnManager.Instance.ServerTryConsumeDieActionWithMinimum(player.PlayerSlot, cardInstance.Data.MinimumDieValue))
            {
                return false;
            }

            cardInstance.Data.Use(context);
            if (cardInstance.Data.isConsumable)
            {
                cardInstance.ServerMarkPendingConsume();
                if (cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
                {
                    cards.Remove(cardInstance);
                }

                SyncPlayerCards(player);
                cardInstance.RpcPlayUseAnimation();
                StartCoroutine(ServerConsumeAfterDelay(cardInstance, 0.3f));
            }
            else
            {
                SyncPlayerCards(player);
            }

            return true;
        }

        [Server]
        private bool ServerTryDiscardCard(NetworkPlayerConnection player, CardInstance cardInstance)
        {
            if (player == null ||
                cardInstance == null ||
                cardInstance.IsPendingConsume ||
                cardInstance.ownerNetId != player.netId)
            {
                return false;
            }

            Deck deck = GetDeck();
            if (deck != null && cardInstance.Data != null)
            {
                deck.ReturnCardAndShuffle(cardInstance.Data);
            }

            cardInstance.ServerMarkPendingConsume();
            ServerConsumeCard(cardInstance);
            return true;
        }

        [Server]
        private bool ServerTryTransferCard(NetworkPlayerConnection player, CardInstance cardInstance, uint targetCharacterNetId)
        {
            if (player == null ||
                cardInstance == null ||
                cardInstance.IsPendingConsume ||
                cardInstance.ownerNetId != player.netId ||
                TurnManager.Instance == null ||
                CharacterManager.Instance == null ||
                !TurnManager.Instance.ServerIsCurrentPlayer(player) ||
                !TurnManager.Instance.CanPlayerTransferCard(player.PlayerSlot))
            {
                return false;
            }

            if (!TryResolveTurnCharacter(player, out NetworkCharacterPawn sourceCharacter) ||
                cardInstance.AssignedCharacterNetId != sourceCharacter.netId)
            {
                return false;
            }

            if (!CharacterManager.Instance.TryGetServerCharacter(targetCharacterNetId, out NetworkCharacterPawn targetCharacter) ||
                targetCharacter.OwnerSlot != player.PlayerSlot ||
                targetCharacter.netId == sourceCharacter.netId)
            {
                return false;
            }

            if (TurnManager.Instance.ActiveCharacterNetId == 0)
            {
                if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, sourceCharacter.netId))
                {
                    return false;
                }
            }
            else if (TurnManager.Instance.ActiveCharacterNetId != sourceCharacter.netId)
            {
                return false;
            }

            if (!TurnManager.Instance.ServerTryConsumeCardTransfer(player.PlayerSlot))
            {
                return false;
            }

            player.SetSelectedCharacter(sourceCharacter.netId);
            int handSlotIndex = Mathf.Clamp((int)targetCharacter.CharacterType, 0, HandSlotCount - 1);
            cardInstance.ServerAssignCharacter(targetCharacter.netId, handSlotIndex);
            SyncPlayerCards(player);
            return true;
        }

        private const int HandSlotCount = 5;

        [Server]
        private void ServerDealStarterCards(IReadOnlyList<NetworkPlayerConnection> players)
        {
            if (players == null || players.Count == 0)
            {
                return;
            }

            Match.MatchContext matchContext = Match.MatchContext.Instance;
            if (matchContext == null || matchContext.Config == null)
            {
                return;
            }

            IReadOnlyList<RRaM.Core.Data.MatchPrototypeConfig.StarterCardDefinition> starterCards = matchContext.Config.StarterCards;
            if (starterCards == null || starterCards.Count == 0)
            {
                return;
            }

            Deck deck = GetDeck();
            if (deck == null || deck.CardPrefab == null)
            {
                Debug.LogWarning("[Cards] Starter cards were skipped because the deck or card prefab is missing.");
                return;
            }

            if (CharacterManager.Instance == null)
            {
                Debug.LogWarning("[Cards] Starter cards were skipped because CharacterManager is missing.");
                return;
            }

            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                NetworkPlayerConnection player = players[playerIndex];
                if (player == null)
                {
                    continue;
                }

                for (int cardIndex = 0; cardIndex < starterCards.Count; cardIndex++)
                {
                    RRaM.Core.Data.MatchPrototypeConfig.StarterCardDefinition starterCard = starterCards[cardIndex];
                    if (starterCard.Card == null)
                    {
                        continue;
                    }

                    if (!CharacterManager.Instance.TryGetServerCharacter(player, starterCard.AssignedCharacter, out NetworkCharacterPawn assignedCharacter))
                    {
                        Debug.LogWarning($"[Cards] Starter card '{starterCard.Card.CardId}' was skipped for player slot {player.PlayerSlot} because the target character '{starterCard.AssignedCharacter}' was not found.");
                        continue;
                    }

                    ServerSpawnOwnedCard(player, starterCard.Card, assignedCharacter, (int)starterCard.AssignedCharacter);
                }

                SyncPlayerCards(player);
            }
        }

        [Server]
        private CardInstance ServerSpawnOwnedCard(
            NetworkPlayerConnection player,
            BaseCard cardData,
            NetworkCharacterPawn assignedCharacter,
            int handSlotIndex)
        {
            Deck deck = GetDeck();
            if (player == null || cardData == null || deck == null || deck.CardPrefab == null)
            {
                return null;
            }

            CardInstance cardInstance = Instantiate(deck.CardPrefab);
            cardInstance.name = $"Card_{cardData.CardId}";
            cardInstance.Initialize(
                cardData,
                player.netId,
                assignedCharacter != null ? assignedCharacter.netId : 0,
                Mathf.Clamp(handSlotIndex, 0, HandSlotCount - 1),
                player.PlayerSlot);
            NetworkServer.Spawn(cardInstance.gameObject);

            if (!cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                cards = new List<CardInstance>();
                cardsByPlayerSlot[player.PlayerSlot] = cards;
            }

            cards.Add(cardInstance);
            return cardInstance;
        }

        [Server]
        private IEnumerator ServerConsumeAfterDelay(CardInstance cardInstance, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            ServerConsumeCard(cardInstance);
        }

        [Server]
        private CardContext BuildContext(NetworkPlayerConnection player, CardInstance cardInstance)
        {
            NetworkCharacterPawn character = ResolveCharacterForCard(cardInstance);
            return BuildContext(player, character, cardInstance);
        }

        [Server]
        private CardContext BuildContext(NetworkPlayerConnection player, NetworkCharacterPawn character, CardInstance cardInstance)
        {
            return new CardContext
            {
                player = player,
                character = character,
                diceSystem = DiceManager.Instance,
                board = BoardGraph.Instance,
                owner = player != null ? player.netIdentity : null,
                turnManager = TurnManager.Instance,
                cardManager = this,
                cardInstance = cardInstance
            };
        }

        [Server]
        private bool TryResolveTurnCharacter(NetworkPlayerConnection player, out NetworkCharacterPawn character)
        {
            character = null;
            if (player == null || TurnManager.Instance == null || CharacterManager.Instance == null)
            {
                return false;
            }

            uint characterNetId = TurnManager.Instance.ActiveCharacterNetId != 0
                ? TurnManager.Instance.ActiveCharacterNetId
                : player.SelectedCharacterNetId;
            if (!CharacterManager.Instance.TryGetServerCharacter(characterNetId, out character))
            {
                return false;
            }

            return character.OwnerSlot == player.PlayerSlot;
        }

        [Server]
        private static bool TryResolveDeckNode(NetworkCharacterPawn character, out string deckNodeId)
        {
            deckNodeId = string.Empty;
            if (character == null ||
                string.IsNullOrWhiteSpace(character.CurrentNodeId) ||
                BoardGraph.Instance == null)
            {
                return false;
            }

            BoardGraph.Instance.EnsureInitialized();
            if (!BoardGraph.Instance.TryGetNode(character.CurrentNodeId, out BoardNode node))
            {
                return false;
            }

            if (node.NodeKind != BoardNodeKind.GreenDeck && node.NodeKind != BoardNodeKind.RedDeck)
            {
                return false;
            }

            deckNodeId = node.NodeId;
            return true;
        }

        [Server]
        private NetworkCharacterPawn ResolveCharacterForCard(CardInstance cardInstance)
        {
            if (cardInstance == null || CharacterManager.Instance == null)
            {
                return null;
            }

            return CharacterManager.Instance.TryGetServerCharacter(cardInstance.AssignedCharacterNetId, out NetworkCharacterPawn assignedPawn)
                ? assignedPawn
                : null;
        }

        [Server]
        private void DestroyAllCards()
        {
            foreach (KeyValuePair<int, List<CardInstance>> pair in cardsByPlayerSlot)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    CardInstance card = pair.Value[i];
                    if (card == null)
                    {
                        continue;
                    }

                    if (card.netIdentity != null && card.netIdentity.isServer)
                    {
                        NetworkServer.Destroy(card.gameObject);
                    }
                    else
                    {
                        Destroy(card.gameObject);
                    }
                }
            }
        }

        [Server]
        private void SyncPlayerCards(NetworkPlayerConnection player)
        {
            if (player == null || !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                player?.SetCardSnapshots(System.Array.Empty<CardSnapshot>());
                return;
            }

            List<CardSnapshot> snapshots = new(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null || card.Data == null)
                {
                    continue;
                }

                snapshots.Add(new CardSnapshot
                {
                    NetId = card.netId,
                    OwnerNetId = card.ownerNetId,
                    AssignedCharacterNetId = card.AssignedCharacterNetId,
                    CardId = card.Data.CardId,
                    DisplayName = card.Data.DisplayName,
                    IsConsumable = card.Data.isConsumable,
                    IsPlayable = card.Data.IsPlayable,
                    MinimumDieValue = card.Data.MinimumDieValue,
                    HandSlotIndex = card.HandSlotIndex
                });
            }

            player.SetCardSnapshots(snapshots);
        }

        [Server]
        private static bool TryResolvePlayer(NetworkConnectionToClient sender, out NetworkPlayerConnection player)
        {
            player = null;
            return sender?.identity != null && sender.identity.TryGetComponent(out player);
        }

        [Server]
        private static bool TryResolveOwner(uint ownerNetId, out NetworkPlayerConnection player)
        {
            player = null;
            return ownerNetId != 0 &&
                   NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity identity) &&
                   identity.TryGetComponent(out player);
        }

        [Server]
        private static bool TryGetServerCard(uint cardNetId, out CardInstance cardInstance)
        {
            cardInstance = null;
            return cardNetId != 0 &&
                   NetworkServer.spawned.TryGetValue(cardNetId, out NetworkIdentity identity) &&
                   identity.TryGetComponent(out cardInstance);
        }

        private static Deck GetDeck()
        {
            return Deck.Instance != null ? Deck.Instance : FindAnyObjectByType<Deck>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
