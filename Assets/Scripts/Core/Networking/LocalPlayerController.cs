using Mirror;
using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Networking
{
    /// <summary>
    /// Provides a local-only entry point for HUD actions.
    /// </summary>
    public sealed class LocalPlayerController : MonoBehaviour
    {
        public static LocalPlayerController Instance { get; private set; }

        public NetworkPlayerConnection Player { get; private set; }
        public string RequestedAddress { get; set; } = "localhost";
        public uint PredictedSelectedCharacterNetId { get; private set; }
        public CharacterSnapshot PredictedSelectedCharacter { get; private set; }

        public uint EffectiveSelectedCharacterNetId
        {
            get
            {
                if (Player == null)
                {
                    return PredictedSelectedCharacterNetId;
                }

                if (Player.SelectedCharacterNetId != 0)
                {
                    PredictedSelectedCharacterNetId = Player.SelectedCharacterNetId;
                }

                return Player.SelectedCharacterNetId != 0
                    ? Player.SelectedCharacterNetId
                    : PredictedSelectedCharacterNetId;
            }
        }

        /// <summary>
        /// Binds this helper to the spawned local player object.
        /// </summary>
        public void Initialize(NetworkPlayerConnection player)
        {
            Player = player;
            PredictedSelectedCharacterNetId = player != null ? player.SelectedCharacterNetId : 0;
            PredictedSelectedCharacter = default;
            Instance = this;
        }

        /// <summary>
        /// Sends a ready state change to the server.
        /// </summary>
        public void SetReady(bool isReady)
        {
            Player?.CmdSetReady(isReady);
        }

        /// <summary>
        /// Sends a character selection request to the server.
        /// </summary>
        public void SelectCharacter(uint characterNetId)
        {
            PredictedSelectedCharacterNetId = characterNetId;
            PredictedSelectedCharacter = ResolveOwnedCharacterSnapshot(characterNetId);
            Player?.CmdSelectCharacter(characterNetId);
        }

        /// <summary>
        /// Sends a character selection request by board node.
        /// </summary>
        public void SelectCharacterAtNode(string nodeId, uint predictedCharacterNetId = 0, CharacterSnapshot predictedCharacter = default)
        {
            if (predictedCharacterNetId != 0)
            {
                PredictedSelectedCharacterNetId = predictedCharacterNetId;
            }

            if (predictedCharacter.NetId != 0)
            {
                PredictedSelectedCharacter = predictedCharacter;
            }

            Player?.CmdSelectCharacterAtNode(nodeId);
        }

        public CharacterSnapshot ResolveOwnedCharacterSnapshot(uint characterNetId)
        {
            if (Player == null || characterNetId == 0)
            {
                return default;
            }

            for (int i = 0; i < Player.Characters.Count; i++)
            {
                CharacterSnapshot candidate = Player.Characters[i];
                if (candidate.NetId == characterNetId)
                {
                    return candidate;
                }
            }

            return default;
        }

        /// <summary>
        /// Sends a dice roll request to the server.
        /// </summary>
        public void RollDice()
        {
            Player?.CmdRollDice();
        }

        /// <summary>
        /// Sends a movement request to the server.
        /// </summary>
        public void MoveSelectedCharacter(string destinationNodeId)
        {
            uint selectedCharacterNetId = EffectiveSelectedCharacterNetId;
            Debug.Log(
                $"[LocalPlayerController] MoveSelectedCharacter destination={destinationNodeId}, selectedCharacterNetId={selectedCharacterNetId}, player={(Player != null ? Player.name : "null")}",
                this);
            Player?.CmdMoveSelectedCharacter(destinationNodeId, selectedCharacterNetId);
        }

        /// <summary>
        /// Requests drawing the next card from the shared deck.
        /// </summary>
        public void DrawCard()
        {
            Player?.CmdDrawCard();
        }

        /// <summary>
        /// Sends a card play request to the server.
        /// </summary>
        public void UseCard(uint cardNetId)
        {
            Player?.CmdUseCard(cardNetId);
        }

        /// <summary>
        /// Requests discarding a card from the local hand.
        /// </summary>
        public void DiscardCard(uint cardNetId)
        {
            Player?.CmdDiscardCard(cardNetId);
        }

        /// <summary>
        /// Sends an end turn request to the server.
        /// </summary>
        public void EndTurn()
        {
            Player?.CmdEndTurn();
        }

        /// <summary>
        /// Sends a chat message to the server for broadcast.
        /// </summary>
        public void SendChatMessage(string message)
        {
            Player?.CmdSendChatMessage(message);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
