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

        public override bool IsPlayable => false;
        public override string DisplayName => string.IsNullOrWhiteSpace(displayName) ? base.DisplayName : displayName.Trim();
        public RulebookCardImplementationStatus ImplementationStatus => implementationStatus;
        public string FutureHook => futureHook;

        public override bool CanUse(CardContext context)
        {
            return false;
        }

        public override void Use(CardContext context) { }
    }
}
