using System.Collections.Generic;
using Mirror;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Networking
{
    /// <summary>
    /// Network-owned player session object used for lobby and gameplay requests.
    /// </summary>
    public sealed class NetworkPlayerConnection : NetworkBehaviour
    {
        public readonly struct ChatEntry
        {
            public ChatEntry(int senderSlot, string senderName, string message)
            {
                SenderSlot = senderSlot;
                SenderName = senderName;
                Message = message;
            }

            public int SenderSlot { get; }
            public string SenderName { get; }
            public string Message { get; }
        }

        public sealed class SyncCardList : SyncList<CardSnapshot> { }
        public sealed class SyncCharacterList : SyncList<CharacterSnapshot> { }

        public const int MaxChatMessageLength = 160;

        [SyncVar] public int PlayerSlot;
        [SyncVar] public string DisplayName;
        [SyncVar(hook = nameof(OnReadyChanged))] public bool IsReady;
        [SyncVar] public uint SelectedCharacterNetId;

        private const int MaxChatHistoryEntries = 32;
        private static readonly List<ChatEntry> chatHistory = new();

        public readonly SyncCardList Cards = new();
        public readonly SyncCharacterList Characters = new();

        public bool HasSelectedCharacter => SelectedCharacterNetId != 0;
        public static IReadOnlyList<ChatEntry> ChatHistory => chatHistory;


        /// <summary>
        /// Sets initial server-owned player data.
        /// </summary>
        [Server]
        public void AssignServerState(int playerSlot, string displayName)
        {
            PlayerSlot = playerSlot;
            DisplayName = displayName;
        }

        /// <summary>
        /// Synchronizes the player's visible card list.
        /// </summary>
        [Server]
        public void SetCardSnapshots(System.Collections.Generic.IReadOnlyList<CardSnapshot> snapshots)
        {
            Cards.Clear();
            for (int i = 0; i < snapshots.Count; i++)
            {
                Cards.Add(snapshots[i]);
            }
        }

        /// <summary>
        /// Synchronizes the player's visible character roster.
        /// </summary>
        [Server]
        public void SetCharacterSnapshots(System.Collections.Generic.IReadOnlyList<CharacterSnapshot> snapshots)
        {
            Characters.Clear();
            for (int i = 0; i < snapshots.Count; i++)
            {
                Characters.Add(snapshots[i]);
            }
        }

        /// <summary>
        /// Updates the currently selected character netId.
        /// </summary>
        [Server]
        public void SetSelectedCharacter(uint netId)
        {
            SelectedCharacterNetId = netId;
        }

        /// <summary>
        /// Clears lobby-owned state before a fresh session starts.
        /// </summary>
        [Server]
        public void ServerResetLobbyState()
        {
            IsReady = false;
            SelectedCharacterNetId = 0;
            Cards.Clear();
            Characters.Clear();
        }

        /// <summary>
        /// Handles client-side initialization for every visible player session.
        /// </summary>
        public override void OnStartClient() { }

        /// <summary>
        /// Binds the local-only HUD controller when this is the local player.
        /// </summary>
        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            ResetLocalChatState();
            LocalPlayerController controller = GetComponent<LocalPlayerController>();
            if (controller != null)
            {
                controller.Initialize(this);
            }
        }

        /// <summary>
        /// Requests a ready state change.
        /// </summary>
        [Command]
        public void CmdSetReady(bool isReady)
        {
            IsReady = isReady;
            Match.MatchManager.Instance?.ServerNotifyReadyStateChanged(this);
        }

        /// <summary>
        /// Requests selection of an owned character.
        /// </summary>
        [Command]
        public void CmdSelectCharacter(uint characterNetId)
        {
            global::RRaM.Core.Characters.CharacterManager.Instance?.ServerTrySelectCharacter(this, characterNetId);
        }

        /// <summary>
        /// Requests selection of an owned character standing on the given board node.
        /// </summary>
        [Command]
        public void CmdSelectCharacterAtNode(string nodeId)
        {
            global::RRaM.Core.Characters.CharacterManager.Instance?.ServerTrySelectCharacterAtNode(this, nodeId);
        }

        /// <summary>
        /// Requests a server-side roll of two dice.
        /// </summary>
        [Command]
        public void CmdRollDice()
        {
            Dice.DiceManager.Instance?.ServerRollForCurrentPlayer(this);
        }

        /// <summary>
        /// Requests movement of the currently selected character.
        /// </summary>
        [Command]
        public void CmdMoveSelectedCharacter(string destinationNodeId, uint selectedCharacterNetId)
        {
            Debug.Log(
                $"[NetworkPlayerConnection] CmdMoveSelectedCharacter destination={destinationNodeId}, requestedCharacterNetId={selectedCharacterNetId}, serverSelectedCharacterNetId={SelectedCharacterNetId}, playerSlot={PlayerSlot}",
                this);
            global::RRaM.Core.Characters.CharacterManager.Instance?.ServerTryMoveSelectedCharacter(this, destinationNodeId, selectedCharacterNetId);
        }

        /// <summary>
        /// Requests drawing the next card from the shared deck.
        /// </summary>
        [Command]
        public void CmdDrawCard()
        {
            global::RRaM.Core.Cards.CardManager.Instance?.ServerTryDrawCard(this);
        }

        /// <summary>
        /// Requests use of a card from the local hand.
        /// </summary>
        [Command]
        public void CmdUseCard(uint cardNetId)
        {
            global::RRaM.Core.Cards.CardManager.Instance?.ServerTryUseOwnedCard(this, cardNetId);
        }

        /// <summary>
        /// Requests discarding a card from the local hand.
        /// </summary>
        [Command]
        public void CmdDiscardCard(uint cardNetId)
        {
            global::RRaM.Core.Cards.CardManager.Instance?.ServerTryDiscardOwnedCard(this, cardNetId);
        }

        /// <summary>
        /// Requests turn completion on the server.
        /// </summary>
        [Command]
        public void CmdEndTurn()
        {
            Turns.TurnManager.Instance?.ServerTryEndTurn(this);
        }

        [Server]
        public bool ServerForceEndTurn()
        {
            return Turns.TurnManager.Instance != null && Turns.TurnManager.Instance.ServerForceEndTurn(this);
        }

        /// <summary>
        /// Requests broadcasting a chat message to all connected clients.
        /// </summary>
        [Command]
        public void CmdSendChatMessage(string rawMessage)
        {
            string message = SanitizeChatMessage(rawMessage);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string senderName = string.IsNullOrWhiteSpace(DisplayName)
                ? $"Игрок {PlayerSlot + 1}"
                : DisplayName.Trim();
            RpcReceiveChatMessage(PlayerSlot, senderName, message);
        }

        public override void OnStopClient()
        {
            if (isLocalPlayer)
            {
                ResetLocalChatState();
            }

            base.OnStopClient();
        }

        private void OnReadyChanged(bool oldValue, bool newValue) { }

        public static void ResetLocalChatState()
        {
            chatHistory.Clear();
        }

        [ClientRpc]
        private void RpcReceiveChatMessage(int senderSlot, string senderName, string message)
        {
            if (chatHistory.Count >= MaxChatHistoryEntries)
            {
                chatHistory.RemoveAt(0);
            }

            chatHistory.Add(new ChatEntry(senderSlot, senderName, message));
        }

        private static string SanitizeChatMessage(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return string.Empty;
            }

            string sanitized = rawMessage.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (sanitized.Length > MaxChatMessageLength)
            {
                sanitized = sanitized[..MaxChatMessageLength];
            }

            return sanitized;
        }
    }
}
