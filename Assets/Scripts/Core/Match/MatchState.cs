namespace RRaM.Core.Match
{
    public enum MatchState : byte
    {
        Bootstrapping = 0,
        Lobby = 1,
        Starting = 2,
        PlayerTurn = 3,
        ResolvingDwarfs = 4,
        Completed = 5
    }
}
