using System.Collections.Generic;
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

            if (!TurnManager.Instance.ServerTryConsumeDieAction(player.PlayerSlot))
            {
                return false;
            }

            if (!deck.TryDraw(out BaseCard cardData))
            {
                Debug.LogWarning("[Cards] Draw aborted. Deck was exhausted before the card could be created.");
                return false;
            }

            if (!TryResolveDrawCharacter(player, cardData, out NetworkCharacterPawn assignedCharacter))
            {
                Debug.LogWarning($"[Cards] Draw aborted. Could not resolve a character for '{cardData.CardId}'.");
                return false;
            }

            CardContext drawContext = BuildContext(player, assignedCharacter, null);
            int handSlotIndex = Mathf.Clamp(cardData.ResolveHandSlotIndex(drawContext), 0, HandSlotCount - 1);

            CardInstance cardInstance = Instantiate(deck.CardPrefab);
            cardInstance.name = $"Card_{cardData.CardId}";
            cardInstance.Initialize(cardData, player.netId, assignedCharacter.netId, handSlotIndex, player.PlayerSlot);
            NetworkServer.Spawn(cardInstance.gameObject);

            if (!cardsByPlayerSlot.TryGetValue(player.PlayerSlot, out List<CardInstance> cards))
            {
                cards = new List<CardInstance>();
                cardsByPlayerSlot[player.PlayerSlot] = cards;
            }

            cards.Add(cardInstance);
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

            if (!TurnManager.Instance.ServerTryConsumeDieAction(player.PlayerSlot))
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

            cardInstance.ServerMarkPendingConsume();
            ServerConsumeCard(cardInstance);
            return true;
        }

        private const int HandSlotCount = 5;

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
        private bool TryResolveDrawCharacter(NetworkPlayerConnection player, BaseCard cardData, out NetworkCharacterPawn assignedCharacter)
        {
            assignedCharacter = null;
            if (player == null || cardData == null || CharacterManager.Instance == null)
            {
                return false;
            }

            if (cardData.HandSlotMode == CardHandSlotMode.FixedCharacter &&
                CharacterManager.Instance.TryGetServerCharacter(player, cardData.FixedHandCharacter, out assignedCharacter))
            {
                return true;
            }

            return CharacterManager.Instance.TryGetRandomActiveServerCharacter(player, out assignedCharacter);
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
