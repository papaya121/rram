namespace RRaM.Core.Dice
{
    public readonly struct DiceRollResult
    {
        public DiceRollResult(int dieA, int dieB)
        {
            DieA = dieA;
            DieB = dieB;
            Total = dieA + dieB;
        }

        public int DieA { get; }
        public int DieB { get; }
        public int Total { get; }
    }
}
