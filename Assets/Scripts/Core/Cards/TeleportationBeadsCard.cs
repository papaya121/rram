using UnityEngine;

namespace RRaM.Core.Cards
{
    [CreateAssetMenu(menuName = "Cards/Teleportation Beads", fileName = "TeleportationBeadsCard")]
    public sealed class TeleportationBeadsCard : BaseCard
    {
        public override bool CanUse(CardContext context)
        {
            return context?.player != null &&
                   context.character != null &&
                   context.diceSystem != null &&
                   context.diceSystem.HasRolledThisTurn(context.player.PlayerSlot) &&
                   (context.diceSystem.DieA >= 2 || context.diceSystem.DieB >= 2);
        }

        public override void Use(CardContext context)
        {
            if (!CanUse(context))
            {
                return;
            }

            context.character.ServerTeleportToSpawn();
        }
    }
}
