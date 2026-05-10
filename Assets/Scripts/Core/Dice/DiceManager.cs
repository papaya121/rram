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

        public DiceRollResult CurrentResult => new(DieA, DieB);

        public bool HasRolledThisTurn(int playerSlot)
        {
            return HasRolled && LastRollPlayerSlot == playerSlot;
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
            TurnManager.Instance.ServerOnDiceRolled(player);
            return true;
        }

        /// <summary>
        /// Re-rolls the dice if a card effect allows it.
        /// </summary>
        [Server]
        public bool ServerRerollForCurrentPlayer(NetworkPlayerConnection player)
        {
            if (player == null || !TurnManager.Instance.ServerIsCurrentPlayer(player) || !HasRolled || TurnManager.Instance.HasMovedThisTurn)
            {
                return false;
            }

            RollInternal();
            LastRollPlayerSlot = player.PlayerSlot;
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

            if (HasRolled)
            {
                return false;
            }

            return true;
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
