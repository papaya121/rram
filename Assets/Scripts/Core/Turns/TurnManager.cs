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
        [SyncVar] private int playerZeroSetupTurnsRemaining;
        [SyncVar] private int playerOneSetupTurnsRemaining;

        public event Action<int> ServerTurnCompleted;

        private readonly List<int> orderedPlayerSlots = new();
        private readonly SyncDeckNodeIdSet drawnDeckNodeIdsThisTurn = new();
        private int currentPlayerIndex;
        private int starterPlayerIndex;
        private bool setupCompletionNotified;

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

            starterPlayerIndex = orderedPlayerSlots.Count > 0 ? UnityEngine.Random.Range(0, orderedPlayerSlots.Count) : 0;
            currentPlayerIndex = starterPlayerIndex;
            SetupTurnsPerPlayer = Mathf.Max(1, setupTurnsPerPlayer);
            playerZeroSetupTurnsRemaining = SetupTurnsPerPlayer;
            playerOneSetupTurnsRemaining = SetupTurnsPerPlayer;
            CurrentMode = TurnMode.Setup;
            TurnNumber = 1;
            CurrentPlayerSlot = -1;
            ActiveActionPlayerSlot = -1;
            CurrentPhase = TurnPhase.WaitingForMove;
            ActiveCharacterNetId = 0;
            RemainingMovePoints = SetupTurnsPerPlayer;
            setupCompletionNotified = false;
            drawnDeckNodeIdsThisTurn.Clear();
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
            setupCompletionNotified = false;
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
            playerZeroSetupTurnsRemaining = 0;
            playerOneSetupTurnsRemaining = 0;
            drawnDeckNodeIdsThisTurn.Clear();
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
            if (player != null && player.PlayerSlot == CurrentPlayerSlot)
            {
                ActiveActionPlayerSlot = player.PlayerSlot;
            }

            DieAAvailable = true;
            DieBAvailable = true;
            RefreshRemainingDieActions();
            RemainingCardTransfers = 0;
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
            if (CurrentMode == TurnMode.Setup)
            {
                return RemainingMovePoints;
            }

            return HasMovedThisTurn
                ? GetRemainingMoveBudget()
                : GetAvailableDiceTotal() + MoveBonus;
        }

        /// <summary>
        /// Returns the movement budget that can be spent while keeping the second die for non-movement actions.
        /// </summary>
        public int GetPrimaryMoveBudget()
        {
            if (CurrentMode == TurnMode.Setup)
            {
                return RemainingMovePoints;
            }

            return HasMovedThisTurn
                ? 0
                : GetSingleDieMoveBudget() + MoveBonus;
        }

        /// <summary>
        /// Returns movement points still available for the current action.
        /// </summary>
        public int GetRemainingMoveBudget()
        {
            return Mathf.Max(0, RemainingMovePoints);
        }

        public int GetRemainingMoveBudget(int playerSlot)
        {
            return CurrentMode == TurnMode.Setup
                ? GetRemainingSetupTurns(playerSlot)
                : GetRemainingMoveBudget();
        }

        public int GetCurrentMoveBudget(int playerSlot)
        {
            return CurrentMode == TurnMode.Setup
                ? GetRemainingSetupTurns(playerSlot)
                : GetCurrentMoveBudget();
        }

        public int GetPrimaryMoveBudget(int playerSlot)
        {
            return CurrentMode == TurnMode.Setup
                ? GetRemainingSetupTurns(playerSlot)
                : GetPrimaryMoveBudget();
        }

        /// <summary>
        /// Returns the number of unspent die-based actions in the current turn.
        /// </summary>
        public int GetRemainingDieActions()
        {
            return Mathf.Max(0, RemainingDieActions);
        }

        /// <summary>
        /// Returns how many card transfers remain from the currently spent transfer die.
        /// </summary>
        public int GetRemainingCardTransfers()
        {
            return Mathf.Max(0, RemainingCardTransfers);
        }

        /// <summary>
        /// Returns true if the player may spend one die on a non-movement action.
        /// </summary>
        public bool CanPlayerSpendDieAction(int playerSlot)
        {
            if (CurrentMode == TurnMode.Setup)
            {
                return false;
            }

            return CanPlayerModifyCurrentAction(playerSlot) &&
                   CurrentPhase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolled &&
                   RemainingDieActions > 0;
        }

        public bool CanPlayerSpendDieActionWithMinimum(int playerSlot, int minimumDieValue)
        {
            return CanPlayerSpendDieAction(playerSlot) &&
                   HasAvailableDieAtLeast(Mathf.Max(1, minimumDieValue));
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

            RemainingCardTransfers = 0;
            LastConsumedDieValue = 0;
            for (int i = 0; i < amount; i++)
            {
                if (!TryConsumeAvailableDie(DieConsumptionPreference.Smallest, out int dieValue))
                {
                    return false;
                }

                LastConsumedDieValue += dieValue;
            }

            ApplyPostDieConsumptionState();
            return true;
        }

        [Server]
        public bool ServerTryConsumeDieActionWithMinimum(int playerSlot, int minimumDieValue)
        {
            if (!CanPlayerSpendDieActionWithMinimum(playerSlot, minimumDieValue))
            {
                return false;
            }

            RemainingCardTransfers = 0;
            if (!TryConsumeAvailableDieAtLeast(Mathf.Max(1, minimumDieValue), out int dieValue))
            {
                return false;
            }

            LastConsumedDieValue = dieValue;
            ApplyPostDieConsumptionState();
            return true;
        }

        /// <summary>
        /// Returns true if the player can transfer a card from the active character this turn.
        /// </summary>
        public bool CanPlayerTransferCard(int playerSlot)
        {
            if (CurrentMode == TurnMode.Setup)
            {
                return false;
            }

            return CanPlayerModifyCurrentAction(playerSlot) &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolled &&
                   (RemainingCardTransfers > 0 ||
                    (CurrentPhase == TurnPhase.WaitingForMove && RemainingDieActions > 0));
        }

        /// <summary>
        /// Consumes one card-transfer allowance, spending the smallest available die when needed.
        /// </summary>
        [Server]
        public bool ServerTryConsumeCardTransfer(int playerSlot)
        {
            if (!CanPlayerTransferCard(playerSlot))
            {
                return false;
            }

            if (RemainingCardTransfers <= 0)
            {
                if (!CanPlayerSpendDieAction(playerSlot) ||
                    !TryConsumeAvailableDie(DieConsumptionPreference.Smallest, out int dieValue))
                {
                    return false;
                }

                LastConsumedDieValue = dieValue;
                RemainingCardTransfers = Mathf.Max(0, dieValue);
                RefreshRemainingDieActions();
                RemainingMovePoints = !HasMovedThisTurn ? ResolveAvailableMoveBudget() : 0;
            }

            RemainingCardTransfers = Mathf.Max(0, RemainingCardTransfers - 1);
            if (RemainingDieActions <= 0 && RemainingCardTransfers <= 0)
            {
                RemainingMovePoints = 0;
                CurrentPhase = TurnPhase.WaitingForEndTurn;
            }

            return true;
        }

        /// <summary>
        /// Locks the current turn to one character before resolving a die-backed action.
        /// </summary>
        [Server]
        public bool ServerTryUseCharacterForCurrentTurn(int playerSlot, uint characterNetId)
        {
            if (characterNetId == 0 || !CanPlayerAct(playerSlot) || CurrentPhase != TurnPhase.WaitingForMove)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return true;
            }

            bool isContinuingMovement =
                HasMovedThisTurn &&
                ActiveCharacterNetId == characterNetId &&
                RemainingMovePoints > 0;
            if (isContinuingMovement)
            {
                return true;
            }

            if (!CanPlayerSpendDieAction(playerSlot) || !CanPlayerSelectCharacter(playerSlot, characterNetId))
            {
                return false;
            }

            if (ActiveCharacterNetId == 0)
            {
                ActiveCharacterNetId = characterNetId;
            }

            return ActiveCharacterNetId == characterNetId;
        }

        /// <summary>
        /// Consumes one die for a deck draw and remembers the deck point used this turn.
        /// </summary>
        [Server]
        public bool ServerTryConsumeDeckDrawAction(int playerSlot, string deckNodeId)
        {
            string normalizedDeckNodeId = NormalizeDeckNodeId(deckNodeId);
            if (string.IsNullOrEmpty(normalizedDeckNodeId) || HasDrawnFromDeckNode(normalizedDeckNodeId))
            {
                return false;
            }

            if (!ServerTryConsumeDieAction(playerSlot))
            {
                return false;
            }

            drawnDeckNodeIdsThisTurn.Add(normalizedDeckNodeId);
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

            return ServerConsumeMovement(ResolveActivePlayerSlot(), usedSteps);
        }

        [Server]
        public bool ServerConsumeMovement(int playerSlot, int usedSteps)
        {
            if (usedSteps <= 0 || !CanPlayerMove(playerSlot))
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                int remainingSetupSteps = GetRemainingSetupTurns(playerSlot);
                if (usedSteps > remainingSetupSteps)
                {
                    return false;
                }

                RemainingCardTransfers = 0;
                ConsumeSetupSteps(playerSlot, usedSteps);
                RemainingMovePoints = Mathf.Max(0, remainingSetupSteps - usedSteps);
                HasMovedThisTurn = true;
                ActiveCharacterNetId = 0;
                TryNotifySetupCompleted();
                return true;
            }

            if (usedSteps > RemainingMovePoints)
            {
                return false;
            }

            RemainingCardTransfers = 0;
            RemainingMovePoints = Mathf.Max(0, RemainingMovePoints - usedSteps);
            HasMovedThisTurn = true;
            DieAAvailable = false;
            DieBAvailable = false;
            RefreshRemainingDieActions();

            if (RemainingMovePoints <= 0)
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
                if (!CanPlayerFinishSetupAction(player.PlayerSlot))
                {
                    return false;
                }

                ClearSetupSteps(player.PlayerSlot);
                CompletedTurnCount++;
                TurnNumber++;
                ClearPlayerSelections(Match.MatchManager.Instance?.Players);
                TryNotifySetupCompleted();
                return true;
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
            MoveBonus = 0;
            RemainingMovePoints = 0;
            RemainingDieActions = 0;
            DieAAvailable = false;
            DieBAvailable = false;
            RemainingCardTransfers = 0;
            HasMovedThisTurn = false;
            ActiveCharacterNetId = 0;
            LastConsumedDieValue = 0;
            CurrentPhase = CurrentMode == TurnMode.Setup && !AreSetupTurnsFinished()
                ? TurnPhase.WaitingForMove
                : TurnPhase.WaitingForRoll;
            if (CurrentMode == TurnMode.Setup && !AreSetupTurnsFinished())
            {
                RemainingMovePoints = GetRemainingSetupTurns(CurrentPlayerSlot);
            }

            drawnDeckNodeIdsThisTurn.Clear();
            Dice.DiceManager.Instance?.ServerResetTurn();
            ClearPlayerSelections(Match.MatchManager.Instance?.Players);
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
                return remainingTurns > 0 && CurrentPhase == TurnPhase.WaitingForMove;
            }

            return playerSlot == CurrentPlayerSlot;
        }

        public bool CanPlayerRoll(int playerSlot)
        {
            return CurrentMode != TurnMode.Setup && CanPlayerAct(playerSlot) && CurrentPhase == TurnPhase.WaitingForRoll;
        }

        public bool CanPlayerMove(int playerSlot)
        {
            if (CurrentMode == TurnMode.Setup)
            {
                return CanPlayerModifyCurrentAction(playerSlot) && GetRemainingSetupTurns(playerSlot) > 0;
            }

            return CanPlayerModifyCurrentAction(playerSlot) &&
                   CurrentPhase == TurnPhase.WaitingForMove &&
                   Dice.DiceManager.Instance != null &&
                   Dice.DiceManager.Instance.HasRolled &&
                   (RemainingDieActions > 0 || HasMovedThisTurn) &&
                   RemainingMovePoints > 0;
        }

        public bool CanPlayerEndTurn(int playerSlot)
        {
            if (!CanPlayerModifyCurrentAction(playerSlot))
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return CurrentPhase == TurnPhase.WaitingForMove;
            }

            if (CurrentPhase == TurnPhase.WaitingForEndTurn)
            {
                return true;
            }

            return CurrentPhase == TurnPhase.WaitingForMove && Dice.DiceManager.Instance != null && Dice.DiceManager.Instance.HasRolled;
        }

        public bool CanPlayerSelectCharacter(int playerSlot, uint characterNetId)
        {
            if (characterNetId == 0 ||
                !CanPlayerAct(playerSlot) ||
                CurrentPhase != TurnPhase.WaitingForMove)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return GetRemainingSetupTurns(playerSlot) > 0;
            }

            if (Dice.DiceManager.Instance == null || !Dice.DiceManager.Instance.HasRolled)
            {
                return false;
            }

            return ActiveCharacterNetId == 0 || ActiveCharacterNetId == characterNetId;
        }

        public bool HasDrawnFromDeckNode(string deckNodeId)
        {
            string normalizedDeckNodeId = NormalizeDeckNodeId(deckNodeId);
            return !string.IsNullOrEmpty(normalizedDeckNodeId) &&
                   drawnDeckNodeIdsThisTurn.Contains(normalizedDeckNodeId);
        }

        private bool CanPlayerModifyCurrentAction(int playerSlot)
        {
            if (playerSlot < 0)
            {
                return false;
            }

            if (CurrentMode == TurnMode.Setup)
            {
                return CurrentPhase == TurnPhase.WaitingForMove && GetRemainingSetupTurns(playerSlot) > 0;
            }

            return playerSlot == CurrentPlayerSlot;
        }

        [Server]
        private bool CanPlayerFinishSetupAction(int playerSlot)
        {
            return CurrentMode == TurnMode.Setup &&
                   CurrentPhase == TurnPhase.WaitingForMove &&
                   GetRemainingSetupTurns(playerSlot) > 0;
        }

        [Server]
        private bool CanPlayerFinishAlternatingTurn(int playerSlot)
        {
            return playerSlot == CurrentPlayerSlot && CanPlayerEndTurn(playerSlot);
        }

        [Server]
        private void ConsumeSetupSteps(int playerSlot, int usedSteps)
        {
            int amount = Mathf.Max(0, usedSteps);
            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupTurnsRemaining = Mathf.Max(0, playerZeroSetupTurnsRemaining - amount);
                    break;
                case 1:
                    playerOneSetupTurnsRemaining = Mathf.Max(0, playerOneSetupTurnsRemaining - amount);
                    break;
            }
        }

        [Server]
        private void ClearSetupSteps(int playerSlot)
        {
            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupTurnsRemaining = 0;
                    break;
                case 1:
                    playerOneSetupTurnsRemaining = 0;
                    break;
            }
        }

        [Server]
        private void AdvanceSetupTurnOrder()
        {
            if (orderedPlayerSlots.Count == 0 || AreSetupTurnsFinished())
            {
                CurrentPlayerSlot = -1;
                ActiveActionPlayerSlot = -1;
                return;
            }

            for (int i = 0; i < orderedPlayerSlots.Count; i++)
            {
                currentPlayerIndex = (currentPlayerIndex + 1) % orderedPlayerSlots.Count;
                int nextPlayerSlot = orderedPlayerSlots[currentPlayerIndex];
                if (GetRemainingSetupTurns(nextPlayerSlot) <= 0)
                {
                    continue;
                }

                CurrentPlayerSlot = nextPlayerSlot;
                ActiveActionPlayerSlot = nextPlayerSlot;
                return;
            }

            CurrentPlayerSlot = -1;
            ActiveActionPlayerSlot = -1;
        }

        [Server]
        private void TryNotifySetupCompleted()
        {
            if (setupCompletionNotified || !AreSetupTurnsFinished())
            {
                return;
            }

            setupCompletionNotified = true;
            CurrentPlayerSlot = -1;
            ActiveActionPlayerSlot = -1;
            ActiveCharacterNetId = 0;
            RemainingMovePoints = 0;
            CurrentPhase = TurnPhase.WaitingForEndTurn;
            ServerTurnCompleted?.Invoke(CompletedTurnCount);
        }

        private int ResolveActivePlayerSlot()
        {
            return CurrentPlayerSlot;
        }

        private int ResolveAvailableMoveBudget()
        {
            if (HasMovedThisTurn || RemainingDieActions <= 0)
            {
                return 0;
            }

            return RemainingDieActions >= 2 ? GetCurrentMoveBudget() : GetPrimaryMoveBudget();
        }

        private int GetSingleDieMoveBudget()
        {
            if (Dice.DiceManager.Instance == null || RemainingDieActions <= 0)
            {
                return 0;
            }

            int best = 0;
            if (DieAAvailable)
            {
                best = Mathf.Max(best, Dice.DiceManager.Instance.DieA);
            }

            if (DieBAvailable)
            {
                best = Mathf.Max(best, Dice.DiceManager.Instance.DieB);
            }

            return best;
        }

        private int GetAvailableDiceTotal()
        {
            if (Dice.DiceManager.Instance == null)
            {
                return 0;
            }

            int total = 0;
            if (DieAAvailable)
            {
                total += Dice.DiceManager.Instance.DieA;
            }

            if (DieBAvailable)
            {
                total += Dice.DiceManager.Instance.DieB;
            }

            return total;
        }

        private bool CanSingleAvailableDieCoverMovement(int usedSteps)
        {
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            return (DieAAvailable && usedSteps <= Dice.DiceManager.Instance.DieA + MoveBonus) ||
                   (DieBAvailable && usedSteps <= Dice.DiceManager.Instance.DieB + MoveBonus);
        }

        private bool TryConsumeMovementDie(int usedSteps, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            bool canUseA = DieAAvailable && usedSteps <= Dice.DiceManager.Instance.DieA + MoveBonus;
            bool canUseB = DieBAvailable && usedSteps <= Dice.DiceManager.Instance.DieB + MoveBonus;
            if (!canUseA && !canUseB)
            {
                return false;
            }

            if (canUseA && (!canUseB || Dice.DiceManager.Instance.DieA <= Dice.DiceManager.Instance.DieB))
            {
                dieValue = Dice.DiceManager.Instance.DieA;
                DieAAvailable = false;
                return true;
            }

            dieValue = Dice.DiceManager.Instance.DieB;
            DieBAvailable = false;
            return true;
        }

        private bool TryConsumeAllAvailableDice()
        {
            if (RemainingDieActions <= 0)
            {
                return false;
            }

            DieAAvailable = false;
            DieBAvailable = false;
            return true;
        }

        private enum DieConsumptionPreference
        {
            Smallest,
            Largest
        }

        private bool TryConsumeAvailableDie(DieConsumptionPreference preference, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null || RemainingDieActions <= 0)
            {
                return false;
            }

            bool useA;
            if (DieAAvailable && DieBAvailable)
            {
                useA = preference == DieConsumptionPreference.Smallest
                    ? Dice.DiceManager.Instance.DieA <= Dice.DiceManager.Instance.DieB
                    : Dice.DiceManager.Instance.DieA >= Dice.DiceManager.Instance.DieB;
            }
            else
            {
                useA = DieAAvailable;
            }

            if (useA && DieAAvailable)
            {
                dieValue = Dice.DiceManager.Instance.DieA;
                DieAAvailable = false;
                RefreshRemainingDieActions();
                return true;
            }

            if (!DieBAvailable)
            {
                return false;
            }

            dieValue = Dice.DiceManager.Instance.DieB;
            DieBAvailable = false;
            RefreshRemainingDieActions();
            return true;
        }

        private bool HasAvailableDieAtLeast(int minimumDieValue)
        {
            if (Dice.DiceManager.Instance == null)
            {
                return false;
            }

            return (DieAAvailable && Dice.DiceManager.Instance.DieA >= minimumDieValue) ||
                   (DieBAvailable && Dice.DiceManager.Instance.DieB >= minimumDieValue);
        }

        private bool TryConsumeAvailableDieAtLeast(int minimumDieValue, out int dieValue)
        {
            dieValue = 0;
            if (Dice.DiceManager.Instance == null || RemainingDieActions <= 0)
            {
                return false;
            }

            bool canUseA = DieAAvailable && Dice.DiceManager.Instance.DieA >= minimumDieValue;
            bool canUseB = DieBAvailable && Dice.DiceManager.Instance.DieB >= minimumDieValue;
            if (!canUseA && !canUseB)
            {
                return false;
            }

            if (canUseA && (!canUseB || Dice.DiceManager.Instance.DieA <= Dice.DiceManager.Instance.DieB))
            {
                dieValue = Dice.DiceManager.Instance.DieA;
                DieAAvailable = false;
                RefreshRemainingDieActions();
                return true;
            }

            dieValue = Dice.DiceManager.Instance.DieB;
            DieBAvailable = false;
            RefreshRemainingDieActions();
            return true;
        }

        private void RefreshRemainingDieActions()
        {
            RemainingDieActions = (DieAAvailable ? 1 : 0) + (DieBAvailable ? 1 : 0);
        }

        private void ApplyPostDieConsumptionState()
        {
            RefreshRemainingDieActions();
            if (RemainingDieActions <= 0)
            {
                RemainingMovePoints = 0;
                CurrentPhase = TurnPhase.WaitingForEndTurn;
            }
            else if (!HasMovedThisTurn)
            {
                RemainingMovePoints = ResolveAvailableMoveBudget();
            }
            else
            {
                RemainingMovePoints = 0;
            }
        }

        private static string NormalizeDeckNodeId(string deckNodeId)
        {
            return string.IsNullOrWhiteSpace(deckNodeId) ? string.Empty : deckNodeId.Trim();
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
