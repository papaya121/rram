using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public enum RulebookCardImplementationStatus : byte
    {
        RulesOnly = 0,
        ReadyForImplementation = 1
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
                return true;
            }

            uint currentCardNetId = context.cardInstance != null ? context.cardInstance.netId : 0;
            return CardId switch
            {
                "BagCard" => context.character.CharacterType == CharacterType.BlacksmithAssistant &&
                             (cards.ServerCanGrantCardsAfterConsuming(
                                  context.player,
                                  context.character,
                                  currentCardNetId,
                                  new[] { "DirtyMixedIronOreCard", "DirtyMixedIronOreCard", "DirtyMixedIronOreCard" },
                                  "MediumQualityIronOreCard") ||
                              cards.ServerCanGrantCardsAfterConsuming(
                                  context.player,
                                  context.character,
                                  currentCardNetId,
                                  new[] { "GoldNuggetCard" },
                                  "MediumQualityIronOreCard")),
                "BagRecipeCard" => cards.ServerHasCards(context.player, "ShamanCarpetCard") &&
                                   cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                                       context.player,
                                       CharacterType.BlacksmithAssistant,
                                       currentCardNetId,
                                       new[] { "CleanedRamHideCard", "RamWoolThreadBallCard" },
                                       "BagCard"),
                "BowCard" => context.character.CharacterType == CharacterType.Hunter &&
                             cards.ServerHasEnemyInRange(context.player, context.character, 2, 3),
                "BowRecipeCard" => cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
                    context.player,
                    CharacterType.Hunter,
                    currentCardNetId,
                    new[] { "FlexibleStickCard", "RamWoolThreadBallCard" },
                    "BowCard"),
                "ClubBlueprintCard" => cards.ServerCanGrantCardsToCharacterTypeAfterConsuming(
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
                                           "ClubCard"),
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
                "RamCard" => cards.ServerCanGrantCards(context.player, context.character, currentCardNetId, "RamHideCard", "SheepWoolCard"),
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

        public override void Use(CardContext context)
        {
            if (!CanUse(context) || context.cardManager == null)
            {
                return;
            }

            CardManager cards = context.cardManager;
            int dieBonus = Mathf.Max(0, context.turnManager != null ? context.turnManager.LastConsumedDieValue : 0);
            uint currentCardNetId = context.cardInstance != null ? context.cardInstance.netId : 0;
            switch (CardId)
            {
                case "BagCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "DirtyMixedIronOreCard", "DirtyMixedIronOreCard", "DirtyMixedIronOreCard") ||
                        cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "GoldNuggetCard"))
                    {
                        cards.ServerTryGrantCard(context.player, context.character, "MediumQualityIronOreCard");
                    }
                    break;
                case "BagRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "CleanedRamHideCard", "RamWoolThreadBallCard"))
                    {
                        cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.BlacksmithAssistant, "BagCard", currentCardNetId);
                    }
                    break;
                case "BowCard":
                    cards.ServerTryDamageNearestEnemy(context.player, context.character, 2, 3, 5 + dieBonus);
                    break;
                case "BowRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "FlexibleStickCard", "RamWoolThreadBallCard"))
                    {
                        cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Hunter, "BowCard", currentCardNetId);
                    }
                    break;
                case "ClubBlueprintCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "BearHideCard") ||
                        cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "RamHideCard"))
                    {
                        cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Warrior, "ClubCard", currentCardNetId);
                    }
                    break;
                case "ClubCard":
                    cards.ServerTryDamageNearestEnemy(context.player, context.character, 1, 10 + dieBonus);
                    break;
                case "DirtyMixedIronOreCard":
                    cards.ServerTryGrantCard(context.player, context.character, "MixedIronOreCard", currentCardNetId);
                    break;
                case "GoldNuggetCard":
                    cards.ServerTryGrantCard(context.player, context.character, "MediumQualityIronOreCard", currentCardNetId);
                    break;
                case "HammerBlueprintCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "MixedIronOreCard"))
                    {
                        cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Blacksmith, "HammerCard", currentCardNetId);
                    }
                    break;
                case "RamCard":
                    cards.ServerTryGrantCard(context.player, context.character, "RamHideCard", currentCardNetId);
                    cards.ServerTryGrantCard(context.player, context.character, "SheepWoolCard", currentCardNetId);
                    break;
                case "RamHideCard":
                    cards.ServerTryGrantCard(context.player, context.character, ResolveRamHideProduct(context), currentCardNetId);
                    break;
                case "ShamanCarpetCard":
                    cards.ServerTryHealCharacter(context.player, context.character, 2);
                    break;
                case "ShamanCarpetRecipeCard":
                    if (cards.ServerTryConsumeCardsPreferCharacter(context.player, context.character.netId, "BearHideCard", "RamHideThreadCard"))
                    {
                        cards.ServerTryGrantCardToCharacterType(context.player, CharacterType.Shaman, "ShamanCarpetCard", currentCardNetId);
                    }
                    break;
                case "SheepWoolCard":
                    cards.ServerTryGrantCard(context.player, context.character, "RamWoolThreadBallCard", currentCardNetId);
                    break;
            }
        }

        private static string ResolveRamHideProduct(CardContext context)
        {
            return context?.character != null && context.character.CharacterType == CharacterType.Shaman
                ? "RamHideThreadCard"
                : "CleanedRamHideCard";
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
