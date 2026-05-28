using UnityEngine;
using RRaM.Core.Characters;

namespace RRaM.Core.Cards
{
    [CreateAssetMenu(menuName = "Cards/Teleportation Beads", fileName = "TeleportationBeadsCard")]
    public sealed class TeleportationBeadsCard : BaseCard
    {
        public override int MinimumDieValue => 2;

        public override bool CanUse(CardContext context)
        {
            return context?.player != null &&
                   context.character != null &&
                   context.diceSystem != null &&
                   context.diceSystem.HasRolledThisTurn(context.player.PlayerSlot) &&
                   context.turnManager != null &&
                   context.turnManager.CanPlayerSpendDieActionWithMinimum(context.player.PlayerSlot, MinimumDieValue) &&
                   HasTeleportDestination(context);
        }

        public override bool Use(CardContext context)
        {
            if (context?.character == null || context.character.IsDead)
            {
                return false;
            }

            context.character.ServerTeleportToSpawn();
            CharacterManager.Instance?.ServerSyncPlayerCharacters(context.player);
            return true;
        }

        private static bool HasTeleportDestination(CardContext context)
        {
            return context?.character != null &&
                   !string.IsNullOrWhiteSpace(context.character.SpawnNodeId) &&
                   context.character.CurrentNodeId != context.character.SpawnNodeId;
        }
    }
}
