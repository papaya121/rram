using Mirror;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Dice
{
    /// <summary>
    /// Owns server-authoritative rolling of two dice.
    /// </summary>
    public sealed class DiceManager : NetworkBehaviour
    {
        public static DiceManager Instance { get; private set; }

        [SyncVar] public int DieA;
        [SyncVar] public int DieB;
        [SyncVar] public int Total;
        [SyncVar] public bool HasRolled;
        [SyncVar] public int LastRollPlayerSlot = -1;
        [SyncVar] private int playerZeroSetupDieA;
        [SyncVar] private int playerZeroSetupDieB;
        [SyncVar] private int playerZeroSetupTotal;
        [SyncVar] private bool playerZeroSetupHasRolled;
        [SyncVar] private int playerOneSetupDieA;
        [SyncVar] private int playerOneSetupDieB;
        [SyncVar] private int playerOneSetupTotal;
        [SyncVar] private bool playerOneSetupHasRolled;

        public DiceRollResult CurrentResult => new(DieA, DieB);

        public bool HasRolledThisTurn(int playerSlot)
        {
            if (TurnManager.Instance != null && TurnManager.Instance.IsSetupPhase)
            {
                return playerSlot switch
                {
                    0 => playerZeroSetupHasRolled,
                    1 => playerOneSetupHasRolled,
                    _ => false
                };
            }

            return HasRolled && LastRollPlayerSlot == playerSlot;
        }

        public int GetDieA(int playerSlot)
        {
            return TurnManager.Instance != null && TurnManager.Instance.IsSetupPhase
                ? playerSlot switch
                {
                    0 => playerZeroSetupDieA,
                    1 => playerOneSetupDieA,
                    _ => 0
                }
                : DieA;
        }

        public int GetDieB(int playerSlot)
        {
            return TurnManager.Instance != null && TurnManager.Instance.IsSetupPhase
                ? playerSlot switch
                {
                    0 => playerZeroSetupDieB,
                    1 => playerOneSetupDieB,
                    _ => 0
                }
                : DieB;
        }

        public int GetTotal(int playerSlot)
        {
            return TurnManager.Instance != null && TurnManager.Instance.IsSetupPhase
                ? playerSlot switch
                {
                    0 => playerZeroSetupTotal,
                    1 => playerOneSetupTotal,
                    _ => 0
                }
                : Total;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Dice] Duplicate dice manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Clears the dice state for the next turn.
        /// </summary>
        [Server]
        public void ServerResetTurn()
        {
            ServerResetState();
        }

        [Server]
        public void ServerResetTurn(int playerSlot)
        {
            if (TurnManager.Instance == null || !TurnManager.Instance.IsSetupPhase)
            {
                ServerResetTurn();
                return;
            }

            SetSetupRoll(playerSlot, 0, 0, false);
            if (LastRollPlayerSlot == playerSlot)
            {
                DieA = 0;
                DieB = 0;
                Total = 0;
                HasRolled = false;
                LastRollPlayerSlot = -1;
            }
        }

        /// <summary>
        /// Clears all synchronized dice state.
        /// </summary>
        [Server]
        public void ServerResetState()
        {
            DieA = 0;
            DieB = 0;
            Total = 0;
            HasRolled = false;
            LastRollPlayerSlot = -1;
            SetSetupRoll(0, 0, 0, false);
            SetSetupRoll(1, 0, 0, false);
        }

        /// <summary>
        /// Rolls two dice for the active player.
        /// </summary>
        [Server]
        public bool ServerRollForCurrentPlayer(NetworkPlayerConnection player)
        {
            if (!CanRoll(player))
            {
                return false;
            }

            RollInternal();
            LastRollPlayerSlot = player.PlayerSlot;
            if (TurnManager.Instance != null && TurnManager.Instance.IsSetupPhase)
            {
                SetSetupRoll(player.PlayerSlot, DieA, DieB, true);
            }

            TurnManager.Instance.ServerOnDiceRolled(player);
            return true;
        }

        /// <summary>
        /// Re-rolls the dice if a card effect allows it.
        /// </summary>
        [Server]
        public bool ServerRerollForCurrentPlayer(NetworkPlayerConnection player)
        {
            if (player == null ||
                TurnManager.Instance == null ||
                !TurnManager.Instance.ServerIsCurrentPlayer(player) ||
                !HasRolledThisTurn(player.PlayerSlot) ||
                TurnManager.Instance.HasPlayerMovedThisTurn(player.PlayerSlot))
            {
                return false;
            }

            RollInternal();
            LastRollPlayerSlot = player.PlayerSlot;
            if (TurnManager.Instance.IsSetupPhase)
            {
                SetSetupRoll(player.PlayerSlot, DieA, DieB, true);
            }

            return true;
        }

        private bool CanRoll(NetworkPlayerConnection player)
        {
            if (player == null || TurnManager.Instance == null)
            {
                return false;
            }

            if (!TurnManager.Instance.CanPlayerRoll(player.PlayerSlot))
            {
                return false;
            }

            if (HasRolledThisTurn(player.PlayerSlot))
            {
                return false;
            }

            return true;
        }

        [Server]
        private void SetSetupRoll(int playerSlot, int dieA, int dieB, bool hasRolled)
        {
            int total = hasRolled ? dieA + dieB : 0;
            switch (playerSlot)
            {
                case 0:
                    playerZeroSetupDieA = dieA;
                    playerZeroSetupDieB = dieB;
                    playerZeroSetupTotal = total;
                    playerZeroSetupHasRolled = hasRolled;
                    break;
                case 1:
                    playerOneSetupDieA = dieA;
                    playerOneSetupDieB = dieB;
                    playerOneSetupTotal = total;
                    playerOneSetupHasRolled = hasRolled;
                    break;
            }
        }

        private void RollInternal()
        {
            DieA = Random.Range(1, 7);
            DieB = Random.Range(1, 7);
            Total = DieA + DieB;
            HasRolled = true;
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
