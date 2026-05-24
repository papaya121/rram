using UnityEngine;

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
                   context.turnManager.CanPlayerSpendDieActionWithMinimum(context.player.PlayerSlot, MinimumDieValue);
        }

        public override bool Use(CardContext context)
        {
            if (context?.character == null || context.character.IsDead)
            {
                return false;
            }

            context.character.ServerTeleportToSpawn();
            return true;
        }
    }
}
