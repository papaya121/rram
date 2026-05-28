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

        private const int HandSlotCount = 5;
        private const int BaseCharacterCardCapacity = 10;
        private const int BagCapacityBonus = 3;
        private const string BagCardId = "BagCard";

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
                Match.MatchManager.Instance?.State == Match.MatchState.Completed ||
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

            if (!TryResolveDeckNode(actingCharacter, out string deckNodeId, out BoardNodeKind deckNodeKind))
            {
                Debug.LogWarning($"[Cards] Draw rejected. Character '{actingCharacter.DisplayName}' is not standing on a deck point.");
                return false;
            }

            if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, actingCharacter.netId))
            {
                Debug.LogWarning($"[Cards] Draw rejected. Turn is locked to another character. Slot={player.PlayerSlot}, CharacterNetId={actingCharacter.netId}, ActiveCharacterNetId={TurnManager.Instance.GetActiveCharacterNetId(player.PlayerSlot)}");
                return false;
            }

            player.SetSelectedCharacter(actingCharacter.netId);

            if (!deck.TryDraw(deckNodeKind, out BaseCard cardData))
            {
                Debug.LogWarning("[Cards] Draw aborted. Deck was exhausted before the card could be created.");
                return false;
            }

            CardContext drawContext = BuildContext(player, actingCharacter, null);
            int handSlotIndex = Mathf.Clamp(cardData.ResolveHandSlotIndex(drawContext), 0, HandSlotCount - 1);
            NetworkCharacterPawn assignedCharacter = ResolveAssignmentCharacter(player, actingCharacter, handSlotIndex);
            if (assignedCharacter == null || !CanAcceptCard(player, assignedCharacter, cardData))
            {
                deck.ReturnCardAndShuffle(cardData);
                return false;
            }

            if (!TurnManager.Instance.ServerTryConsumeDeckDrawAction(player.PlayerSlot, deckNodeId))
            {
                deck.ReturnCardAndShuffle(cardData);
                return false;
            }

            ServerSpawnOwnedCard(player, cardData, assignedCharacter, handSlotIndex);
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
                Match.MatchManager.Instance?.State == Match.MatchState.Completed ||
                !TurnManager.Instance.ServerIsCurrentPlayer(player) ||
                !TurnManager.Instance.CanPlayerSpendDieAction(player.PlayerSlot) ||
                !cardInstance.Data.IsPlayable ||
                cardInstance.RequiresTransferBeforeUse ||
                cardInstance.ownerNetId != player.netId)
            {
                return false;
            }

            CardContext context = BuildContext(player, cardInstance);
            if (context.character == null ||
                context.character.OwnerSlot != player.PlayerSlot ||
                context.character.IsDead)
            {
                Debug.LogWarning($"[Cards] Use rejected. Invalid assigned character. Card={cardInstance.Data.CardId}, Slot={player.PlayerSlot}, CharacterNetId={(context.character != null ? context.character.netId : 0)}");
                return false;
            }

            if (!cardInstance.Data.CanUse(context))
            {
                Debug.LogWarning($"[Cards] Use rejected by card rules. Card={cardInstance.Data.CardId}, Slot={player.PlayerSlot}, Character={context.character.DisplayName}, DieMinimum={cardInstance.Data.MinimumDieValue}");
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

            if (!cardInstance.Data.Use(context))
            {
                Debug.LogWarning($"[Cards] Use failed during effect resolution. Card={cardInstance.Data.CardId}, Slot={player.PlayerSlot}, Character={context.character.DisplayName}");
                return false;
            }

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
                Match.MatchManager.Instance?.State == Match.MatchState.Completed ||
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

            uint activeCharacterNetId = TurnManager.Instance.GetActiveCharacterNetId(player.PlayerSlot);
            if (activeCharacterNetId == 0)
            {
                if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, sourceCharacter.netId))
                {
                    return false;
                }
            }
            else if (activeCharacterNetId != sourceCharacter.netId)
            {
                return false;
            }

            if (!CanAcceptCard(player, targetCharacter, cardInstance.Data))
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

                DealBaseTeleportationBeads(player);

                SyncPlayerCards(player);
            }
        }

        [Server]
        private void DealBaseTeleportationBeads(NetworkPlayerConnection player)
        {
            if (player == null || CharacterManager.Instance == null || !TryResolveCardData("TeleportationBeadsCard", out BaseCard beadsCard))
            {
                return;
            }

            foreach (CharacterType characterType in System.Enum.GetValues(typeof(CharacterType)))
            {
                if (!CharacterManager.Instance.TryGetServerCharacter(player, characterType, out NetworkCharacterPawn character))
                {
                    continue;
                }

                ServerSpawnOwnedCard(player, beadsCard, character, (int)characterType);
            }
        }

        [Server]
        private CardInstance ServerSpawnOwnedCard(
            NetworkPlayerConnection player,
            BaseCard cardData,
            NetworkCharacterPawn assignedCharacter,
            int handSlotIndex,
            bool requireTransferBeforeUse = false)
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
                player.PlayerSlot,
                requireTransferBeforeUse);
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
        public bool HasOwnedCard(NetworkPlayerConnection player, string cardId, uint assignedCharacterNetId = 0)
        {
            if (player == null || string.IsNullOrWhiteSpace(cardId) ||
                !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                return false;
            }

            string normalizedCardId = cardId.Trim();
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null || card.IsPendingConsume || card.Data == null)
                {
                    continue;
                }

                if (assignedCharacterNetId != 0 && card.AssignedCharacterNetId != assignedCharacterNetId)
                {
                    continue;
                }

                if (card.Data.CardId == normalizedCardId)
                {
                    return true;
                }
            }

            return false;
        }

        [Server]
        public bool HasOwnedCardOnCharacterType(NetworkPlayerConnection player, string cardId, CharacterType characterType)
        {
            return CharacterManager.Instance != null &&
                   CharacterManager.Instance.TryGetServerCharacter(player, characterType, out NetworkCharacterPawn character) &&
                   HasOwnedCard(player, cardId, character.netId);
        }

        [Server]
        public int CountOwnedCards(NetworkPlayerConnection player, uint assignedCharacterNetId, uint ignoredCardNetId = 0)
        {
            if (player == null || assignedCharacterNetId == 0 ||
                !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card != null &&
                    !card.IsPendingConsume &&
                    card.netId != ignoredCardNetId &&
                    card.AssignedCharacterNetId == assignedCharacterNetId)
                {
                    count++;
                }
            }

            return count;
        }

        [Server]
        public int GetCharacterCardCapacity(NetworkPlayerConnection player, NetworkCharacterPawn character)
        {
            if (player == null || character == null)
            {
                return BaseCharacterCardCapacity;
            }

            int capacity = BaseCharacterCardCapacity;
            if (HasOwnedCard(player, BagCardId, character.netId))
            {
                capacity += BagCapacityBonus;
            }

            return capacity;
        }

        [Server]
        public bool CanAcceptCard(NetworkPlayerConnection player, NetworkCharacterPawn character, BaseCard cardData, uint ignoredCardNetId = 0)
        {
            if (player == null || character == null || cardData == null || character.OwnerSlot != player.PlayerSlot || character.IsDead)
            {
                return false;
            }

            return CountOwnedCards(player, character.netId, ignoredCardNetId) < GetCharacterCardCapacity(player, character);
        }

        [Server]
        public bool ServerTryGrantCard(NetworkPlayerConnection player, NetworkCharacterPawn character, string cardId, uint ignoredCardNetId = 0)
        {
            if (player == null || character == null || string.IsNullOrWhiteSpace(cardId) || !TryResolveCardData(cardId, out BaseCard cardData))
            {
                return false;
            }

            int handSlotIndex = Mathf.Clamp((int)character.CharacterType, 0, HandSlotCount - 1);
            if (!CanAcceptCard(player, character, cardData, ignoredCardNetId))
            {
                return false;
            }

            ServerSpawnOwnedCard(player, cardData, character, handSlotIndex);
            SyncPlayerCards(player);
            return true;
        }

        [Server]
        public bool ServerTryGrantCardToCharacterType(NetworkPlayerConnection player, CharacterType characterType, string cardId, uint ignoredCardNetId = 0)
        {
            return CharacterManager.Instance != null &&
                   CharacterManager.Instance.TryGetServerCharacter(player, characterType, out NetworkCharacterPawn character) &&
                   ServerTryGrantCard(player, character, cardId, ignoredCardNetId);
        }

        [Server]
        public bool ServerTryConsumeCardsAndGrantCard(
            NetworkPlayerConnection player,
            uint preferredCharacterNetId,
            NetworkCharacterPawn grantCharacter,
            string grantedCardId,
            uint ignoredCardNetId,
            params string[] consumedCardIds)
        {
            if (player == null ||
                grantCharacter == null ||
                string.IsNullOrWhiteSpace(grantedCardId) ||
                consumedCardIds == null ||
                !TryResolveCardData(grantedCardId, out BaseCard grantedCardData))
            {
                return false;
            }

            if (!TryCollectOwnedCards(player, preferredCharacterNetId, consumedCardIds, out List<CardInstance> cardsToConsume))
            {
                return false;
            }

            if (!CanGrantCardAfterConsumingSelected(player, grantCharacter, grantedCardData, ignoredCardNetId, cardsToConsume))
            {
                return false;
            }

            for (int i = 0; i < cardsToConsume.Count; i++)
            {
                ServerRemoveCard(player, cardsToConsume[i], returnToDeck: true);
            }

            int handSlotIndex = Mathf.Clamp((int)grantCharacter.CharacterType, 0, HandSlotCount - 1);
            ServerSpawnOwnedCard(player, grantedCardData, grantCharacter, handSlotIndex);
            SyncPlayerCards(player);
            return true;
        }

        [Server]
        public bool ServerTryConsumeCardsAndGrantCardToCharacterType(
            NetworkPlayerConnection player,
            uint preferredCharacterNetId,
            CharacterType grantCharacterType,
            string grantedCardId,
            uint ignoredCardNetId,
            params string[] consumedCardIds)
        {
            return CharacterManager.Instance != null &&
                   CharacterManager.Instance.TryGetServerCharacter(player, grantCharacterType, out NetworkCharacterPawn grantCharacter) &&
                   ServerTryConsumeCardsAndGrantCard(
                       player,
                       preferredCharacterNetId,
                       grantCharacter,
                       grantedCardId,
                       ignoredCardNetId,
                       consumedCardIds);
        }

        [Server]
        public bool ServerHasCards(NetworkPlayerConnection player, params string[] cardIds)
        {
            if (player == null || cardIds == null)
            {
                return false;
            }

            Dictionary<string, int> required = BuildRequiredCardCounts(cardIds);
            foreach (KeyValuePair<string, int> pair in required)
            {
                if (CountOwnedCardsById(player, pair.Key) < pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        [Server]
        public bool ServerHasCraftingTool(NetworkPlayerConnection player)
        {
            return HasOwnedCard(player, "HammerCard");
        }

        [Server]
        public bool ServerTryConsumeCards(NetworkPlayerConnection player, params string[] cardIds)
        {
            return ServerTryConsumeCardsPreferCharacter(player, 0, cardIds);
        }

        [Server]
        public bool ServerTryConsumeCardsPreferCharacter(NetworkPlayerConnection player, uint preferredCharacterNetId, params string[] cardIds)
        {
            if (!ServerHasCards(player, cardIds))
            {
                return false;
            }

            List<CardInstance> cardsToConsume = new(cardIds.Length);
            for (int i = 0; i < cardIds.Length; i++)
            {
                if (!TryFindOwnedCard(player, cardIds[i], preferredCharacterNetId, cardsToConsume, out CardInstance card) &&
                    !TryFindOwnedCard(player, cardIds[i], 0, cardsToConsume, out card))
                {
                    return false;
                }

                cardsToConsume.Add(card);
            }

            for (int i = 0; i < cardsToConsume.Count; i++)
            {
                CardInstance card = cardsToConsume[i];
                ServerRemoveCard(player, card, returnToDeck: true);
            }

            SyncPlayerCards(player);
            return true;
        }

        [Server]
        public bool ServerTryDamageNearestEnemy(NetworkPlayerConnection attackerPlayer, NetworkCharacterPawn attacker, int range, int damage)
        {
            return ServerTryDamageNearestEnemy(attackerPlayer, attacker, 0, range, damage);
        }

        [Server]
        public bool ServerTryDamageNearestEnemy(NetworkPlayerConnection attackerPlayer, NetworkCharacterPawn attacker, int minRange, int maxRange, int damage)
        {
            if (attackerPlayer == null || attacker == null || attacker.IsDead || minRange < 0 || maxRange < minRange || damage <= 0)
            {
                return false;
            }

            if (!TryFindNearestEnemy(attackerPlayer, attacker, minRange, maxRange, out NetworkPlayerConnection targetPlayer, out NetworkCharacterPawn target))
            {
                return false;
            }

            target.ServerApplyDamage(damage);
            CharacterManager.Instance?.ServerSyncPlayerCharacters(targetPlayer);
            CharacterManager.Instance?.ServerSyncPlayerCharacters(attackerPlayer);

            if (target.IsDead)
            {
                ServerTransferInventoryOnKill(targetPlayer, target, attackerPlayer, attacker);
                Match.MatchManager.Instance?.ServerCheckEliminationVictory();
            }

            return true;
        }

        [Server]
        public bool ServerHasEnemyInRange(NetworkPlayerConnection attackerPlayer, NetworkCharacterPawn attacker, int range)
        {
            return ServerHasEnemyInRange(attackerPlayer, attacker, 0, range);
        }

        [Server]
        public bool ServerHasEnemyInRange(NetworkPlayerConnection attackerPlayer, NetworkCharacterPawn attacker, int minRange, int maxRange)
        {
            return TryFindNearestEnemy(attackerPlayer, attacker, Mathf.Max(0, minRange), Mathf.Max(0, maxRange), out _, out _);
        }

        [Server]
        public bool ServerCanGrantCardToCharacterType(NetworkPlayerConnection player, CharacterType characterType, string cardId, uint ignoredCardNetId = 0)
        {
            return CharacterManager.Instance != null &&
                   CharacterManager.Instance.TryGetServerCharacter(player, characterType, out NetworkCharacterPawn character) &&
                   TryResolveCardData(cardId, out BaseCard cardData) &&
                   CanAcceptCard(player, character, cardData, ignoredCardNetId);
        }

        [Server]
        public bool ServerCanGrantCards(NetworkPlayerConnection player, NetworkCharacterPawn character, uint ignoredCardNetId, params string[] cardIds)
        {
            if (player == null || character == null || cardIds == null || character.OwnerSlot != player.PlayerSlot || character.IsDead)
            {
                return false;
            }

            int grantCount = 0;
            for (int i = 0; i < cardIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(cardIds[i]) || !TryResolveCardData(cardIds[i], out _))
                {
                    return false;
                }

                grantCount++;
            }

            return CountOwnedCards(player, character.netId, ignoredCardNetId) + grantCount <= GetCharacterCardCapacity(player, character);
        }

        [Server]
        public bool ServerCanGrantCardsAfterConsuming(
            NetworkPlayerConnection player,
            NetworkCharacterPawn character,
            uint ignoredCardNetId,
            string[] consumedCardIds,
            params string[] grantedCardIds)
        {
            if (player == null || character == null || character.OwnerSlot != player.PlayerSlot || character.IsDead)
            {
                return false;
            }

            if (!ServerCanGrantCards(player, character, ignoredCardNetId, grantedCardIds) && character != null)
            {
                int grantCount = CountResolvableCards(grantedCardIds);
                int freedSlots = CountCardsOnCharacterMatchingRequirements(player, character.netId, ignoredCardNetId, consumedCardIds);
                return grantCount >= 0 &&
                       ServerHasCards(player, consumedCardIds) &&
                       CountOwnedCards(player, character.netId, ignoredCardNetId) - freedSlots + grantCount <= GetCharacterCardCapacity(player, character);
            }

            return ServerHasCards(player, consumedCardIds);
        }

        [Server]
        public bool ServerCanGrantCardsToCharacterTypeAfterConsuming(
            NetworkPlayerConnection player,
            CharacterType characterType,
            uint ignoredCardNetId,
            string[] consumedCardIds,
            params string[] grantedCardIds)
        {
            return CharacterManager.Instance != null &&
                   CharacterManager.Instance.TryGetServerCharacter(player, characterType, out NetworkCharacterPawn character) &&
                   ServerCanGrantCardsAfterConsuming(player, character, ignoredCardNetId, consumedCardIds, grantedCardIds);
        }

        [Server]
        public bool ServerTryHealCharacter(NetworkPlayerConnection player, NetworkCharacterPawn character, int amount)
        {
            if (player == null || character == null || character.OwnerSlot != player.PlayerSlot || character.IsDead || amount <= 0)
            {
                return false;
            }

            character.ServerHeal(amount);
            CharacterManager.Instance?.ServerSyncPlayerCharacters(player);
            return true;
        }

        [Server]
        public bool ServerTryDamageOwnedCharacter(NetworkPlayerConnection player, NetworkCharacterPawn character, int amount)
        {
            if (player == null || character == null || character.OwnerSlot != player.PlayerSlot || character.IsDead || amount <= 0)
            {
                return false;
            }

            character.ServerApplyDamage(amount);
            CharacterManager.Instance?.ServerSyncPlayerCharacters(player);
            Match.MatchManager.Instance?.ServerCheckEliminationVictory();
            return !character.IsDead;
        }

        [Server]
        private void ServerRemoveCard(NetworkPlayerConnection player, CardInstance card, bool returnToDeck)
        {
            if (player == null || card == null)
            {
                return;
            }

            if (returnToDeck && card.Data != null)
            {
                GetDeck()?.ReturnCardAndShuffle(card.Data);
            }

            if (cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                cards.Remove(card);
            }

            card.ServerMarkPendingConsume();
            NetworkServer.Destroy(card.gameObject);
        }

        [Server]
        private bool TryCollectOwnedCards(
            NetworkPlayerConnection player,
            uint preferredCharacterNetId,
            IReadOnlyList<string> cardIds,
            out List<CardInstance> cardsToConsume)
        {
            cardsToConsume = null;
            if (player == null || cardIds == null)
            {
                return false;
            }

            List<CardInstance> collected = new(cardIds.Count);
            for (int i = 0; i < cardIds.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(cardIds[i]) ||
                    (!TryFindOwnedCard(player, cardIds[i], preferredCharacterNetId, collected, out CardInstance card) &&
                     !TryFindOwnedCard(player, cardIds[i], 0, collected, out card)))
                {
                    return false;
                }

                collected.Add(card);
            }

            cardsToConsume = collected;
            return true;
        }

        [Server]
        private bool CanGrantCardAfterConsumingSelected(
            NetworkPlayerConnection player,
            NetworkCharacterPawn character,
            BaseCard grantedCardData,
            uint ignoredCardNetId,
            IReadOnlyList<CardInstance> cardsToConsume)
        {
            if (player == null ||
                character == null ||
                grantedCardData == null ||
                character.OwnerSlot != player.PlayerSlot ||
                character.IsDead)
            {
                return false;
            }

            int freedSlots = 0;
            if (cardsToConsume != null)
            {
                for (int i = 0; i < cardsToConsume.Count; i++)
                {
                    CardInstance card = cardsToConsume[i];
                    if (card != null &&
                        !card.IsPendingConsume &&
                        card.netId != ignoredCardNetId &&
                        card.AssignedCharacterNetId == character.netId)
                    {
                        freedSlots++;
                    }
                }
            }

            int cardsAfterConsume = CountOwnedCards(player, character.netId, ignoredCardNetId) - freedSlots;
            return cardsAfterConsume + 1 <= GetCharacterCardCapacity(player, character);
        }

        [Server]
        private bool TryFindOwnedCard(NetworkPlayerConnection player, string cardId, out CardInstance cardInstance)
        {
            return TryFindOwnedCard(player, cardId, 0, null, out cardInstance);
        }

        [Server]
        private bool TryFindOwnedCard(
            NetworkPlayerConnection player,
            string cardId,
            uint preferredCharacterNetId,
            List<CardInstance> excludedCards,
            out CardInstance cardInstance)
        {
            cardInstance = null;
            if (player == null || string.IsNullOrWhiteSpace(cardId) ||
                !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                return false;
            }

            string normalizedCardId = cardId.Trim();
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance candidate = cards[i];
                if (candidate == null ||
                    candidate.IsPendingConsume ||
                    candidate.Data == null ||
                    candidate.Data.CardId != normalizedCardId ||
                    (preferredCharacterNetId != 0 && candidate.AssignedCharacterNetId != preferredCharacterNetId) ||
                    (excludedCards != null && excludedCards.Contains(candidate)))
                {
                    continue;
                }

                cardInstance = candidate;
                return true;
            }

            return false;
        }

        [Server]
        private int CountCardsOnCharacterMatchingRequirements(
            NetworkPlayerConnection player,
            uint characterNetId,
            uint ignoredCardNetId,
            IEnumerable<string> requiredCardIds)
        {
            if (player == null || characterNetId == 0 || requiredCardIds == null ||
                !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                return 0;
            }

            Dictionary<string, int> required = BuildRequiredCardCounts(requiredCardIds);
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance candidate = cards[i];
                if (candidate == null ||
                    candidate.IsPendingConsume ||
                    candidate.netId == ignoredCardNetId ||
                    candidate.AssignedCharacterNetId != characterNetId ||
                    candidate.Data == null ||
                    !required.TryGetValue(candidate.Data.CardId, out int remaining) ||
                    remaining <= 0)
                {
                    continue;
                }

                required[candidate.Data.CardId] = remaining - 1;
                count++;
            }

            return count;
        }

        private static int CountResolvableCards(IEnumerable<string> cardIds)
        {
            if (cardIds == null)
            {
                return -1;
            }

            int count = 0;
            foreach (string cardId in cardIds)
            {
                if (string.IsNullOrWhiteSpace(cardId) || !TryResolveCardData(cardId, out _))
                {
                    return -1;
                }

                count++;
            }

            return count;
        }

        [Server]
        private int CountOwnedCardsById(NetworkPlayerConnection player, string cardId)
        {
            if (player == null || string.IsNullOrWhiteSpace(cardId) ||
                !cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                return 0;
            }

            string normalizedCardId = cardId.Trim();
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card != null && !card.IsPendingConsume && card.Data != null && card.Data.CardId == normalizedCardId)
                {
                    count++;
                }
            }

            return count;
        }

        private static Dictionary<string, int> BuildRequiredCardCounts(IEnumerable<string> cardIds)
        {
            Dictionary<string, int> required = new();
            foreach (string cardId in cardIds)
            {
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    continue;
                }

                string normalizedCardId = cardId.Trim();
                required.TryGetValue(normalizedCardId, out int count);
                required[normalizedCardId] = count + 1;
            }

            return required;
        }

        [Server]
        private NetworkCharacterPawn ResolveAssignmentCharacter(NetworkPlayerConnection player, NetworkCharacterPawn fallbackCharacter, int handSlotIndex)
        {
            if (player == null || CharacterManager.Instance == null)
            {
                return fallbackCharacter;
            }

            if (handSlotIndex >= 0 &&
                handSlotIndex <= byte.MaxValue &&
                System.Enum.IsDefined(typeof(CharacterType), (byte)handSlotIndex) &&
                CharacterManager.Instance.TryGetServerCharacter(player, (CharacterType)(byte)handSlotIndex, out NetworkCharacterPawn fixedCharacter))
            {
                return fixedCharacter;
            }

            return fallbackCharacter;
        }

        [Server]
        private static bool TryResolveCardData(string cardId, out BaseCard cardData)
        {
            cardData = null;
            Deck deck = GetDeck();
            return deck != null && deck.TryResolveCard(cardId, out cardData);
        }

        [Server]
        private static bool TryFindNearestEnemy(
            NetworkPlayerConnection attackerPlayer,
            NetworkCharacterPawn attacker,
            int minRange,
            int maxRange,
            out NetworkPlayerConnection targetPlayer,
            out NetworkCharacterPawn target)
        {
            targetPlayer = null;
            target = null;
            if (attackerPlayer == null || attacker == null || Match.MatchManager.Instance == null)
            {
                return false;
            }

            int bestDistance = int.MaxValue;
            IReadOnlyList<NetworkPlayerConnection> players = Match.MatchManager.Instance.Players;
            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                NetworkPlayerConnection candidatePlayer = players[playerIndex];
                if (candidatePlayer == null || candidatePlayer.PlayerSlot == attackerPlayer.PlayerSlot)
                {
                    continue;
                }

                for (int i = 0; i < candidatePlayer.Characters.Count; i++)
                {
                    CharacterSnapshot snapshot = candidatePlayer.Characters[i];
                    if (snapshot.NetId == 0 || snapshot.IsDead || CharacterManager.Instance == null ||
                        !CharacterManager.Instance.TryGetServerCharacter(snapshot.NetId, out NetworkCharacterPawn candidate) ||
                        candidate.IsDead)
                    {
                        continue;
                    }

                    int distance = ResolveBoardDistance(attacker.CurrentNodeId, candidate.CurrentNodeId);
                    if (distance < minRange || distance > maxRange || distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    targetPlayer = candidatePlayer;
                    target = candidate;
                }
            }

            return target != null;
        }

        private static int ResolveBoardDistance(string startNodeId, string destinationNodeId)
        {
            if (string.IsNullOrWhiteSpace(startNodeId) || string.IsNullOrWhiteSpace(destinationNodeId))
            {
                return -1;
            }

            if (startNodeId == destinationNodeId)
            {
                return 0;
            }

            BoardGraph board = BoardGraph.Instance != null ? BoardGraph.Instance : FindAnyObjectByType<BoardGraph>();
            if (board == null || !board.TryGetShortestPath(startNodeId, destinationNodeId, out List<string> path))
            {
                return -1;
            }

            return Mathf.Max(0, path.Count - 1);
        }

        [Server]
        private void ServerTransferInventoryOnKill(
            NetworkPlayerConnection defeatedPlayer,
            NetworkCharacterPawn defeatedCharacter,
            NetworkPlayerConnection winnerPlayer,
            NetworkCharacterPawn winnerCharacter)
        {
            if (defeatedPlayer == null || defeatedCharacter == null || winnerPlayer == null || winnerCharacter == null)
            {
                return;
            }

            if (!cardsByPlayerSlot.TryGetValue(defeatedPlayer.PlayerSlot, out List<CardInstance> defeatedCards))
            {
                return;
            }

            if (!cardsByPlayerSlot.TryGetValue(winnerPlayer.PlayerSlot, out List<CardInstance> winnerCards))
            {
                winnerCards = new List<CardInstance>();
                cardsByPlayerSlot[winnerPlayer.PlayerSlot] = winnerCards;
            }

            for (int i = defeatedCards.Count - 1; i >= 0; i--)
            {
                CardInstance card = defeatedCards[i];
                if (card == null || card.AssignedCharacterNetId != defeatedCharacter.netId)
                {
                    continue;
                }

                defeatedCards.RemoveAt(i);
                if (!winnerCards.Contains(card))
                {
                    winnerCards.Add(card);
                }

                int handSlotIndex = Mathf.Clamp((int)winnerCharacter.CharacterType, 0, HandSlotCount - 1);
                card.ServerAssignOwner(winnerPlayer.netId, winnerPlayer.PlayerSlot, winnerCharacter.netId, handSlotIndex);
            }

            SyncPlayerCards(defeatedPlayer);
            SyncPlayerCards(winnerPlayer);
        }

        [Server]
        private bool TryResolveTurnCharacter(NetworkPlayerConnection player, out NetworkCharacterPawn character)
        {
            character = null;
            if (player == null || TurnManager.Instance == null || CharacterManager.Instance == null)
            {
                return false;
            }

            uint activeCharacterNetId = TurnManager.Instance.GetActiveCharacterNetId(player.PlayerSlot);
            uint characterNetId = activeCharacterNetId != 0
                ? activeCharacterNetId
                : player.SelectedCharacterNetId;
            if (!CharacterManager.Instance.TryGetServerCharacter(characterNetId, out character))
            {
                return false;
            }

            return character.OwnerSlot == player.PlayerSlot && !character.IsDead;
        }

        [Server]
        private static bool TryResolveDeckNode(NetworkCharacterPawn character, out string deckNodeId, out BoardNodeKind nodeKind)
        {
            deckNodeId = string.Empty;
            nodeKind = BoardNodeKind.Normal;
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
            nodeKind = node.NodeKind;
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
                    RequiresTransferBeforeUse = card.RequiresTransferBeforeUse,
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
