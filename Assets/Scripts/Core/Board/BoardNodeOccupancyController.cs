using System.Collections.Generic;
using System.Linq;
using System.Text;
using RRaM.Core.Characters;
using RRaM.Core.Dwarfs;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Board
{
    [RequireComponent(typeof(BoardGraph))]
    public sealed class BoardNodeOccupancyController : MonoBehaviour
    {
        private sealed class OccupancyState
        {
            public readonly HashSet<CharacterType> CharacterTypes = new();
            public readonly HashSet<int> OwnerSlots = new();
            public int DwarfCount;
        }

        private readonly Dictionary<string, BoardNodeAnchor> anchorsByNodeId = new();
        private readonly Dictionary<string, string> lastLabelsByNodeId = new();
        private readonly Dictionary<string, Color> lastColorsByNodeId = new();
        private readonly Dictionary<string, bool> lastSelectionByNodeId = new();
        private readonly Dictionary<string, OccupancyState> occupancyByNodeId = new();

        private static readonly Color EmptyColor = Color.clear;
        private static readonly Color MixedOwnerColor = new(0.94f, 0.94f, 0.94f, 1f);
        private static readonly Color PlayerOneColor = new(0.15f, 0.52f, 1f, 1f);
        private static readonly Color PlayerTwoColor = new(1f, 0.58f, 0.12f, 1f);
        private static readonly Color DwarfColor = new(0.58f, 0.82f, 0.86f, 1f);
        private const float RefreshInterval = 0.1f;

        private BoardGraph boardGraph;
        private float nextRefreshTime;

        private void Awake()
        {
            boardGraph = GetComponent<BoardGraph>();
            boardGraph.EnsureInitialized();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RebuildAnchorCacheIfNeeded();
            RefreshOccupancy();
        }

        private void RebuildAnchorCacheIfNeeded()
        {
            if (anchorsByNodeId.Count == boardGraph.Nodes.Count)
            {
                return;
            }

            anchorsByNodeId.Clear();
            lastLabelsByNodeId.Clear();
            lastColorsByNodeId.Clear();
            lastSelectionByNodeId.Clear();

            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                BoardNode node = boardGraph.Nodes[i];
                if (boardGraph.TryGetAnchor(node.NodeId, out BoardNodeAnchor anchor) && anchor != null)
                {
                    anchorsByNodeId[node.NodeId] = anchor;
                }
            }
        }

        private void RefreshOccupancy()
        {
            occupancyByNodeId.Clear();
            CollectCharacters();
            CollectDwarfs();

            string selectedNodeId = ResolveSelectedNodeId();
            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                string nodeId = boardGraph.Nodes[i].NodeId;
                if (!anchorsByNodeId.TryGetValue(nodeId, out BoardNodeAnchor anchor) || anchor == null)
                {
                    continue;
                }

                string nextLabel = BuildLabel(nodeId);
                Color nextColor = ResolveColor(nodeId);
                bool isSelected = nodeId == selectedNodeId;

                lastLabelsByNodeId.TryGetValue(nodeId, out string previousLabel);
                lastColorsByNodeId.TryGetValue(nodeId, out Color previousColor);
                lastSelectionByNodeId.TryGetValue(nodeId, out bool previousSelection);
                if (previousLabel == nextLabel && previousColor == nextColor && previousSelection == isSelected)
                {
                    continue;
                }

                bool occupied = !string.IsNullOrEmpty(nextLabel);
                anchor.ApplyOccupancyState(nextLabel, occupied, nextColor, isSelected);
                lastLabelsByNodeId[nodeId] = nextLabel;
                lastColorsByNodeId[nodeId] = nextColor;
                lastSelectionByNodeId[nodeId] = isSelected;
            }
        }

        private void CollectCharacters()
        {
            NetworkCharacterPawn[] pawns = FindObjectsByType<NetworkCharacterPawn>();
            for (int i = 0; i < pawns.Length; i++)
            {
                NetworkCharacterPawn pawn = pawns[i];
                if (pawn == null || string.IsNullOrWhiteSpace(pawn.CurrentNodeId) || !anchorsByNodeId.ContainsKey(pawn.CurrentNodeId))
                {
                    continue;
                }

                OccupancyState state = GetOrCreateState(pawn.CurrentNodeId);
                state.CharacterTypes.Add(pawn.CharacterType);
                state.OwnerSlots.Add(pawn.OwnerSlot);
            }
        }

        private void CollectDwarfs()
        {
            NetworkDwarfPawn[] dwarfs = FindObjectsByType<NetworkDwarfPawn>();
            for (int i = 0; i < dwarfs.Length; i++)
            {
                NetworkDwarfPawn dwarf = dwarfs[i];
                if (dwarf == null || string.IsNullOrWhiteSpace(dwarf.CurrentNodeId) || !anchorsByNodeId.ContainsKey(dwarf.CurrentNodeId))
                {
                    continue;
                }

                OccupancyState state = GetOrCreateState(dwarf.CurrentNodeId);
                state.DwarfCount++;
            }
        }

        private OccupancyState GetOrCreateState(string nodeId)
        {
            if (!occupancyByNodeId.TryGetValue(nodeId, out OccupancyState state))
            {
                state = new OccupancyState();
                occupancyByNodeId[nodeId] = state;
            }

            return state;
        }

        private string ResolveSelectedNodeId()
        {
            LocalPlayerController local = LocalPlayerController.Instance;
            if (local?.Player == null || local.Player.SelectedCharacterNetId == 0)
            {
                return null;
            }

            CharacterSnapshot selected = local.Player.Characters.FirstOrDefault(character => character.NetId == local.Player.SelectedCharacterNetId);
            return selected.NetId == 0 || string.IsNullOrWhiteSpace(selected.CurrentNodeId)
                ? null
                : selected.CurrentNodeId;
        }

        private string BuildLabel(string nodeId)
        {
            if (!occupancyByNodeId.TryGetValue(nodeId, out OccupancyState state))
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            AppendIfPresent(builder, state.CharacterTypes, CharacterType.Blacksmith);
            AppendIfPresent(builder, state.CharacterTypes, CharacterType.BlacksmithAssistant);
            AppendIfPresent(builder, state.CharacterTypes, CharacterType.Warrior);
            AppendIfPresent(builder, state.CharacterTypes, CharacterType.Hunter);
            AppendIfPresent(builder, state.CharacterTypes, CharacterType.Shaman);
            if (state.DwarfCount > 0)
            {
                builder.Append('Д');
            }

            return builder.ToString();
        }

        private Color ResolveColor(string nodeId)
        {
            if (!occupancyByNodeId.TryGetValue(nodeId, out OccupancyState state))
            {
                return EmptyColor;
            }

            bool hasHeroes = state.OwnerSlots.Count > 0;
            bool hasDwarfs = state.DwarfCount > 0;
            if (!hasHeroes && !hasDwarfs)
            {
                return EmptyColor;
            }

            if (hasHeroes && state.OwnerSlots.Count == 1 && !hasDwarfs)
            {
                int ownerSlot = state.OwnerSlots.First();
                return ownerSlot == 0 ? PlayerOneColor : PlayerTwoColor;
            }

            if (!hasHeroes && hasDwarfs)
            {
                return DwarfColor;
            }

            return MixedOwnerColor;
        }

        private static void AppendIfPresent(StringBuilder builder, HashSet<CharacterType> occupants, CharacterType type)
        {
            if (occupants.Contains(type))
            {
                builder.Append(BoardNodeDisplayUtility.GetCharacterShortLabel(type));
            }
        }
    }
}
