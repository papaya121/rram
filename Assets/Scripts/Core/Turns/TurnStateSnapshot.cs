namespace RRaM.Core.Turns
{
    public readonly struct TurnStateSnapshot
    {
        public TurnStateSnapshot(int turnNumber, int currentPlayerSlot, TurnPhase phase, bool hasMoved)
        {
            TurnNumber = turnNumber;
            CurrentPlayerSlot = currentPlayerSlot;
            Phase = phase;
            HasMoved = hasMoved;
        }

        public int TurnNumber { get; }
        public int CurrentPlayerSlot { get; }
        public TurnPhase Phase { get; }
        public bool HasMoved { get; }
    }
}
