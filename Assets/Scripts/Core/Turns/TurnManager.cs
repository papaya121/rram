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
        public static TurnManager Instance { get; private set; }

        [SyncVar] public TurnMode CurrentMode = TurnMode.Setup;
        [SyncVar] public int CurrentPlayerSlot = -1;
        [SyncVar] public int TurnNumber;
        [SyncVar] public TurnPhase CurrentPhase = TurnPhase.WaitingForRoll;
        [SyncVar] public int MoveBonus;
        [SyncVar] public int RemainingMovePoints;
        [SyncVar] public int RemainingDieActions;
        [SyncVar] public bool HasMovedThisTurn;
        [SyncVar] public int CompletedTurnCount;
        [SyncVar] public int ActiveActionPlayerSlot = -1;
        [SyncVar] public int SetupTurnsPerPlayer;
        [SyncVar] private int playerZeroSetupTurnsRemaining;
        [SyncVar] private int playerOneSetupTurnsRemaining;

        public event Action<int> ServerTurnCompleted;

        private readonly List<int> orderedPlayerSlots = new();
        private int currentPlayerIndex;

        public TurnStateSnapshot Snapshot => new(TurnNumber, CurrentPlayerSlot, CurrentPhase, HasMovedThisTurn);

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

            currentPlayerIndex = 0;
            SetupTurnsPerPlayer = Mathf.Max(1, setupTurnsPerPlayer);
            playerZeroSetupTurnsRemaining = SetupTurnsPerPlayer;
            playerOneSetupTurnsRemaining = SetupTurnsPerPlayer;
            CurrentMode = TurnMode.Setup;
            TurnNumber = 1;
            CurrentPlayerSlot = -1;
            ActiveActionPlayerSlot = -1;
            CurrentPhase = TurnPhase.WaitingForRoll;
            Dice.DiceManager.Instance?.ServerResetTurn();
        }

        /// <summary>
        /// Returns the turn system to its idle lobby state.
        /// </summary>
        [Server]
        public void ServerResetState()
        {
            orderedPlayerSlots.Clear();
            currentPlayerIndex = 0;
            CurrentMode = TurnMode.Setup;
            CurrentPlayerSlot = -1;
            TurnNumber = 0;
            CurrentPhase = TurnPhase.WaitingForRoll;
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            HasMovedThisTurn = false;
            CompletedTurnCount = 0;
            ActiveActionPlayerSlot = -1;
            SetupTurnsPerPlayer = 0;
            playerZeroSetupTurnsRemaining = 0;
            playerOneSetupTurnsRemaining = 0;
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
            if (CurrentMode == TurnMode.Setup && player != null)
            {
                ActiveActionPlayerSlot = player.PlayerSlot;
            }

            RemainingDieActions = 2;
            RemainingMovePoints = ResolveAvailableMoveBudget();
            CurrentPhase = TurnPhase.WaitingForMove;
        }

        /// <summary>
        /// Adds temporary move points for the active player.
        /// </summary>
        [Server]
        public bool ServerAddMoveBonus(int playerSlot, int amount)
        {
            if (!CanPlayerModifyCurrentAction(playerSlot) || amount <= 0 || HasMovedThisTurn)
            {
                return false;
            }

            MoveBonus += amount;
            if (CurrentPhase == TurnPhase.WaitingForMove)
            {
                RemainingMovePoints += amount;
            }

            return true;
        }

        /// <summary>
        /// Returns the movement budget for the current turn.
        /// </summary>
        public int GetCurrentMoveBudget()
        {
            int diceTotal = Dice.DiceManager.Instance != null ? Dice.DiceManager.Instance.Total : 0;
            return diceTotal + MoveBonus;
        }

        /// <summary>
        /// Returns the movement budget that can be spent while keeping the second die for non-movement actions.
        /// </summary>
        public int GetPrimaryMoveBudget()
        {
            int firstDie = Dice.DiceManager.Instance != null ? Dice.DiceManager.Instance.DieA : 0;
            return firstDie + MoveBonus;
        }

        /// <summary>
        /// Returns movement points still available for the current action.
        /// </summary>
        public int GetRemainingMoveBudget()
        {
            return Mathf.Max(0, RemainingMovePoints);
        }

        /// <summary>
        /// Returns the number of unspent die-based actions in the current turn.
        /// </summary>
        public int GetRemainingDieActions()
        {
            return Mathf.Max(0, RemainingDieActions);
        }

        /// <summary>
        /// Returns true if the player may spend one die on a non-movement action.
        /// </summary>
        public bool CanPlayerSpendDieAction(int playerSlot)
        {
            return CanPlayerModifyCurrentAction(playerSlot) &&
                   CurrentPhase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolled &&
                   RemainingDieActions > 0;
        }

        /// <summary>
        /// Consumes one or more die actions for card draw/use style actions.
        /// </summary>
        [Server]
        public bool ServerTryConsumeDieAction(int playerSlot, int amount = 1)
        {
            if (amount <= 0 || !CanPlayerSpendDieAction(playerSlot) || RemainingDieActions < amount)
            {
                return false;
            }

            RemainingDieActions -= amount;
            if (RemainingDieActions <= 0)
            {
                RemainingMovePoints = 0;
                CurrentPhase = TurnPhase.WaitingForEndTurn;
            }
            else if (!HasMovedThisTurn)
            {
                RemainingMovePoints = ResolveAvailableMoveBudget();
            }

            return true;
        }

        /// <summary>
        /// Consumes movement points for a validated path.
        /// </summary>
        [Server]
        public bool ServerConsumeMovement(int usedSteps)
        {
            if (usedSteps <= 0 || !CanPlayerMove(ResolveActivePlayerSlot()) || usedSteps > RemainingMovePoints)
            {
                return false;
            }

            int requiredDieActions = usedSteps > GetPrimaryMoveBudget() ? 2 : 1;
            if (RemainingDieActions < requiredDieActions)
            {
                return false;
            }

            RemainingDieActions -= requiredDieActions;
            RemainingMovePoints = 0;
            HasMovedThisTurn = true;
            if (RemainingDieActions <= 0)
            {
                CurrentPhase = TurnPhase.WaitingForEndTurn;
            }

            return true;
        }

        /// <summary>
        /// Switches the match from setup turns into classic alternating turns.
        /// </summary>
        [Server]
        public void ServerBeginAlternatingPhase()
        {
            CurrentMode = TurnMode.Alternating;
            currentPlayerIndex = 0;
            CurrentPlayerSlot = orderedPlayerSlots.Count > 0 ? orderedPlayerSlots[0] : -1;
            ActiveActionPlayerSlot = -1;
            CurrentPhase = TurnPhase.WaitingForRoll;
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            HasMovedThisTurn = false;
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

            if (CurrentMode == TurnMode.Setup)
            {
                if (!CanPlayerAct(player.PlayerSlot) || (!force && !CanPlayerFinishSetupAction(player.PlayerSlot)))
                {
                    return false;
                }

                ConsumeSetupTurn(player.PlayerSlot);
                ActiveActionPlayerSlot = -1;
                CurrentPlayerSlot = -1;
            }
            else
            {
                if (player.PlayerSlot != CurrentPlayerSlot || (!force && !CanPlayerFinishAlternatingTurn(player.PlayerSlot)))
                {
                    return false;
                }

                currentPlayerIndex = (currentPlayerIndex + 1) % orderedPlayerSlots.Count;
                CurrentPlayerSlot = orderedPlayerSlots[currentPlayerIndex];
            }

            CompletedTurnCount++;
            TurnNumber++;
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            HasMovedThisTurn = false;
            CurrentPhase = TurnPhase.WaitingForRoll;
            Dice.DiceManager.Instance?.ServerResetTurn();
            ServerTurnCompleted?.Invoke(CompletedTurnCount);
            return true;
        }

        public bool IsSetupPhase => CurrentMode == TurnMode.Setup;

        public bool AreSetupTurnsFinished()
        {
            return playerZeroSetupTurnsRemaining <= 0 && playerOneSetupTurnsRemaining <= 0;
        }

        public int GetRemainingSetupTurns(int playerSlot)
        {
            return playerSlot switch
            {
                0 => Mathf.Max(0, playerZeroSetupTurnsRemaining),
                1 => Mathf.Max(0, playerOneSetupTurnsRemaining),
                _ => 0
            };
        }

        public bool CanPlayerAct(int playerSlot)
        {
            if (playerSlot < 0)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                int remainingTurns = GetRemainingSetupTurns(playerSlot);
                return remainingTurns > 0 && (ActiveActionPlayerSlot == -1 || ActiveActionPlayerSlot == playerSlot);
            }

            return playerSlot == CurrentPlayerSlot;
        }

        public bool CanPlayerRoll(int playerSlot)
        {
            return CanPlayerAct(playerSlot) && CurrentPhase == TurnPhase.WaitingForRoll;
        }

        public bool CanPlayerMove(int playerSlot)
        {
            return CanPlayerModifyCurrentAction(playerSlot) &&
                   CurrentPhase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolled &&
                   !HasMovedThisTurn &&
                   RemainingDieActions > 0 &&
                   RemainingMovePoints > 0;
        }

        public bool CanPlayerEndTurn(int playerSlot)
        {
            if (!CanPlayerModifyCurrentAction(playerSlot))
            {
                return false;
            }

            if (CurrentPhase == TurnPhase.WaitingForEndTurn)
            {
                return true;
            }

            return CurrentPhase == TurnPhase.WaitingForMove && Dice.DiceManager.Instance != null && Dice.DiceManager.Instance.HasRolled;
        }

        private bool CanPlayerModifyCurrentAction(int playerSlot)
        {
            if (playerSlot < 0)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return ActiveActionPlayerSlot == playerSlot && GetRemainingSetupTurns(playerSlot) > 0;
            }

            return playerSlot == CurrentPlayerSlot;
        }

        [Server]
        private bool CanPlayerFinishSetupAction(int playerSlot)
        {
            return CanPlayerEndTurn(playerSlot);
        }

        [Server]
        private bool CanPlayerFinishAlternatingTurn(int playerSlot)
        {
            return playerSlot == CurrentPlayerSlot && CanPlayerEndTurn(playerSlot);
        }

        [Server]
        private void ConsumeSetupTurn(int playerSlot)
        {
            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupTurnsRemaining = Mathf.Max(0, playerZeroSetupTurnsRemaining - 1);
                    break;
                case 1:
                    playerOneSetupTurnsRemaining = Mathf.Max(0, playerOneSetupTurnsRemaining - 1);
                    break;
            }
        }

        private int ResolveActivePlayerSlot()
        {
            return CurrentMode == TurnMode.Setup ? ActiveActionPlayerSlot : CurrentPlayerSlot;
        }

        private int ResolveAvailableMoveBudget()
        {
            if (HasMovedThisTurn || RemainingDieActions <= 0)
            {
                return 0;
            }

            return RemainingDieActions >= 2 ? GetCurrentMoveBudget() : GetPrimaryMoveBudget();
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
