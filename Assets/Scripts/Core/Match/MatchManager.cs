using System.Collections.Generic;
using Mirror;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using RRaM.Core.Dice;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Match
{
    /// <summary>
    /// Coordinates lobby readiness and match state transitions.
    /// </summary>
    public sealed class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [SyncVar] public MatchState State = MatchState.Bootstrapping;
        [SyncVar] public int StarterTurnsElapsed;

        private readonly List<Networking.NetworkPlayerConnection> players = new();
        private TurnManager turnManager;
        private CharacterManager characterManager;
        private CardManager cardManager;
        private DiceManager diceManager;
        private Dwarfs.DwarfManager dwarfManager;

        public IReadOnlyList<Networking.NetworkPlayerConnection> Players => players;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Match] Duplicate match manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            turnManager = GetComponent<TurnManager>();
            characterManager = GetComponent<CharacterManager>();
            cardManager = GetComponent<CardManager>();
            diceManager = GetComponent<DiceManager>();
            dwarfManager = GetComponent<Dwarfs.DwarfManager>();
        }

        /// <summary>
        /// Prepares the service root for a new lobby.
        /// </summary>
        [Server]
        public void ServerBootstrapLobby()
        {
            if (!TryValidateServices(out string validationError))
            {
                Debug.LogError($"[Match] Failed to bootstrap lobby. {validationError}", this);
                return;
            }

            turnManager.ServerTurnCompleted -= OnServerTurnCompleted;
            turnManager.ServerTurnCompleted += OnServerTurnCompleted;
            ServerResetSessionState();
        }

        /// <summary>
        /// Registers a newly connected player session.
        /// </summary>
        [Server]
        public void ServerRegisterPlayer(Networking.NetworkPlayerConnection player)
        {
            if (player == null || players.Contains(player))
            {
                return;
            }

            players.Add(player);
            players.Sort((a, b) => a.PlayerSlot.CompareTo(b.PlayerSlot));
            if (players.Count >= 2)
            {
                ServerStartFreshSession();
            }
            else
            {
                ServerResetSessionState();
            }
        }

        /// <summary>
        /// Removes a disconnected player session.
        /// </summary>
        [Server]
        public void ServerUnregisterPlayer(Networking.NetworkPlayerConnection player)
        {
            if (player == null)
            {
                return;
            }

            players.Remove(player);
            ServerResetSessionState();
        }

        /// <summary>
        /// Re-evaluates the lobby ready state.
        /// </summary>
        [Server]
        public void ServerNotifyReadyStateChanged(Networking.NetworkPlayerConnection sourcePlayer)
        {
        }

        /// <summary>
        /// Starts a clean match as soon as both player slots are occupied.
        /// </summary>
        [Server]
        public bool ServerStartFreshSession()
        {
            if (players.Count != 2 || players[0] == null || players[1] == null)
            {
                return false;
            }

            if (!TryValidateServices(out string validationError))
            {
                Debug.LogError($"[Match] Failed to start session. {validationError}", this);
                ServerResetSessionState();
                return false;
            }

            ServerResetSessionState();
            State = MatchState.Starting;
            StarterTurnsElapsed = 0;
            Board.BoardGraph.Instance?.EnsureInitialized();
            characterManager.ServerSpawnStarterCharacters(players);
            cardManager.ServerInitialize(players);
            turnManager.ServerInitialize(players, MatchContext.Instance != null && MatchContext.Instance.Config != null
                ? MatchContext.Instance.Config.SetupTurnsPerPlayer
                : 10);
            State = MatchState.PlayerTurn;
            RpcMatchStarted(turnManager.CurrentPlayerSlot);
            return true;
        }

        [Server]
        private void ServerResetSessionState()
        {
            State = MatchState.Lobby;
            StarterTurnsElapsed = 0;

            for (int i = 0; i < players.Count; i++)
            {
                players[i]?.ServerResetLobbyState();
            }

            turnManager?.ServerResetState();
            diceManager?.ServerResetState();
            characterManager?.ServerResetState(players);
            cardManager?.ServerResetState(players);
            dwarfManager?.ServerResetState();
        }

        private void OnServerTurnCompleted(int completedTurns)
        {
            if (turnManager == null || dwarfManager == null)
            {
                return;
            }

            if (turnManager.IsSetupPhase)
            {
                StarterTurnsElapsed = completedTurns;
                if (!turnManager.AreSetupTurnsFinished())
                {
                    State = MatchState.PlayerTurn;
                    return;
                }

                State = MatchState.ResolvingDwarfs;
                int dwarfTurns = MatchContext.Instance != null && MatchContext.Instance.Config != null
                    ? MatchContext.Instance.Config.DwarfTurnsAfterSetup
                    : 10;
                dwarfManager.ServerResolveSetupPhaseCompleted(dwarfTurns);
                turnManager.ServerBeginAlternatingPhase();
                State = MatchState.PlayerTurn;
                return;
            }

            StarterTurnsElapsed = completedTurns;
            State = MatchState.PlayerTurn;
        }

        private void OnDestroy()
        {
            if (turnManager != null)
            {
                turnManager.ServerTurnCompleted -= OnServerTurnCompleted;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        [ClientRpc]
        private void RpcMatchStarted(int currentPlayerSlot) { }

        private bool TryValidateServices(out string validationError)
        {
            if (turnManager == null)
            {
                validationError = $"{nameof(TurnManager)} component is missing.";
                return false;
            }

            if (characterManager == null)
            {
                validationError = $"{nameof(CharacterManager)} component is missing.";
                return false;
            }

            if (cardManager == null)
            {
                validationError = $"{nameof(CardManager)} component is missing.";
                return false;
            }

            if (diceManager == null)
            {
                validationError = $"{nameof(DiceManager)} component is missing.";
                return false;
            }

            if (dwarfManager == null)
            {
                validationError = $"{nameof(Dwarfs.DwarfManager)} component is missing.";
                return false;
            }

            MatchContext context = MatchContext.Instance;
            if (context == null || context.Config == null)
            {
                validationError = $"{nameof(MatchContext)} or match config is missing.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }
    }
}
