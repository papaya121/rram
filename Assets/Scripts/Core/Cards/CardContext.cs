using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Networking;
using RRaM.Core.Turns;

namespace RRaM.Core.Cards
{
    public sealed class CardContext
    {
        public NetworkPlayerConnection player;
        public NetworkCharacterPawn character;
        public DiceManager diceSystem;
        public BoardGraph board;
        public NetworkIdentity owner;
        public TurnManager turnManager;
        public CardManager cardManager;
        public CardInstance cardInstance;
    }
}
