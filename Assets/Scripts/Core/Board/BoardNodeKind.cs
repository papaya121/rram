namespace RRaM.Core.Board
{
    /// <summary>
    /// Describes special gameplay markers authored on top of the map texture.
    /// </summary>
    public enum BoardNodeKind
    {
        Normal = 0,
        GreenDeck = 1,
        RedDeck = 2,
        Teleport = 3,
        Custom = 4
    }
}
