using System.Collections.Generic;
using System.Linq;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using UnityEngine;

namespace RRaM.Core.Characters
{
    /// <summary>
    /// Spawns player rosters and validates movement requests.
    /// </summary>
    public sealed class CharacterManager : NetworkBehaviour
    {
        private enum StarterSide
        {
            Left,
            Right
        }

        public static CharacterManager Instance { get; private set; }

        private readonly Dictionary<int, List<NetworkCharacterPawn>> charactersByPlayerSlot = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Characters] Duplicate character manager detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Spawns the five starter characters for each connected player.
        /// </summary>
        [Server]
        public void ServerSpawnStarterCharacters(IReadOnlyList<NetworkPlayerConnection> players)
        {
            ServerResetState(players);

            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayerConnection player = players[i];
                List<NetworkCharacterPawn> roster = new();
                foreach (CharacterType characterType in System.Enum.GetValues(typeof(CharacterType)))
                {
                    string startNodeId = ResolveStartNodeId(player.PlayerSlot, characterType);
                    RramNetworkManager networkManager = NetworkManager.singleton as RramNetworkManager;
                    GameObject pawnObject = networkManager != null
                        ? networkManager.CreateCharacterInstance()
                        : null;
                    if (pawnObject == null)
                    {
                        Debug.LogError("[Server] Character spawn aborted. No prefab instance available.");
                        continue;
                    }

                    NetworkCharacterPawn pawn = pawnObject.GetComponent<NetworkCharacterPawn>();
                    pawn.ServerInitialize(player.PlayerSlot, characterType, ResolveDisplayName(characterType), startNodeId);
                    NetworkServer.Spawn(pawnObject);
                    roster.Add(pawn);
                }

                charactersByPlayerSlot[player.PlayerSlot] = roster;
                SyncPlayerCharacters(player);
            }
        }

        /// <summary>
        /// Clears all spawned characters and synchronized rosters.
        /// </summary>
        [Server]
        public void ServerResetState(IReadOnlyList<NetworkPlayerConnection> players)
        {
            DespawnExistingCharacters();
            charactersByPlayerSlot.Clear();

            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }

                players[i].SetSelectedCharacter(0);
                players[i].SetCharacterSnapshots(System.Array.Empty<CharacterSnapshot>());
            }
        }

        /// <summary>
        /// Tries to select a character owned by the player.
        /// </summary>
        [Server]
        public bool ServerTrySelectCharacter(NetworkPlayerConnection player, uint characterNetId)
        {
            if (player == null || !TryGetServerCharacter(characterNetId, out NetworkCharacterPawn pawn))
            {
                return false;
            }

            if (pawn.OwnerSlot != player.PlayerSlot ||
                (TurnManager.Instance != null && !TurnManager.Instance.CanPlayerSelectCharacter(player.PlayerSlot, characterNetId)))
            {
                return false;
            }

            player.SetSelectedCharacter(characterNetId);
            SyncPlayerCharacters(player);
            return true;
        }

        /// <summary>
        /// Tries to select a character owned by the player at the given board node.
        /// </summary>
        [Server]
        public bool ServerTrySelectCharacterAtNode(NetworkPlayerConnection player, string nodeId)
        {
            if (!TryGetOwnedCharacterAtNode(player, nodeId, out NetworkCharacterPawn pawn))
            {
                return false;
            }

            if (TurnManager.Instance != null && !TurnManager.Instance.CanPlayerSelectCharacter(player.PlayerSlot, pawn.netId))
            {
                return false;
            }

            player.SetSelectedCharacter(pawn.netId);
            SyncPlayerCharacters(player);
            return true;
        }

        /// <summary>
        /// Validates and executes a movement request for the selected character.
        /// </summary>
        [Server]
        public bool ServerTryMoveSelectedCharacter(NetworkPlayerConnection player, string destinationNodeId, uint requestedCharacterNetId = 0)
        {
            if (player == null || TurnManager.Instance == null || Dice.DiceManager.Instance == null)
            {
                Debug.LogWarning($"[Server] Movement rejected. Missing dependencies. Player={(player != null)} TurnManager={(TurnManager.Instance != null)} DiceManager={(Dice.DiceManager.Instance != null)}");
                return false;
            }

            if (!TurnManager.Instance.ServerIsCurrentPlayer(player))
            {
                Debug.LogWarning($"[Server] Movement rejected. Not current player. Slot={player.PlayerSlot}, CurrentPlayerSlot={TurnManager.Instance.CurrentPlayerSlot}");
                return false;
            }

            if (!Dice.DiceManager.Instance.HasRolled || !TurnManager.Instance.CanPlayerMove(player.PlayerSlot))
            {
                Debug.LogWarning($"[Server] Movement rejected. Cannot move. Slot={player.PlayerSlot}, HasRolled={Dice.DiceManager.Instance.HasRolled}, CanPlayerMove={TurnManager.Instance.CanPlayerMove(player.PlayerSlot)}, Phase={TurnManager.Instance.CurrentPhase}, RemainingMove={TurnManager.Instance.GetRemainingMoveBudget()}, RemainingDieActions={TurnManager.Instance.GetRemainingDieActions()}");
                return false;
            }

            uint characterNetIdToMove = requestedCharacterNetId != 0 ? requestedCharacterNetId : player.SelectedCharacterNetId;
            if (!TryGetServerCharacter(characterNetIdToMove, out NetworkCharacterPawn pawn))
            {
                Debug.LogWarning($"[Server] Movement rejected. Character not found. Slot={player.PlayerSlot}, RequestedCharacterNetId={requestedCharacterNetId}, SelectedCharacterNetId={player.SelectedCharacterNetId}, Destination={destinationNodeId}");
                return false;
            }

            if (pawn.OwnerSlot != player.PlayerSlot)
            {
                Debug.LogWarning($"[Server] Movement rejected. Character ownership mismatch. Slot={player.PlayerSlot}, CharacterNetId={pawn.netId}, OwnerSlot={pawn.OwnerSlot}, Destination={destinationNodeId}");
                return false;
            }

            if (TryGetOwnedCharacterAtNode(player, destinationNodeId, out NetworkCharacterPawn ownedOccupant) &&
                ownedOccupant != null &&
                ownedOccupant.netId != pawn.netId)
            {
                if (TurnManager.Instance.CanPlayerSelectCharacter(player.PlayerSlot, ownedOccupant.netId))
                {
                    player.SetSelectedCharacter(ownedOccupant.netId);
                    Debug.Log($"[Server] Movement converted to selection. Slot={player.PlayerSlot}, Destination={destinationNodeId}, SelectedCharacterNetId={ownedOccupant.netId}");
                    return true;
                }

                Debug.LogWarning($"[Server] Movement rejected. Destination '{destinationNodeId}' is occupied by owned character NetId={ownedOccupant.netId}.");
                return false;
            }

            if (!TurnManager.Instance.ServerTryUseCharacterForCurrentTurn(player.PlayerSlot, pawn.netId))
            {
                Debug.LogWarning($"[Server] Movement rejected. Turn is locked to another character. Slot={player.PlayerSlot}, CharacterNetId={pawn.netId}, ActiveCharacterNetId={TurnManager.Instance.ActiveCharacterNetId}");
                return false;
            }

            int moveBudget = TurnManager.Instance.GetRemainingMoveBudget();
            if (!TryResolveMovementPath(pawn.CurrentNodeId, destinationNodeId, moveBudget, out List<string> path, out string pathResolver))
            {
                Debug.LogWarning($"[Server] Movement rejected. Path invalid. Slot={player.PlayerSlot}, CharacterNetId={pawn.netId}, From={pawn.CurrentNodeId}, Destination={destinationNodeId}, Budget={moveBudget}, Resolver={pathResolver}");
                return false;
            }

            int usedSteps = Mathf.Max(0, path.Count - 1);
            if (!TurnManager.Instance.ServerConsumeMovement(usedSteps))
            {
                Debug.LogWarning($"[Server] Movement rejected. Failed to consume movement. Slot={player.PlayerSlot}, CharacterNetId={pawn.netId}, UsedSteps={usedSteps}, PrimaryBudget={TurnManager.Instance.GetPrimaryMoveBudget()}, RemainingMove={TurnManager.Instance.GetRemainingMoveBudget()}, RemainingDieActions={TurnManager.Instance.GetRemainingDieActions()}");
                return false;
            }

            string originNodeId = pawn.CurrentNodeId;
            player.SetSelectedCharacter(pawn.netId);
            pawn.ServerSetCurrentNode(destinationNodeId);
            SyncPlayerCharacters(player);
            Debug.Log($"[Server] Movement accepted. Slot={player.PlayerSlot}, CharacterNetId={pawn.netId}, From={originNodeId}, Destination={destinationNodeId}, UsedSteps={usedSteps}, Resolver={pathResolver}");
            return true;
        }

        /// <summary>
        /// Resolves a server-side character netId to a pawn.
        /// </summary>
        [Server]
        public bool TryGetServerCharacter(uint characterNetId, out NetworkCharacterPawn pawn)
        {
            pawn = null;
            if (characterNetId == 0 || !NetworkServer.spawned.TryGetValue(characterNetId, out NetworkIdentity identity))
            {
                return false;
            }

            pawn = identity.GetComponent<NetworkCharacterPawn>();
            return pawn != null;
        }

        [Server]
        public bool TryGetServerCharacter(NetworkPlayerConnection player, CharacterType characterType, out NetworkCharacterPawn pawn)
        {
            pawn = null;
            if (player == null || !charactersByPlayerSlot.TryGetValue(player.PlayerSlot, out List<NetworkCharacterPawn> roster))
            {
                return false;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                NetworkCharacterPawn candidate = roster[i];
                if (candidate != null && candidate.CharacterType == characterType)
                {
                    pawn = candidate;
                    return true;
                }
            }

            return false;
        }

        [Server]
        public bool TryGetRandomActiveServerCharacter(NetworkPlayerConnection player, out NetworkCharacterPawn pawn)
        {
            pawn = null;
            if (player == null || !charactersByPlayerSlot.TryGetValue(player.PlayerSlot, out List<NetworkCharacterPawn> roster))
            {
                return false;
            }

            List<NetworkCharacterPawn> activeCharacters = new();
            for (int i = 0; i < roster.Count; i++)
            {
                NetworkCharacterPawn candidate = roster[i];
                if (candidate != null && candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy)
                {
                    activeCharacters.Add(candidate);
                }
            }

            if (activeCharacters.Count == 0)
            {
                return false;
            }

            pawn = activeCharacters[Random.Range(0, activeCharacters.Count)];
            return pawn != null;
        }

        [Server]
        private bool TryGetOwnedCharacterAtNode(NetworkPlayerConnection player, string nodeId, out NetworkCharacterPawn pawn)
        {
            pawn = null;
            if (player == null ||
                string.IsNullOrWhiteSpace(nodeId) ||
                !charactersByPlayerSlot.TryGetValue(player.PlayerSlot, out List<NetworkCharacterPawn> roster))
            {
                return false;
            }

            string normalizedNodeId = nodeId.Trim();
            for (int i = 0; i < roster.Count; i++)
            {
                NetworkCharacterPawn candidate = roster[i];
                if (candidate == null || candidate.netId == 0 || candidate.CurrentNodeId != normalizedNodeId)
                {
                    continue;
                }

                pawn = candidate;
                return true;
            }

            return false;
        }

        private static bool TryResolveMovementPath(string startNodeId, string destinationNodeId, int moveBudget, out List<string> path, out string resolver)
        {
            path = new List<string>();
            resolver = "none";

            BoardPathValidator validator = ResolveBoardPathValidator();
            if (validator != null)
            {
                resolver = "BoardPathValidator";
                return validator.TryFindPathWithinRange(startNodeId, destinationNodeId, moveBudget, out path);
            }

            BoardGraph boardGraph = ResolveBoardGraph();
            if (boardGraph == null ||
                moveBudget < 0 ||
                string.IsNullOrWhiteSpace(startNodeId) ||
                string.IsNullOrWhiteSpace(destinationNodeId) ||
                startNodeId == destinationNodeId ||
                !boardGraph.TryGetNode(startNodeId, out _) ||
                !boardGraph.TryGetNode(destinationNodeId, out _) ||
                !boardGraph.TryGetShortestPath(startNodeId, destinationNodeId, out path))
            {
                resolver = boardGraph == null ? "BoardGraphMissing" : "BoardGraphFallback";
                path.Clear();
                return false;
            }

            int usedSteps = Mathf.Max(0, path.Count - 1);
            if (usedSteps > moveBudget)
            {
                resolver = "BoardGraphFallback";
                path.Clear();
                return false;
            }

            resolver = "BoardGraphFallback";
            return true;
        }

        private static BoardPathValidator ResolveBoardPathValidator()
        {
            if (Match.MatchContext.Instance != null && Match.MatchContext.Instance.BoardPathValidator != null)
            {
                return Match.MatchContext.Instance.BoardPathValidator;
            }

            return FindAnyObjectByType<BoardPathValidator>();
        }

        private static BoardGraph ResolveBoardGraph()
        {
            if (Match.MatchContext.Instance != null && Match.MatchContext.Instance.BoardGraph != null)
            {
                Match.MatchContext.Instance.BoardGraph.EnsureInitialized();
                return Match.MatchContext.Instance.BoardGraph;
            }

            BoardGraph boardGraph = BoardGraph.Instance;
            boardGraph ??= FindAnyObjectByType<BoardGraph>();
            if (boardGraph != null)
            {
                boardGraph.EnsureInitialized();
            }

            return boardGraph;
        }

        private string ResolveStartNodeId(int playerSlot, CharacterType characterType)
        {
            BoardGraph boardGraph = BoardGraph.Instance;
            if (boardGraph == null)
            {
                return playerSlot == 0 ? "N00" : "N06";
            }

            boardGraph.EnsureInitialized();
            List<BoardNode> starterNodes = boardGraph.Nodes.Where(node => node.IsStarterNode).ToList();
            if (starterNodes.Count == 0)
            {
                return playerSlot == 0 ? "N00" : "N06";
            }

            float separatorX = ResolveStarterSideSeparatorX(starterNodes);

            StarterSide preferredSide = playerSlot == 0 ? StarterSide.Left : StarterSide.Right;
            List<BoardNode> preferredSideNodes = starterNodes
                .Where(node => ResolveSide(node, separatorX) == preferredSide)
                .OrderBy(node => node.LocalPosition.z)
                .ThenBy(node => node.LocalPosition.x)
                .ThenBy(node => node.NodeId)
                .ToList();
            BoardNode exactMatch = preferredSideNodes.FirstOrDefault(node => node.StarterCharacterType == characterType);
            if (exactMatch != null)
            {
                return exactMatch.NodeId;
            }

            BoardNode anyMatch = starterNodes.FirstOrDefault(node => node.StarterCharacterType == characterType);
            if (anyMatch != null)
            {
                return anyMatch.NodeId;
            }

            BoardNode sideFallback = preferredSideNodes.FirstOrDefault();
            if (sideFallback != null)
            {
                return sideFallback.NodeId;
            }

            return starterNodes[0].NodeId;
        }

        private static float ResolveStarterSideSeparatorX(IReadOnlyList<BoardNode> starterNodes)
        {
            if (starterNodes == null || starterNodes.Count == 0)
            {
                return 0f;
            }

            List<float> sortedXs = starterNodes
                .Select(node => node.LocalPosition.x)
                .OrderBy(value => value)
                .ToList();
            if (sortedXs.Count == 1)
            {
                return sortedXs[0];
            }

            float largestGap = float.MinValue;
            float separatorX = sortedXs[sortedXs.Count / 2];
            for (int i = 1; i < sortedXs.Count; i++)
            {
                float gap = sortedXs[i] - sortedXs[i - 1];
                if (gap <= largestGap)
                {
                    continue;
                }

                largestGap = gap;
                separatorX = (sortedXs[i] + sortedXs[i - 1]) * 0.5f;
            }

            return separatorX;
        }

        private static StarterSide ResolveSide(BoardNode node, float separatorX)
        {
            return node.LocalPosition.x < separatorX ? StarterSide.Left : StarterSide.Right;
        }

        private void DespawnExistingCharacters()
        {
            foreach (KeyValuePair<int, List<NetworkCharacterPawn>> pair in charactersByPlayerSlot)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (pair.Value[i] != null)
                    {
                        NetworkServer.Destroy(pair.Value[i].gameObject);
                    }
                }
            }
        }

        [Server]
        private void SyncPlayerCharacters(NetworkPlayerConnection player)
        {
            if (player == null)
            {
                return;
            }

            if (!charactersByPlayerSlot.TryGetValue(player.PlayerSlot, out List<NetworkCharacterPawn> roster))
            {
                player.SetCharacterSnapshots(System.Array.Empty<CharacterSnapshot>());
                return;
            }

            List<CharacterSnapshot> snapshots = new(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                NetworkCharacterPawn pawn = roster[i];
                if (pawn == null)
                {
                    continue;
                }

                snapshots.Add(new CharacterSnapshot
                {
                    NetId = pawn.netId,
                    OwnerSlot = pawn.OwnerSlot,
                    CharacterType = pawn.CharacterType,
                    DisplayName = pawn.DisplayName,
                    CurrentNodeId = pawn.CurrentNodeId
                });
            }

            player.SetCharacterSnapshots(snapshots);
        }

        private static string ResolveDisplayName(CharacterType type)
        {
            return type switch
            {
                CharacterType.Blacksmith => "Кузнец",
                CharacterType.BlacksmithAssistant => "Помощник",
                CharacterType.Warrior => "Воин",
                CharacterType.Hunter => "Охотник",
                CharacterType.Shaman => "Шаман",
                _ => type.ToString()
            };
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
