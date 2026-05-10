namespace RRaM.Core.Cards
{
    public struct CardSnapshot
    {
        public uint NetId;
        public uint OwnerNetId;
        public uint AssignedCharacterNetId;
        public string CardId;
        public string DisplayName;
        public bool IsConsumable;
        public bool IsPlayable;
        public int HandSlotIndex;
    }
}
