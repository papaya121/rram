using System.Collections.Generic;
using RRaM.Core.Characters;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public enum RulebookCardImplementationStatus : byte
    {
        RulesOnly = 0,
        ReadyForImplementation = 1,
        Implemented = 2
    }

    [CreateAssetMenu(menuName = "Cards/Rulebook Card", fileName = "RulebookCard")]
    public sealed class RulebookCard : BaseCard
    {
        [SerializeField] private string displayName;
        [SerializeField] private RulebookCardImplementationStatus implementationStatus = RulebookCardImplementationStatus.RulesOnly;
        [SerializeField, TextArea(2, 6)] private string futureHook;

        public override bool IsPlayable => ResolveKind() != RulebookCardKind.Resource;
        public override int MinimumDieValue => CardId switch
        {
            "BagCard" => 2,
            "BagRecipeCard" => 4,
            "BowRecipeCard" => 3,
            "DirtyMixedIronOreCard" => 2,
            "GoldNuggetCard" => 2,
            "HammerBlueprintCard" => 2,
            "RamCard" => 3,
            "RamHideCard" => 2,
            "SheepWoolCard" => 2,
            "ShamanCarpetRecipeCard" => 3,
            _ => 1
        };
        public override string DisplayName => string.IsNullOrWhiteSpace(displayName) ? base.DisplayName : displayName.Trim();
        public RulebookCardImplementationStatus ImplementationStatus => implementationStatus;
        public string FutureHook => futureHook;

        public override bool CanUse(CardContext context)
        {
            if (context?.player == null || context.character == null || context.character.IsDead)
            {
                return false;
            }

            RulebookCardKind kind = ResolveKind();
            if (kind == RulebookCardKind.Resource)
            {
                return false;
            }

            CardManager cards = context.cardManager;
            bool hasServerState = cards != null && cards.isServer;
            if (!hasServerState)
            {
                return CanUseWithLocalSnapshot(context);
            }

            uint currentCardNetId = context.cardInstance != null ? context.cardInstance.netId : 0;
            return CardId switch
            {
                "BagCard" => context.character.CharacterType == CharacterType.BlacksmithAssistant &&
                             (cards.ServerCanGrantCardsAfterConsuming(
                                  context.player,
                                  context.character,
                                  0,
                                  new[] { "DirtyMixedIronOreCard", "DirtyMixedIronOreCard", "DirtyMixedIronOreCard" },
                                  "MediumQualityIronOreCard") ||
                              cards.ServerCanGrantCardsAfterConsuming(
                                  context.player,
                                  context.character,
                                  0,
                                  new[] { "GoldNuggetCard" },
                                  "MediumQualityIronOreCard")),
                "BagRecipeCard" => HasRequiredCraftingTool(cards, context.player, CardId) &&
                                   cards.ServerHasCards(context.player, "ShamanCarpetCard") &&
                                   cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                                       context.player,
                                       CharacterType.BlacksmithAssistant,
                                       currentCardNetId,
                                       new[] { "CleanedRamHideCard", "RamWoolThreadBallCard" },
                                       "BagCard"),
                "BowCard" => context.character.CharacterType == CharacterType.Hunter &&
                             cards.ServerHasEnemyInRange(context.player, context.character, 2, 3),
                "BowRecipeCard" => HasRequiredCraftingTool(cards, context.player, CardId) &&
                                   cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                                       context.player,
                                       CharacterType.Hunter,
                                       currentCardNetId,
                                       new[] { "FlexibleStickCard", "RamWoolThreadBallCard" },
                                       "BowCard"),
                "ClubBlueprintCard" => HasRequiredCraftingTool(cards, context.player, CardId) &&
                                       (cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                                           context.player,
                                           CharacterType.Warrior,
                                           currentCardNetId,
                                           new[] { "BearHideCard" },
                                           "ClubCard") ||
                                       cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                                           context.player,
                                           CharacterType.Warrior,
                                           currentCardNetId,
                                           new[] { "RamHideCard" },
                                           "ClubCard")),
                "ClubCard" => context.character.CharacterType == CharacterType.Warrior &&
                              cards.ServerHasEnemyInRange(context.player, context.character, 1),
                "DirtyMixedIronOreCard" => context.character.CharacterType == CharacterType.Blacksmith &&
                                           cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, "MixedIronOreCard"),
                "GoldNuggetCard" => context.character.CharacterType == CharacterType.BlacksmithAssistant &&
                                    cards.HasOwnedCard(context.player, "BagCard", context.character.netId) &&
                                    cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, "MediumQualityIronOreCard"),
                "HammerBlueprintCard" => cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                    context.player,
                    CharacterType.Blacksmith,
                    currentCardNetId,
                    new[] { "MixedIronOreCard" },
                    "HammerCard"),
                "RamCard" => context.character.Health > 1 &&
                             cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, "RamHideCard"),
                "RamHideCard" => cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, ResolveRamHideProduct(context)),
                "ShamanCarpetCard" => context.character.CharacterType == CharacterType.Shaman &&
                                      context.character.Health < NetworkCharacterPawn.MaxHealth,
                "ShamanCarpetRecipeCard" => cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                    context.player,
                    CharacterType.Shaman,
                    currentCardNetId,
                    new[] { "BearHideCard", "RamHideThreadCard" },
                    "ShamanCarpetCard"),
                "SheepWoolCard" => cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, "RamWoolThreadBallCard"),
                _ => false
            };
        }

        public override bool Use(CardContext context)
        {
            if (!CanUse(context) || context.cardManager == null)
            {
                return false;
            }

            CardManager cards = context.cardManager;
            int dieBonus = Mathf.Max(0, context.turnManager != null ? context.turnManager.LastConsumedDieValue : 0);
            uint currentCardNetId = context.cardInstance != null ? context.cardInstance.netId : 0;
            switch (CardId)
            {
                case "BagCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "DirtyMixedIronOreCard", "DirtyMixedIronOreCard", "DirtyMixedIronOreCard"))
                    {
                        return cards.ServerTryGrantCard(context.player, context.character, "MediumQualityIronOreCard");
                    }

                    return cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "GoldNuggetCard") &&
                           cards.ServerTryGrantCard(context.player, context.character, "MediumQualityIronOreCard");
                case "BagRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "CleanedRamHideCard", "RamWoolThreadBallCard"))
                    {
                        return cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.BlacksmithAssistant, "BagCard", currentCardNetId);
                    }

                    return false;
                case "BowCard":
                    return cards.ServerTryDamageNearestEnemy(context.player, context.character, 2, 3, 5 + dieBonus);
                case "BowRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "FlexibleStickCard", "RamWoolThreadBallCard"))
                    {
                        return cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Hunter, "BowCard", currentCardNetId);
                    }

                    return false;
                case "ClubBlueprintCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "BearHideCard") ||
                        cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "RamHideCard"))
                    {
                        return cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Warrior, "ClubCard", currentCardNetId);
                    }

                    return false;
                case "ClubCard":
                    return cards.ServerTryDamageNearestEnemy(context.player, context.character, 1, 10 + dieBonus);
                case "DirtyMixedIronOreCard":
                    return cards.ServerTryGrantCard(context.player, context.character, "MixedIronOreCard", currentCardNetId);
                case "GoldNuggetCard":
                    return cards.ServerTryGrantCard(context.player, context.character, "MediumQualityIronOreCard", currentCardNetId);
                case "HammerBlueprintCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "MixedIronOreCard"))
                    {
                        return cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Blacksmith, "HammerCard", currentCardNetId);
                    }

                    return false;
                case "RamCard":
                    if (cards.ServerTryDamageOwnedCharacter(context.player, context.character, 1))
                    {
                        return cards.ServerTryGrantCard(context.player, context.character, "RamHideCard", currentCardNetId);
                    }

                    return false;
                case "RamHideCard":
                    return cards.ServerTryGrantCard(context.player, context.character, ResolveRamHideProduct(context), currentCardNetId);
                case "ShamanCarpetCard":
                    return cards.ServerTryHealCharacter(context.player, context.character, 2);
                case "ShamanCarpetRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "BearHideCard", "RamHideThreadCard"))
                    {
                        return cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Shaman, "ShamanCarpetCard", currentCardNetId);
                    }

                    return false;
                case "SheepWoolCard":
                    return cards.ServerTryGrantCard(context.player, context.character, "RamWoolThreadBallCard", currentCardNetId);
            }

            return false;
        }

        private bool CanUseWithLocalSnapshot(CardContext context)
        {
            NetworkPlayerConnection player = context.player;
            NetworkCharacterPawn character = context.character;
            if (player == null || character == null || character.IsDead)
            {
                return false;
            }

            uint currentCardNetId = context.cardInstance != null ? context.cardInstance.netId : 0;
            return CardId switch
            {
                "BagCard" => character.CharacterType == CharacterType.BlacksmithAssistant &&
                             (LocalCanGrantCardsAfterConsuming(
                                  player,
                                  character.netId,
                                  0,
                                  new[] { "DirtyMixedIronOreCard", "DirtyMixedIronOreCard", "DirtyMixedIronOreCard" },
                                  "MediumQualityIronOreCard") ||
                              LocalCanGrantCardsAfterConsuming(
                                  player,
                                  character.netId,
                                  0,
                                  new[] { "GoldNuggetCard" },
                                  "MediumQualityIronOreCard")),
                "BagRecipeCard" => HasLocalRequiredCraftingTool(player, CardId) &&
                                   HasLocalCards(player, "ShamanCarpetCard") &&
                                   LocalCanGrantCardsToCharacterTypeAfterConsuming(
                                       player,
                                       CharacterType.BlacksmithAssistant,
                                       currentCardNetId,
                                       new[] { "CleanedRamHideCard", "RamWoolThreadBallCard" },
                                       "BagCard"),
                "BowCard" => character.CharacterType == CharacterType.Hunter,
                "BowRecipeCard" => HasLocalRequiredCraftingTool(player, CardId) &&
                                   LocalCanGrantCardsToCharacterTypeAfterConsuming(
                                       player,
                                       CharacterType.Hunter,
                                       currentCardNetId,
                                       new[] { "FlexibleStickCard", "RamWoolThreadBallCard" },
                                       "BowCard"),
                "ClubBlueprintCard" => HasLocalRequiredCraftingTool(player, CardId) &&
                                       (LocalCanGrantCardsToCharacterTypeAfterConsuming(
                                           player,
                                           CharacterType.Warrior,
                                           currentCardNetId,
                                           new[] { "BearHideCard" },
                                           "ClubCard") ||
                                       LocalCanGrantCardsToCharacterTypeAfterConsuming(
                                           player,
                                           CharacterType.Warrior,
                                           currentCardNetId,
                                           new[] { "RamHideCard" },
                                           "ClubCard")),
                "ClubCard" => character.CharacterType == CharacterType.Warrior,
                "DirtyMixedIronOreCard" => character.CharacterType == CharacterType.Blacksmith &&
                                           LocalCanGrantCards(player, character.netId, currentCardNetId, "MixedIronOreCard"),
                "GoldNuggetCard" => character.CharacterType == CharacterType.BlacksmithAssistant &&
                                    CountLocalCards(player, "BagCard", character.netId) > 0 &&
                                    LocalCanGrantCards(player, character.netId, currentCardNetId, "MediumQualityIronOreCard"),
                "HammerBlueprintCard" => LocalCanGrantCardsToCharacterTypeAfterConsuming(
                    player,
                    CharacterType.Blacksmith,
                    currentCardNetId,
                    new[] { "MixedIronOreCard" },
                    "HammerCard"),
                "RamCard" => character.Health > 1 &&
                             LocalCanGrantCards(player, character.netId, currentCardNetId, "RamHideCard"),
                "RamHideCard" => LocalCanGrantCards(player, character.netId, currentCardNetId, ResolveRamHideProduct(context)),
                "ShamanCarpetCard" => character.CharacterType == CharacterType.Shaman &&
                                      character.Health < NetworkCharacterPawn.MaxHealth,
                "ShamanCarpetRecipeCard" => LocalCanGrantCardsToCharacterTypeAfterConsuming(
                    player,
                    CharacterType.Shaman,
                    currentCardNetId,
                    new[] { "BearHideCard", "RamHideThreadCard" },
                    "ShamanCarpetCard"),
                "SheepWoolCard" => LocalCanGrantCards(player, character.netId, currentCardNetId, "RamWoolThreadBallCard"),
                _ => false
            };
        }

        private static string ResolveRamHideProduct(CardContext context)
        {
            return context?.character != null && context.character.CharacterType == CharacterType.Shaman
                ? "RamHideThreadCard"
                : "CleanedRamHideCard";
        }

        private static bool HasRequiredCraftingTool(CardManager cards, NetworkPlayerConnection player, string cardId)
        {
            return !RequiresCraftingTool(cardId) || cards.ServerHasCraftingTool(player);
        }

        private static bool HasLocalRequiredCraftingTool(NetworkPlayerConnection player, string cardId)
        {
            return !RequiresCraftingTool(cardId) || HasLocalCards(player, "HammerCard");
        }

        private static bool RequiresCraftingTool(string cardId)
        {
            return cardId is "BagRecipeCard" or "BowRecipeCard" or "ClubBlueprintCard";
        }

        private static bool HasLocalCards(NetworkPlayerConnection player, params string[] cardIds)
        {
            if (player == null || cardIds == null)
            {
                return false;
            }

            Dictionary<string, int> required = BuildRequiredCardCounts(cardIds);
            foreach (KeyValuePair<string, int> pair in required)
            {
                if (CountLocalCards(player, pair.Key) < pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountLocalCards(NetworkPlayerConnection player, string cardId, uint assignedCharacterNetId = 0, uint ignoredCardNetId = 0)
        {
            if (player == null || string.IsNullOrWhiteSpace(cardId))
            {
                return 0;
            }

            string normalizedCardId = cardId.Trim();
            int count = 0;
            for (int i = 0; i < player.Cards.Count; i++)
            {
                CardSnapshot candidate = player.Cards[i];
                if (candidate.NetId == ignoredCardNetId ||
                    candidate.CardId != normalizedCardId ||
                    (assignedCharacterNetId != 0 && candidate.AssignedCharacterNetId != assignedCharacterNetId))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool LocalCanGrantCardsToCharacterTypeAfterConsuming(
            NetworkPlayerConnection player,
            CharacterType characterType,
            uint ignoredCardNetId,
            string[] consumedCardIds,
            params string[] grantedCardIds)
        {
            return TryGetLocalCharacterNetId(player, characterType, out uint characterNetId) &&
                   LocalCanGrantCardsAfterConsuming(player, characterNetId, ignoredCardNetId, consumedCardIds, grantedCardIds);
        }

        private static bool LocalCanGrantCardsAfterConsuming(
            NetworkPlayerConnection player,
            uint characterNetId,
            uint ignoredCardNetId,
            string[] consumedCardIds,
            params string[] grantedCardIds)
        {
            if (player == null ||
                characterNetId == 0 ||
                consumedCardIds == null ||
                !TryGetLocalCharacterSnapshot(player, characterNetId, out CharacterSnapshot character) ||
                character.IsDead ||
                !HasLocalCards(player, consumedCardIds))
            {
                return false;
            }

            if (LocalCanGrantCards(player, characterNetId, ignoredCardNetId, grantedCardIds))
            {
                return true;
            }

            int grantCount = CountLocalCardIds(grantedCardIds);
            if (grantCount < 0)
            {
                return false;
            }

            int freedSlots = CountLocalCardsOnCharacterMatchingRequirements(player, characterNetId, ignoredCardNetId, consumedCardIds);
            return CountLocalCardsOnCharacter(player, characterNetId, ignoredCardNetId) - freedSlots + grantCount <=
                   GetLocalCharacterCardCapacity(player, characterNetId);
        }

        private static bool LocalCanGrantCards(NetworkPlayerConnection player, uint characterNetId, uint ignoredCardNetId, params string[] cardIds)
        {
            if (player == null ||
                characterNetId == 0 ||
                !TryGetLocalCharacterSnapshot(player, characterNetId, out CharacterSnapshot character) ||
                character.IsDead)
            {
                return false;
            }

            int grantCount = CountLocalCardIds(cardIds);
            return grantCount >= 0 &&
                   CountLocalCardsOnCharacter(player, characterNetId, ignoredCardNetId) + grantCount <=
                   GetLocalCharacterCardCapacity(player, characterNetId);
        }

        private static int CountLocalCardsOnCharacter(NetworkPlayerConnection player, uint characterNetId, uint ignoredCardNetId)
        {
            if (player == null || characterNetId == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < player.Cards.Count; i++)
            {
                CardSnapshot candidate = player.Cards[i];
                if (candidate.NetId != ignoredCardNetId && candidate.AssignedCharacterNetId == characterNetId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountLocalCardsOnCharacterMatchingRequirements(
            NetworkPlayerConnection player,
            uint characterNetId,
            uint ignoredCardNetId,
            IEnumerable<string> requiredCardIds)
        {
            if (player == null || characterNetId == 0 || requiredCardIds == null)
            {
                return 0;
            }

            Dictionary<string, int> required = BuildRequiredCardCounts(requiredCardIds);
            int count = 0;
            for (int i = 0; i < player.Cards.Count; i++)
            {
                CardSnapshot candidate = player.Cards[i];
                if (candidate.NetId == ignoredCardNetId ||
                    candidate.AssignedCharacterNetId != characterNetId ||
                    !required.TryGetValue(candidate.CardId, out int remaining) ||
                    remaining <= 0)
                {
                    continue;
                }

                required[candidate.CardId] = remaining - 1;
                count++;
            }

            return count;
        }

        private static int GetLocalCharacterCardCapacity(NetworkPlayerConnection player, uint characterNetId)
        {
            const int baseCharacterCardCapacity = 10;
            const int bagCapacityBonus = 3;

            if (player == null || characterNetId == 0)
            {
                return baseCharacterCardCapacity;
            }

            return CountLocalCards(player, "BagCard", characterNetId) > 0
                ? baseCharacterCardCapacity + bagCapacityBonus
                : baseCharacterCardCapacity;
        }

        private static bool TryGetLocalCharacterNetId(NetworkPlayerConnection player, CharacterType characterType, out uint characterNetId)
        {
            characterNetId = 0;
            if (player == null)
            {
                return false;
            }

            for (int i = 0; i < player.Characters.Count; i++)
            {
                CharacterSnapshot character = player.Characters[i];
                if (character.NetId != 0 && character.CharacterType == characterType && !character.IsDead)
                {
                    characterNetId = character.NetId;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetLocalCharacterSnapshot(NetworkPlayerConnection player, uint characterNetId, out CharacterSnapshot snapshot)
        {
            snapshot = default;
            if (player == null || characterNetId == 0)
            {
                return false;
            }

            for (int i = 0; i < player.Characters.Count; i++)
            {
                CharacterSnapshot character = player.Characters[i];
                if (character.NetId == characterNetId)
                {
                    snapshot = character;
                    return true;
                }
            }

            return false;
        }

        private static int CountLocalCardIds(IEnumerable<string> cardIds)
        {
            if (cardIds == null)
            {
                return -1;
            }

            int count = 0;
            foreach (string cardId in cardIds)
            {
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    return -1;
                }

                count++;
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

        private RulebookCardKind ResolveKind()
        {
            return CardId switch
            {
                "BearHideCard" => RulebookCardKind.Resource,
                "CleanedRamHideCard" => RulebookCardKind.Resource,
                "FlexibleStickCard" => RulebookCardKind.Resource,
                "HammerCard" => RulebookCardKind.Resource,
                "MediumQualityIronOreCard" => RulebookCardKind.Resource,
                "MixedIronOreCard" => RulebookCardKind.Resource,
                "RamHideThreadCard" => RulebookCardKind.Resource,
                "RamWoolThreadBallCard" => RulebookCardKind.Resource,
                _ => RulebookCardKind.Action
            };
        }

        private enum RulebookCardKind
        {
            Action,
            Resource
        }
    }
}
