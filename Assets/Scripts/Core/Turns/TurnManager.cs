using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Turns
{
    /// <summary>
    /// Owns player turn order and turn phase transitions.
    /// </summary>
    public sealed class TurnManager : NetworkBehaviour
    {
        public sealed class SyncDeckNodeIdSet : SyncHashSet<string> { }

        public static TurnManager Instance { get; private set; }

        [SyncVar] public TurnMode CurrentMode = TurnMode.Setup;
        [SyncVar] public int CurrentPlayerSlot = -1;
        [SyncVar] public int TurnNumber;
        [SyncVar] public TurnPhase CurrentPhase = TurnPhase.WaitingForRoll;
        [SyncVar] public int MoveBonus;
        [SyncVar] public int RemainingMovePoints;
        [SyncVar] public int RemainingDieActions;
        [SyncVar] public bool DieAAvailable;
        [SyncVar] public bool DieBAvailable;
        [SyncVar] public int RemainingCardTransfers;
        [SyncVar] public bool HasMovedThisTurn;
        [SyncVar] public int CompletedTurnCount;
        [SyncVar] public int ActiveActionPlayerSlot = -1;
        [SyncVar] public uint ActiveCharacterNetId;
        [SyncVar] public int LastConsumedDieValue;
        [SyncVar] public int SetupTurnsPerPlayer;
        [SyncVar] private int playerZeroSetupTurnsCompleted;
        [SyncVar] private int playerOneSetupTurnsCompleted;
        [SyncVar] private TurnPhase playerZeroSetupPhase = TurnPhase.WaitingForRoll;
        [SyncVar] private TurnPhase playerOneSetupPhase = TurnPhase.WaitingForRoll;
        [SyncVar] private int playerZeroSetupMoveBonus;
        [SyncVar] private int playerOneSetupMoveBonus;
        [SyncVar] private int playerZeroSetupRemainingMovePoints;
        [SyncVar] private int playerOneSetupRemainingMovePoints;
        [SyncVar] private int playerZeroSetupRemainingDieActions;
        [SyncVar] private int playerOneSetupRemainingDieActions;
        [SyncVar] private bool playerZeroSetupDieAAvailable;
        [SyncVar] private bool playerOneSetupDieAAvailable;
        [SyncVar] private bool playerZeroSetupDieBAvailable;
        [SyncVar] private bool playerOneSetupDieBAvailable;
        [SyncVar] private int playerZeroSetupRemainingCardTransfers;
        [SyncVar] private int playerOneSetupRemainingCardTransfers;
        [SyncVar] private bool playerZeroSetupHasMoved;
        [SyncVar] private bool playerOneSetupHasMoved;
        [SyncVar] private uint playerZeroSetupActiveCharacterNetId;
        [SyncVar] private uint playerOneSetupActiveCharacterNetId;
        [SyncVar] private int playerZeroSetupLastConsumedDieValue;
        [SyncVar] private int playerOneSetupLastConsumedDieValue;

        public event Action<int> ServerTurnCompleted;

        private readonly List<int> orderedPlayerSlots = new();
        private readonly SyncDeckNodeIdSet drawnDeckNodeIdsThisTurn = new();
        private readonly SyncDeckNodeIdSet playerZeroSetupDrawnDeckNodeIdsThisTurn = new();
        private readonly SyncDeckNodeIdSet playerOneSetupDrawnDeckNodeIdsThisTurn = new();
        private int currentPlayerIndex;
        private int starterPlayerIndex;

        public TurnStateSnapshot Snapshot => new(TurnNumber, CurrentPlayerSlot, CurrentPhase, HasMovedThisTurn);

        private struct TurnActionState
        {
            public TurnPhase Phase;
            public int MoveBonus;
            public int RemainingMovePoints;
            public int RemainingDieActions;
            public bool DieAAvailable;
            public bool DieBAvailable;
            public int RemainingCardTransfers;
            public bool HasMovedThisTurn;
            public uint ActiveCharacterNetId;
            public int LastConsumedDieValue;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Turns] Duplicate turn manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Starts the turn loop for the given players.
        /// </summary>
        [Server]
        public void ServerInitialize(IReadOnlyList<NetworkPlayerConnection> players, int setupTurnsPerPlayer)
        {
            ServerResetState();

            if (players == null)
            {
                return;
            }

            orderedPlayerSlots.AddRange(players
                .Where(player => player != null)
                .Select(player => player.PlayerSlot)
                .Distinct()
                .OrderBy(slot => slot));

            starterPlayerIndex = orderedPlayerSlots.Count > 0 ? UnityEngine.Random.Range(0, orderedPlayerSlots.Count) : 0;
            currentPlayerIndex = starterPlayerIndex;
            SetupTurnsPerPlayer = Mathf.Max(1, setupTurnsPerPlayer);
            playerZeroSetupTurnsCompleted = 0;
            playerOneSetupTurnsCompleted = 0;
            ResetSetupActionStates();
            CurrentMode = TurnMode.Setup;
            TurnNumber = 1;
            CurrentPlayerSlot = -1;
            ActiveActionPlayerSlot = -1;
            CurrentPhase = TurnPhase.WaitingForRoll;
            ActiveCharacterNetId = 0;
            RemainingMovePoints = 0;
            drawnDeckNodeIdsThisTurn.Clear();
            playerZeroSetupDrawnDeckNodeIdsThisTurn.Clear();
            playerOneSetupDrawnDeckNodeIdsThisTurn.Clear();
            Dice.DiceManager.Instance?.ServerResetTurn();
            ClearPlayerSelections(players);
        }

        /// <summary>
        /// Returns the turn system to its idle lobby state.
        /// </summary>
        [Server]
        public void ServerResetState()
        {
            orderedPlayerSlots.Clear();
            currentPlayerIndex = 0;
            starterPlayerIndex = 0;
            CurrentMode = TurnMode.Setup;
            CurrentPlayerSlot = -1;
            TurnNumber = 0;
            CurrentPhase = TurnPhase.WaitingForRoll;
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            DieAAvailable = false;
            DieBAvailable = false;
            RemainingCardTransfers = 0;
            HasMovedThisTurn = false;
            CompletedTurnCount = 0;
            ActiveActionPlayerSlot = -1;
            ActiveCharacterNetId = 0;
            LastConsumedDieValue = 0;
            SetupTurnsPerPlayer = 0;
            playerZeroSetupTurnsCompleted = 0;
            playerOneSetupTurnsCompleted = 0;
            ResetSetupActionStates();
            drawnDeckNodeIdsThisTurn.Clear();
            playerZeroSetupDrawnDeckNodeIdsThisTurn.Clear();
            playerOneSetupDrawnDeckNodeIdsThisTurn.Clear();
        }

        /// <summary>
        /// Returns true if this player may act in the current mode.
        /// </summary>
        [Server]
        public bool ServerIsCurrentPlayer(NetworkPlayerConnection player)
        {
            return player != null && CanPlayerAct(player.PlayerSlot);
        }

        /// <summary>
        /// Marks the dice roll phase as completed.
        /// </summary>
        [Server]
        public void ServerOnDiceRolled(NetworkPlayerConnection player)
        {
            if (player == null)
            {
                return;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                TurnActionState state = LoadActionState(player.PlayerSlot);
                state.DieAAvailable = true;
                state.DieBAvailable = true;
                RefreshRemainingDieActions(ref state);
                state.RemainingCardTransfers = 0;
                state.RemainingMovePoints = ResolveAvailableMoveBudget(player.PlayerSlot, state);
                state.Phase = TurnPhase.WaitingForMove;
                SaveActionState(player.PlayerSlot, state);
                ActiveActionPlayerSlot = player.PlayerSlot;
                return;
            }

            if (player.PlayerSlot == CurrentPlayerSlot)
            {
                ActiveActionPlayerSlot = player.PlayerSlot;
            }

            DieAAvailable = true;
            DieBAvailable = true;
            RefreshRemainingDieActions();
            RemainingCardTransfers = 0;
            RemainingMovePoints = ResolveAvailableMoveBudget(CurrentPlayerSlot, LoadActionState(CurrentPlayerSlot));
            CurrentPhase = TurnPhase.WaitingForMove;
        }

        /// <summary>
        /// Adds temporary move points for the active player.
        /// </summary>
        [Server]
        public bool ServerAddMoveBonus(int playerSlot, int amount)
        {
            TurnActionState state = LoadActionState(playerSlot);
            if (!CanPlayerModifyCurrentAction(playerSlot) || amount <= 0 || state.HasMovedThisTurn)
            {
                return false;
            }

            state.MoveBonus += amount;
            if (state.Phase == TurnPhase.WaitingForMove)
            {
                state.RemainingMovePoints += amount;
            }

            SaveActionState(playerSlot, state);
            return true;
        }

        /// <summary>
        /// Returns the movement budget for the current turn.
        /// </summary>
        public int GetCurrentMoveBudget()
        {
            return GetCurrentMoveBudget(CurrentPlayerSlot);
        }

        /// <summary>
        /// Returns the movement budget that can be spent while keeping the second die for non-movement actions.
        /// </summary>
        public int GetPrimaryMoveBudget()
        {
            return GetPrimaryMoveBudget(CurrentPlayerSlot);
        }

        /// <summary>
        /// Returns movement points still available for the current action.
        /// </summary>
        public int GetRemainingMoveBudget()
        {
            return GetRemainingMoveBudget(CurrentPlayerSlot);
        }

        public int GetRemainingMoveBudget(int playerSlot)
        {
            return Mathf.Max(0, LoadActionState(playerSlot).RemainingMovePoints);
        }

        public int GetCurrentMoveBudget(int playerSlot)
        {
            TurnActionState state = LoadActionState(playerSlot);
            return state.HasMovedThisTurn
                ? Mathf.Max(0, state.RemainingMovePoints)
                : GetAvailableDiceTotal(playerSlot, state) + state.MoveBonus;
        }

        public int GetPrimaryMoveBudget(int playerSlot)
        {
            TurnActionState state = LoadActionState(playerSlot);
            return state.HasMovedThisTurn
                ? 0
                : GetSingleDieMoveBudget(playerSlot, state) + state.MoveBonus;
        }

        /// <summary>
        /// Returns the number of unspent die-based actions in the current turn.
        /// </summary>
        public int GetRemainingDieActions()
        {
            return GetRemainingDieActions(CurrentPlayerSlot);
        }

        public int GetRemainingDieActions(int playerSlot)
        {
            return Mathf.Max(0, LoadActionState(playerSlot).RemainingDieActions);
        }

        /// <summary>
        /// Returns how many card transfers remain from the currently spent transfer die.
        /// </summary>
        public int GetRemainingCardTransfers()
        {
            return GetRemainingCardTransfers(CurrentPlayerSlot);
        }

        public int GetRemainingCardTransfers(int playerSlot)
        {
            return Mathf.Max(0, LoadActionState(playerSlot).RemainingCardTransfers);
        }

        public int GetMoveBonus(int playerSlot)
        {
            return LoadActionState(playerSlot).MoveBonus;
        }

        public TurnPhase GetCurrentPhase(int playerSlot)
        {
            return LoadActionState(playerSlot).Phase;
        }

        public bool HasPlayerMovedThisTurn(int playerSlot)
        {
            return LoadActionState(playerSlot).HasMovedThisTurn;
        }

        public uint GetActiveCharacterNetId(int playerSlot)
        {
            return LoadActionState(playerSlot).ActiveCharacterNetId;
        }

        /// <summary>
        /// Returns true if the player may spend one die on a non-movement action.
        /// </summary>
        public bool CanPlayerSpendDieAction(int playerSlot)
        {
            TurnActionState state = LoadActionState(playerSlot);
            return CanPlayerModifyCurrentAction(playerSlot) &&
                   state.Phase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolledThisTurn(playerSlot) &&
                   state.RemainingDieActions > 0;
        }

        public bool CanPlayerSpendDieActionWithMinimum(int playerSlot, int minimumDieValue)
        {
            return CanPlayerSpendDieAction(playerSlot) &&
                   HasAvailableDieAtLeast(playerSlot, LoadActionState(playerSlot), Mathf.Max(1, minimumDieValue));
        }

        public int GetLastConsumedDieValue(int playerSlot)
        {
            return Mathf.Max(0, LoadActionState(playerSlot).LastConsumedDieValue);
        }

        /// <summary>
        /// Consumes one or more die actions for card draw/use style actions.
        /// </summary>
        [Server]
        public bool ServerTryConsumeDieAction(int playerSlot, int amount = 1)
        {
            TurnActionState state = LoadActionState(playerSlot);
            if (amount <= 0 || !CanPlayerSpendDieAction(playerSlot) || state.RemainingDieActions < amount)
            {
                return false;
            }

            state.RemainingCardTransfers = 0;
            state.LastConsumedDieValue = 0;
            for (int i = 0; i < amount; i++)
            {
                if (!TryConsumeAvailableDie(playerSlot, ref state, DieConsumptionPreference.Smallest, out int dieValue))
                {
                    return false;
                }

                state.LastConsumedDieValue += dieValue;
            }

            ApplyPostDieConsumptionState(playerSlot, ref state);
            SaveActionState(playerSlot, state);
            return true;
        }

        [Server]
        public bool ServerTryConsumeDieActionWithMinimum(int playerSlot, int minimumDieValue)
        {
            if (!CanPlayerSpendDieActionWithMinimum(playerSlot, minimumDieValue))
            {
                return false;
            }

            TurnActionState state = LoadActionState(playerSlot);
            state.RemainingCardTransfers = 0;
            if (!TryConsumeAvailableDieAtLeast(playerSlot, ref state, Mathf.Max(1, minimumDieValue), out int dieValue))
            {
                return false;
            }

            state.LastConsumedDieValue = dieValue;
            ApplyPostDieConsumptionState(playerSlot, ref state);
            SaveActionState(playerSlot, state);
            return true;
        }

        /// <summary>
        /// Returns true if the player can transfer a card from the active character this turn.
        /// </summary>
        public bool CanPlayerTransferCard(int playerSlot)
        {
            TurnActionState state = LoadActionState(playerSlot);
            return CanPlayerModifyCurrentAction(playerSlot) &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolledThisTurn(playerSlot) &&
                   (state.RemainingCardTransfers > 0 ||
                    (state.Phase == TurnPhase.WaitingForMove && state.RemainingDieActions > 0));
        }

        /// <summary>
        /// Consumes one card-transfer allowance, spending the largest available die when needed.
        /// </summary>
        [Server]
        public bool ServerTryConsumeCardTransfer(int playerSlot)
        {
            if (!CanPlayerTransferCard(playerSlot))
            {
                return false;
            }

            TurnActionState state = LoadActionState(playerSlot);
            if (state.RemainingCardTransfers <= 0)
            {
                if (!CanPlayerSpendDieAction(playerSlot) ||
                    !TryConsumeAvailableDie(playerSlot, ref state, DieConsumptionPreference.Largest, out int dieValue))
                {
                    return false;
                }

                state.LastConsumedDieValue = dieValue;
                state.RemainingCardTransfers = Mathf.Max(0, dieValue);
                RefreshRemainingDieActions(ref state);
                state.RemainingMovePoints = !state.HasMovedThisTurn ? ResolveAvailableMoveBudget(playerSlot, state) : 0;
            }

            state.RemainingCardTransfers = Mathf.Max(0, state.RemainingCardTransfers - 1);
            if (state.RemainingDieActions <= 0 && state.RemainingCardTransfers <= 0)
            {
                state.RemainingMovePoints = 0;
                state.Phase = TurnPhase.WaitingForEndTurn;
            }

            SaveActionState(playerSlot, state);
            return true;
        }

        /// <summary>
        /// Locks the current turn to one character before resolving a die-backed action.
        /// </summary>
        [Server]
        public bool ServerTryUseCharacterForCurrentTurn(int playerSlot, uint characterNetId)
        {
            TurnActionState state = LoadActionState(playerSlot);
            if (characterNetId == 0 || !CanPlayerAct(playerSlot) || state.Phase != TurnPhase.WaitingForMove)
            {
                return false;
            }

            bool isContinuingMovement =
                state.HasMovedThisTurn &&
                state.ActiveCharacterNetId == characterNetId &&
                state.RemainingMovePoints > 0;
            if (isContinuingMovement)
            {
                return true;
            }

            if (!CanPlayerSpendDieAction(playerSlot) || !CanPlayerSelectCharacter(playerSlot, characterNetId))
            {
                return false;
            }

            if (state.ActiveCharacterNetId == 0)
            {
                state.ActiveCharacterNetId = characterNetId;
                SaveActionState(playerSlot, state);
            }

            return state.ActiveCharacterNetId == characterNetId;
        }

        /// <summary>
        /// Consumes one die for a deck draw and remembers the deck point used this turn.
        /// </summary>
        [Server]
        public bool ServerTryConsumeDeckDrawAction(int playerSlot, string deckNodeId)
        {
            string normalizedDeckNodeId = NormalizeDeckNodeId(deckNodeId);
            if (string.IsNullOrEmpty(normalizedDeckNodeId) || HasDrawnFromDeckNode(playerSlot, normalizedDeckNodeId))
            {
                return false;
            }

            if (!ServerTryConsumeDieAction(playerSlot))
            {
                return false;
            }

            GetDrawnDeckNodeIds(playerSlot).Add(normalizedDeckNodeId);
            return true;
        }

        /// <summary>
        /// Consumes movement points for a validated path.
        /// </summary>
        [Server]
        public bool ServerConsumeMovement(int usedSteps)
        {
            int playerSlot = ResolveActivePlayerSlot();
            if (usedSteps <= 0 || !CanPlayerMove(playerSlot) || usedSteps > GetRemainingMoveBudget(playerSlot))
            {
                return false;
            }

            return ServerConsumeMovement(playerSlot, usedSteps);
        }

        [Server]
        public bool ServerConsumeMovement(int playerSlot, int usedSteps)
        {
            if (usedSteps <= 0 || !CanPlayerMove(playerSlot))
            {
                return false;
            }

            TurnActionState state = LoadActionState(playerSlot);
            if (usedSteps > state.RemainingMovePoints)
            {
                return false;
            }

            state.RemainingCardTransfers = 0;
            if (state.HasMovedThisTurn)
            {
                state.RemainingMovePoints = Mathf.Max(0, state.RemainingMovePoints - usedSteps);
            }
            else if (TryConsumeMovementDie(playerSlot, ref state, usedSteps, out int dieValue))
            {
                state.LastConsumedDieValue = dieValue;
                state.RemainingMovePoints = Mathf.Max(0, dieValue + state.MoveBonus - usedSteps);
            }
            else
            {
                int diceTotal = GetAvailableDiceTotal(playerSlot, state);
                if (usedSteps > diceTotal + state.MoveBonus || !TryConsumeAllAvailableDice(ref state))
                {
                    return false;
                }

                state.LastConsumedDieValue = diceTotal;
                state.RemainingMovePoints = Mathf.Max(0, diceTotal + state.MoveBonus - usedSteps);
                RefreshRemainingDieActions(ref state);
            }

            state.HasMovedThisTurn = true;

            if (state.RemainingMovePoints <= 0 && state.RemainingDieActions <= 0)
            {
                state.Phase = TurnPhase.WaitingForEndTurn;
                SaveActionState(playerSlot, state);
                return true;
            }

            state.Phase = TurnPhase.WaitingForMove;
            SaveActionState(playerSlot, state);
            return true;
        }

        /// <summary>
        /// Switches the match from setup turns into classic alternating turns.
        /// </summary>
        [Server]
        public void ServerBeginAlternatingPhase()
        {
            CurrentMode = TurnMode.Alternating;
            currentPlayerIndex = Mathf.Clamp(starterPlayerIndex, 0, Mathf.Max(0, orderedPlayerSlots.Count - 1));
            CurrentPlayerSlot = orderedPlayerSlots.Count > 0 ? orderedPlayerSlots[currentPlayerIndex] : -1;
            ActiveActionPlayerSlot = CurrentPlayerSlot;
            CurrentPhase = TurnPhase.WaitingForRoll;
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            DieAAvailable = false;
            DieBAvailable = false;
            RemainingCardTransfers = 0;
            HasMovedThisTurn = false;
            ActiveCharacterNetId = 0;
            LastConsumedDieValue = 0;
            drawnDeckNodeIdsThisTurn.Clear();
            playerZeroSetupDrawnDeckNodeIdsThisTurn.Clear();
            playerOneSetupDrawnDeckNodeIdsThisTurn.Clear();
            Dice.DiceManager.Instance?.ServerResetTurn();
        }

        /// <summary>
        /// Attempts to finish the current turn.
        /// </summary>
        [Server]
        public bool ServerTryEndTurn(NetworkPlayerConnection player)
        {
            return TryEndTurnInternal(player, force: false);
        }

        /// <summary>
        /// Forces the current action to end immediately.
        /// </summary>
        [Server]
        public bool ServerForceEndTurn(NetworkPlayerConnection player)
        {
            return TryEndTurnInternal(player, force: true);
        }

        [Server]
        private bool TryEndTurnInternal(NetworkPlayerConnection player, bool force)
        {
            if (player == null || orderedPlayerSlots.Count == 0)
            {
                return false;
            }

            bool setupFinishedAfterEnd = false;
            if (CurrentMode == TurnMode.Setup)
            {
                if (!CanPlayerAct(player.PlayerSlot))
                {
                    return false;
                }

                if (!force && !CanPlayerFinishSetupAction(player.PlayerSlot))
                {
                    return false;
                }

                RegisterSetupTurnCompleted(player.PlayerSlot);
                ResetActionState(player.PlayerSlot);
                GetDrawnDeckNodeIds(player.PlayerSlot).Clear();
                Dice.DiceManager.Instance?.ServerResetTurn(player.PlayerSlot);
                player.SetSelectedCharacter(0);
                setupFinishedAfterEnd = AreSetupTurnsFinished();
                if (setupFinishedAfterEnd)
                {
                    CurrentPlayerSlot = -1;
                    ActiveActionPlayerSlot = -1;
                }
            }
            else
            {
                if (player.PlayerSlot != CurrentPlayerSlot || (!force && !CanPlayerFinishAlternatingTurn(player.PlayerSlot)))
                {
                    return false;
                }

                currentPlayerIndex = (currentPlayerIndex + 1) % orderedPlayerSlots.Count;
                CurrentPlayerSlot = orderedPlayerSlots[currentPlayerIndex];
                ActiveActionPlayerSlot = CurrentPlayerSlot;
            }

            CompletedTurnCount++;
            TurnNumber++;
            if (CurrentMode != TurnMode.Setup)
            {
                MoveBonus = 0;
                RemainingMovePoints = 0;
                RemainingDieActions = 0;
                DieAAvailable = false;
                DieBAvailable = false;
                RemainingCardTransfers = 0;
                HasMovedThisTurn = false;
                ActiveCharacterNetId = 0;
                LastConsumedDieValue = 0;
            }

            CurrentPhase = CurrentMode == TurnMode.Setup && setupFinishedAfterEnd
                ? TurnPhase.WaitingForEndTurn
                : TurnPhase.WaitingForRoll;

            if (CurrentMode != TurnMode.Setup)
            {
                drawnDeckNodeIdsThisTurn.Clear();
                Dice.DiceManager.Instance?.ServerResetTurn();
                ClearPlayerSelections(Match.MatchManager.Instance?.Players);
            }

            ServerTurnCompleted?.Invoke(CompletedTurnCount);
            return true;
        }

        public bool IsSetupPhase => CurrentMode == TurnMode.Setup;

        public bool AreSetupTurnsFinished()
        {
            if (CurrentMode != TurnMode.Setup || SetupTurnsPerPlayer <= 0)
            {
                return false;
            }

            return !HasSetupTurnsRemaining(0) && !HasSetupTurnsRemaining(1);
        }

        public int GetRemainingSetupTurns(int playerSlot)
        {
            return Mathf.Max(0, SetupTurnsPerPlayer - GetSetupTurnsCompleted(playerSlot));
        }

        public int GetTotalRemainingSetupTurns()
        {
            return GetRemainingSetupTurns(0) + GetRemainingSetupTurns(1);
        }

        public bool CanPlayerAct(int playerSlot)
        {
            if (playerSlot < 0)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return HasSetupTurnsRemaining(playerSlot);
            }

            return playerSlot == CurrentPlayerSlot;
        }

        public bool CanPlayerRoll(int playerSlot)
        {
            return CanPlayerAct(playerSlot) && GetCurrentPhase(playerSlot) == TurnPhase.WaitingForRoll;
        }

        public bool CanPlayerMove(int playerSlot)
        {
            TurnActionState state = LoadActionState(playerSlot);
            return CanPlayerModifyCurrentAction(playerSlot) &&
                   state.Phase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolledThisTurn(playerSlot) &&
                   (state.RemainingDieActions > 0 || state.HasMovedThisTurn) &&
                   state.RemainingMovePoints > 0;
        }

        public bool CanPlayerEndTurn(int playerSlot)
        {
            if (!CanPlayerModifyCurrentAction(playerSlot))
            {
                return false;
            }

            TurnActionState state = LoadActionState(playerSlot);
            if (state.Phase == TurnPhase.WaitingForEndTurn)
            {
                return true;
            }

            return state.Phase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolledThisTurn(playerSlot);
        }

        public bool CanPlayerSelectCharacter(int playerSlot, uint characterNetId)
        {
            TurnActionState state = LoadActionState(playerSlot);
            if (characterNetId == 0 ||
                !CanPlayerAct(playerSlot) ||
                state.Phase != TurnPhase.WaitingForMove)
            {
                return false;
            }

            if (Dice.DiceManager.Instance == null || !Dice.DiceManager.Instance.HasRolledThisTurn(playerSlot))
            {
                return false;
            }

            return state.ActiveCharacterNetId == 0 || state.ActiveCharacterNetId == characterNetId;
        }

        public bool HasDrawnFromDeckNode(string deckNodeId)
        {
            return HasDrawnFromDeckNode(CurrentPlayerSlot, deckNodeId);
        }

        public bool HasDrawnFromDeckNode(int playerSlot, string deckNodeId)
        {
            string normalizedDeckNodeId = NormalizeDeckNodeId(deckNodeId);
            return !string.IsNullOrEmpty(normalizedDeckNodeId) &&
                   GetDrawnDeckNodeIds(playerSlot).Contains(normalizedDeckNodeId);
        }

        private bool CanPlayerModifyCurrentAction(int playerSlot)
        {
            if (playerSlot < 0)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return HasSetupTurnsRemaining(playerSlot);
            }

            return playerSlot == CurrentPlayerSlot;
        }

        [Server]
        private bool CanPlayerFinishSetupAction(int playerSlot)
        {
            return CurrentMode == TurnMode.Setup &&
                   HasSetupTurnsRemaining(playerSlot) &&
                   CanPlayerEndTurn(playerSlot);
        }

        [Server]
        private bool CanPlayerFinishAlternatingTurn(int playerSlot)
        {
            return playerSlot == CurrentPlayerSlot && CanPlayerEndTurn(playerSlot);
        }

        [Server]
        private void RegisterSetupTurnCompleted(int playerSlot)
        {
            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupTurnsCompleted = Mathf.Min(SetupTurnsPerPlayer, playerZeroSetupTurnsCompleted + 1);
                    break;
                case 1:
                    playerOneSetupTurnsCompleted = Mathf.Min(SetupTurnsPerPlayer, playerOneSetupTurnsCompleted + 1);
                    break;
            }
        }

        private int GetSetupTurnsCompleted(int playerSlot)
        {
            return playerSlot switch
            {
                0 => Mathf.Max(0, playerZeroSetupTurnsCompleted),
                1 => Mathf.Max(0, playerOneSetupTurnsCompleted),
                _ => SetupTurnsPerPlayer
            };
        }

        private bool HasSetupTurnsRemaining(int playerSlot)
        {
            return GetSetupTurnsCompleted(playerSlot) < SetupTurnsPerPlayer;
        }

        private int ResolveActivePlayerSlot()
        {
            return CurrentPlayerSlot;
        }

        private TurnActionState LoadActionState(int playerSlot)
        {
            if (CurrentMode != TurnMode.Setup)
            {
                return new TurnActionState
                {
                    Phase = CurrentPhase,
                    MoveBonus = MoveBonus,
                    RemainingMovePoints = RemainingMovePoints,
                    RemainingDieActions = RemainingDieActions,
                    DieAAvailable = DieAAvailable,
                    DieBAvailable = DieBAvailable,
                    RemainingCardTransfers = RemainingCardTransfers,
                    HasMovedThisTurn = HasMovedThisTurn,
                    ActiveCharacterNetId = ActiveCharacterNetId,
                    LastConsumedDieValue = LastConsumedDieValue
                };
            }

            return playerSlot switch
            {
                0 => new TurnActionState
                {
                    Phase = playerZeroSetupPhase,
                    MoveBonus = playerZeroSetupMoveBonus,
                    RemainingMovePoints = playerZeroSetupRemainingMovePoints,
                    RemainingDieActions = playerZeroSetupRemainingDieActions,
                    DieAAvailable = playerZeroSetupDieAAvailable,
                    DieBAvailable = playerZeroSetupDieBAvailable,
                    RemainingCardTransfers = playerZeroSetupRemainingCardTransfers,
                    HasMovedThisTurn = playerZeroSetupHasMoved,
                    ActiveCharacterNetId = playerZeroSetupActiveCharacterNetId,
                    LastConsumedDieValue = playerZeroSetupLastConsumedDieValue
                },
                1 => new TurnActionState
                {
                    Phase = playerOneSetupPhase,
                    MoveBonus = playerOneSetupMoveBonus,
                    RemainingMovePoints = playerOneSetupRemainingMovePoints,
                    RemainingDieActions = playerOneSetupRemainingDieActions,
                    DieAAvailable = playerOneSetupDieAAvailable,
                    DieBAvailable = playerOneSetupDieBAvailable,
                    RemainingCardTransfers = playerOneSetupRemainingCardTransfers,
                    HasMovedThisTurn = playerOneSetupHasMoved,
                    ActiveCharacterNetId = playerOneSetupActiveCharacterNetId,
                    LastConsumedDieValue = playerOneSetupLastConsumedDieValue
                },
                _ => new TurnActionState { Phase = TurnPhase.WaitingForRoll }
            };
        }

        [Server]
        private void SaveActionState(int playerSlot, TurnActionState state)
        {
            if (CurrentMode != TurnMode.Setup)
            {
                CurrentPhase = state.Phase;
                MoveBonus = state.MoveBonus;
                RemainingMovePoints = state.RemainingMovePoints;
                RemainingDieActions = state.RemainingDieActions;
                DieAAvailable = state.DieAAvailable;
                DieBAvailable = state.DieBAvailable;
                RemainingCardTransfers = state.RemainingCardTransfers;
                HasMovedThisTurn = state.HasMovedThisTurn;
                ActiveCharacterNetId = state.ActiveCharacterNetId;
                LastConsumedDieValue = state.LastConsumedDieValue;
                return;
            }

            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupPhase = state.Phase;
                    playerZeroSetupMoveBonus = state.MoveBonus;
                    playerZeroSetupRemainingMovePoints = state.RemainingMovePoints;
                    playerZeroSetupRemainingDieActions = state.RemainingDieActions;
                    playerZeroSetupDieAAvailable = state.DieAAvailable;
                    playerZeroSetupDieBAvailable = state.DieBAvailable;
                    playerZeroSetupRemainingCardTransfers = state.RemainingCardTransfers;
                    playerZeroSetupHasMoved = state.HasMovedThisTurn;
                    playerZeroSetupActiveCharacterNetId = state.ActiveCharacterNetId;
                    playerZeroSetupLastConsumedDieValue = state.LastConsumedDieValue;
                    break;
                case 1:
                    playerOneSetupPhase = state.Phase;
                    playerOneSetupMoveBonus = state.MoveBonus;
                    playerOneSetupRemainingMovePoints = state.RemainingMovePoints;
                    playerOneSetupRemainingDieActions = state.RemainingDieActions;
                    playerOneSetupDieAAvailable = state.DieAAvailable;
                    playerOneSetupDieBAvailable = state.DieBAvailable;
                    playerOneSetupRemainingCardTransfers = state.RemainingCardTransfers;
                    playerOneSetupHasMoved = state.HasMovedThisTurn;
                    playerOneSetupActiveCharacterNetId = state.ActiveCharacterNetId;
                    playerOneSetupLastConsumedDieValue = state.LastConsumedDieValue;
                    break;
            }
        }

        [Server]
        private void ResetActionState(int playerSlot)
        {
            SaveActionState(playerSlot, new TurnActionState { Phase = TurnPhase.WaitingForRoll });
        }

        [Server]
        private void ResetSetupActionStates()
        {
            playerZeroSetupPhase = TurnPhase.WaitingForRoll;
            playerOneSetupPhase = TurnPhase.WaitingForRoll;
            playerZeroSetupMoveBonus = 0;
            playerOneSetupMoveBonus = 0;
            playerZeroSetupRemainingMovePoints = 0;
            playerOneSetupRemainingMovePoints = 0;
            playerZeroSetupRemainingDieActions = 0;
            playerOneSetupRemainingDieActions = 0;
            playerZeroSetupDieAAvailable = false;
            playerOneSetupDieAAvailable = false;
            playerZeroSetupDieBAvailable = false;
            playerOneSetupDieBAvailable = false;
            playerZeroSetupRemainingCardTransfers = 0;
            playerOneSetupRemainingCardTransfers = 0;
            playerZeroSetupHasMoved = false;
            playerOneSetupHasMoved = false;
            playerZeroSetupActiveCharacterNetId = 0;
            playerOneSetupActiveCharacterNetId = 0;
            playerZeroSetupLastConsumedDieValue = 0;
            playerOneSetupLastConsumedDieValue = 0;
        }

        private int ResolveAvailableMoveBudget(int playerSlot, TurnActionState state)
        {
            if (state.HasMovedThisTurn || state.RemainingDieActions <= 0)
            {
                return 0;
            }

            return state.RemainingDieActions >= 2
                ? GetAvailableDiceTotal(playerSlot, state) + state.MoveBonus
                : GetSingleDieMoveBudget(playerSlot, state) + state.MoveBonus;
        }

        private int GetSingleDieMoveBudget(int playerSlot, TurnActionState state)
        {
            if (Dice.DiceManager.Instance == null || state.RemainingDieActions <= 0)
            {
                return 0;
            }

            int best = 0;
            if (state.DieAAvailable)
            {
                best = Mathf.Max(best, Dice.DiceManager.Instance.GetDieA(playerSlot));
            }

            if (state.DieBAvailable)
            {
                best = Mathf.Max(best, Dice.DiceManager.Instance.GetDieB(playerSlot));
            }

            return best;
        }

        private int GetAvailableDiceTotal(int playerSlot, TurnActionState state)
        {
            if (Dice.DiceManager.Instance == null)
            {
                return 0;
            }

            int total = 0;
            if (state.DieAAvailable)
            {
                total += Dice.DiceManager.Instance.GetDieA(playerSlot);
            }

            if (state.DieBAvailable)
            {
                total += Dice.DiceManager.Instance.GetDieB(playerSlot);
            }

            return total;
        }

        private bool CanSingleAvailableDieCoverMovement(int playerSlot, TurnActionState state, int usedSteps)
        {
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            return (state.DieAAvailable && usedSteps <= Dice.DiceManager.Instance.GetDieA(playerSlot) + state.MoveBonus) ||
                   (state.DieBAvailable && usedSteps <= Dice.DiceManager.Instance.GetDieB(playerSlot) + state.MoveBonus);
        }

        private bool TryConsumeMovementDie(int playerSlot, ref TurnActionState state, int usedSteps, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            bool canUseA = state.DieAAvailable && usedSteps <= Dice.DiceManager.Instance.GetDieA(playerSlot) + state.MoveBonus;
            bool canUseB = state.DieBAvailable && usedSteps <= Dice.DiceManager.Instance.GetDieB(playerSlot) + state.MoveBonus;
            if (!canUseA && !canUseB)
            {
                return false;
            }

            if (canUseA && (!canUseB || Dice.DiceManager.Instance.GetDieA(playerSlot) <= Dice.DiceManager.Instance.GetDieB(playerSlot)))
            {
                dieValue = Dice.DiceManager.Instance.GetDieA(playerSlot);
                state.DieAAvailable = false;
                RefreshRemainingDieActions(ref state);
                return true;
            }

            dieValue = Dice.DiceManager.Instance.GetDieB(playerSlot);
            state.DieBAvailable = false;
            RefreshRemainingDieActions(ref state);
            return true;
        }

        private bool TryConsumeAllAvailableDice(ref TurnActionState state)
        {
            if (state.RemainingDieActions <= 0)
            {
                return false;
            }

            state.DieAAvailable = false;
            state.DieBAvailable = false;
            return true;
        }

        private enum DieConsumptionPreference
        {
            Smallest,
            Largest
        }

        private bool TryConsumeAvailableDie(int playerSlot, ref TurnActionState state, DieConsumptionPreference preference, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null || state.RemainingDieActions <= 0)
            {
                return false;
            }

            bool useA;
            if (state.DieAAvailable && state.DieBAvailable)
            {
                useA = preference == DieConsumptionPreference.Smallest
                    ? Dice.DiceManager.Instance.GetDieA(playerSlot) <= Dice.DiceManager.Instance.GetDieB(playerSlot)
                    : Dice.DiceManager.Instance.GetDieA(playerSlot) >= Dice.DiceManager.Instance.GetDieB(playerSlot);
            }
            else
            {
                useA = state.DieAAvailable;
            }

            if (useA && state.DieAAvailable)
            {
                dieValue = Dice.DiceManager.Instance.GetDieA(playerSlot);
                state.DieAAvailable = false;
                RefreshRemainingDieActions(ref state);
                return true;
            }

            if (!state.DieBAvailable)
            {
                return false;
            }

            dieValue = Dice.DiceManager.Instance.GetDieB(playerSlot);
            state.DieBAvailable = false;
            RefreshRemainingDieActions(ref state);
            return true;
        }

        private bool HasAvailableDieAtLeast(int playerSlot, TurnActionState state, int minimumDieValue)
        {
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            return (state.DieAAvailable && Dice.DiceManager.Instance.GetDieA(playerSlot) >= minimumDieValue) ||
                   (state.DieBAvailable && Dice.DiceManager.Instance.GetDieB(playerSlot) >= minimumDieValue);
        }

        private bool TryConsumeAvailableDieAtLeast(int playerSlot, ref TurnActionState state, int minimumDieValue, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null || state.RemainingDieActions <= 0)
            {
                return false;
            }

            bool canUseA = state.DieAAvailable && Dice.DiceManager.Instance.GetDieA(playerSlot) >= minimumDieValue;
            bool canUseB = state.DieBAvailable && Dice.DiceManager.Instance.GetDieB(playerSlot) >= minimumDieValue;
            if (!canUseA && !canUseB)
            {
                return false;
            }

            if (canUseA && (!canUseB || Dice.DiceManager.Instance.GetDieA(playerSlot) <= Dice.DiceManager.Instance.GetDieB(playerSlot)))
            {
                dieValue = Dice.DiceManager.Instance.GetDieA(playerSlot);
                state.DieAAvailable = false;
                RefreshRemainingDieActions(ref state);
                return true;
            }

            dieValue = Dice.DiceManager.Instance.GetDieB(playerSlot);
            state.DieBAvailable = false;
            RefreshRemainingDieActions(ref state);
            return true;
        }

        private void RefreshRemainingDieActions()
        {
            RemainingDieActions = (DieAAvailable ? 1 : 0) + (DieBAvailable ? 1 : 0);
        }

        private void RefreshRemainingDieActions(ref TurnActionState state)
        {
            state.RemainingDieActions = (state.DieAAvailable ? 1 : 0) + (state.DieBAvailable ? 1 : 0);
        }

        private void ApplyPostDieConsumptionState(int playerSlot, ref TurnActionState state)
        {
            RefreshRemainingDieActions(ref state);
            if (state.RemainingDieActions <= 0)
            {
                state.RemainingMovePoints = 0;
                state.Phase = TurnPhase.WaitingForEndTurn;
            }
            else if (!state.HasMovedThisTurn)
            {
                state.RemainingMovePoints = ResolveAvailableMoveBudget(playerSlot, state);
            }
            else
            {
                state.RemainingMovePoints = 0;
            }
        }

        private static string NormalizeDeckNodeId(string deckNodeId)
        {
            return string.IsNullOrWhiteSpace(deckNodeId) ? string.Empty : deckNodeId.Trim();
        }

        private SyncDeckNodeIdSet GetDrawnDeckNodeIds(int playerSlot)
        {
            if (CurrentMode != TurnMode.Setup)
            {
                return drawnDeckNodeIdsThisTurn;
            }

            return playerSlot switch
            {
                0 => playerZeroSetupDrawnDeckNodeIdsThisTurn,
                1 => playerOneSetupDrawnDeckNodeIdsThisTurn,
                _ => drawnDeckNodeIdsThisTurn
            };
        }

        [Server]
        private static void ClearPlayerSelections(IReadOnlyList<NetworkPlayerConnection> players)
        {
            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                players[i]?.SetSelectedCharacter(0);
            }
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
