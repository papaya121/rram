using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public enum CardHandSlotMode : byte
    {
        SelectedCharacter = 0,
        FixedCharacter = 1
    }

    public abstract class BaseCard : ScriptableObject
    {
        public string cardName;
        public Sprite cardImage;
        public bool isConsumable = true;

        [SerializeField] private string cardId;
        [SerializeField] private CardHandSlotMode handSlotMode = CardHandSlotMode.SelectedCharacter;
        [SerializeField] private CharacterType fixedHandCharacter = CharacterType.Blacksmith;

        public string CardId => string.IsNullOrWhiteSpace(cardId) ? name : cardId.Trim();
        public CardHandSlotMode HandSlotMode => handSlotMode;
        public CharacterType FixedHandCharacter => fixedHandCharacter;
        public virtual bool IsPlayable => true;
        public virtual int MinimumDieValue => 1;
        public virtual string DisplayName => ResolveDisplayName(cardName, name);

        public virtual int ResolveHandSlotIndex(CardContext context)
        {
            if (handSlotMode == CardHandSlotMode.FixedCharacter)
            {
                return (int)fixedHandCharacter;
            }

            return context?.character != null
                ? (int)context.character.CharacterType
                : 0;
        }

        public abstract bool CanUse(CardContext context);
        public abstract bool Use(CardContext context);

        private static string ResolveDisplayName(string rawName, string fallback)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return fallback;
            }

            string firstLine = rawName.Trim();
            int newLineIndex = firstLine.IndexOf('\n');
            if (newLineIndex >= 0)
            {
                firstLine = firstLine[..newLineIndex];
            }

            firstLine = firstLine
                .Replace("<b>", string.Empty)
                .Replace("</b>", string.Empty)
                .Trim();

            return string.IsNullOrWhiteSpace(firstLine) ? fallback : firstLine.TrimEnd('.');
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(cardName))
            {
                cardName = name;
            }

            if (string.IsNullOrWhiteSpace(cardId))
            {
                cardId = name;
            }
        }
#endif
    }
}
