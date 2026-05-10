using UnityEngine;

namespace RRaM.Core.Cards
{
    [CreateAssetMenu(menuName = "Cards/Skip Turn", fileName = "SkipTurnCard")]
    public sealed class SkipTurnCard : BaseCard
    {
        public override bool CanUse(CardContext context)
        {
            return context?.player != null;
        }

        public override void Use(CardContext context)
        {
            if (!CanUse(context))
            {
                return;
            }

            context.player.ServerForceEndTurn();
        }
    }
}
