using System.Collections.Generic;
using RRaM.Core.Board;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public sealed class Deck : MonoBehaviour
    {
        public static Deck Instance { get; private set; }

        [SerializeField] private CardInstance cardPrefab;
        [SerializeField] private Transform drawOrigin;
        [SerializeField] private Transform cameraWaypoint;
        [SerializeField] private List<BaseCard> cards = new();

        private readonly Dictionary<string, BaseCard> cardsById = new();
        private readonly List<BaseCard> runtimeDeck = new();

        public CardInstance CardPrefab => cardPrefab;
        public Transform DrawOrigin => drawOrigin != null ? drawOrigin : transform;
        public Transform CameraWaypoint => cameraWaypoint;
        public int RemainingCards => runtimeDeck.Count;
        public bool HasCards => runtimeDeck.Count > 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Cards] Duplicate deck detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            RebuildCatalog();
            ResetRuntimeState();
        }

        public void ResetRuntimeState()
        {
            runtimeDeck.Clear();
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null)
                {
                    runtimeDeck.Add(cards[i]);
                }
            }
        }

        public BaseCard Draw()
        {
            if (runtimeDeck.Count == 0)
            {
                return null;
            }

            BaseCard card = runtimeDeck[0];
            runtimeDeck.RemoveAt(0);
            return card;
        }

        public bool TryDraw(out BaseCard card)
        {
            card = Draw();
            return card != null;
        }

        public bool TryDraw(BoardNodeKind nodeKind, out BaseCard card)
        {
            for (int i = 0; i < runtimeDeck.Count; i++)
            {
                BaseCard candidate = runtimeDeck[i];
                if (candidate == null || !IsCardAllowedForNode(candidate, nodeKind))
                {
                    continue;
                }

                runtimeDeck.RemoveAt(i);
                card = candidate;
                return true;
            }

            card = Draw();
            return card != null;
        }

        public bool TryResolveCard(string cardId, out BaseCard card)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                card = null;
                return false;
            }

            return cardsById.TryGetValue(cardId.Trim(), out card);
        }

        public void ReturnCardAndShuffle(BaseCard card)
        {
            if (card == null)
            {
                return;
            }

            runtimeDeck.Add(card);
            ShuffleRuntimeDeck();
        }

        private void RebuildCatalog()
        {
            cardsById.Clear();
            for (int i = 0; i < cards.Count; i++)
            {
                BaseCard card = cards[i];
                if (card == null)
                {
                    continue;
                }

                string cardId = card.CardId;
                if (cardsById.TryGetValue(cardId, out BaseCard existingCard))
                {
                    if (existingCard != card)
                    {
                        Debug.LogWarning($"[Cards] Duplicate card id '{cardId}' on deck '{name}'.", this);
                    }

                    continue;
                }

                cardsById[cardId] = card;
            }
        }

        private void ShuffleRuntimeDeck()
        {
            for (int i = runtimeDeck.Count - 1; i > 0; i--)
            {
                int swapIndex = Random.Range(0, i + 1);
                (runtimeDeck[i], runtimeDeck[swapIndex]) = (runtimeDeck[swapIndex], runtimeDeck[i]);
            }
        }

        private static bool IsCardAllowedForNode(BaseCard card, BoardNodeKind nodeKind)
        {
            string cardId = card.CardId;
            return nodeKind switch
            {
                BoardNodeKind.GreenDeck => cardId is
                    "RamCard" or
                    "SheepWoolCard" or
                    "RamHideCard" or
                    "RamHideThreadCard" or
                    "RamWoolThreadBallCard" or
                    "CleanedRamHideCard" or
                    "FlexibleStickCard" or
                    "DirtyMixedIronOreCard" or
                    "MixedIronOreCard" or
                    "GoldNuggetCard" or
                    "MediumQualityIronOreCard",
                BoardNodeKind.RedDeck => cardId is
                    "RamCard" or
                    "BearHideCard" or
                    "ClubBlueprintCard" or
                    "ClubCard" or
                    "BowCard",
                _ => true
            };
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
