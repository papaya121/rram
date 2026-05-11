using System.Globalization;
using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Converts internal node ids into human-readable labels for the prototype UI.
    /// </summary>
    public static class BoardNodeDisplayUtility
    {
        /// <summary>
        /// Returns a full node name for HUD and logs.
        /// </summary>
        public static string GetDisplayName(string nodeId)
        {
            string authoredLabel = BoardGraph.Instance != null ? BoardGraph.Instance.GetDisplayName(nodeId) : null;
            if (!string.IsNullOrWhiteSpace(authoredLabel))
            {
                return authoredLabel;
            }

            if (TryGetStarterNodeLabel(nodeId, shortForm: false, out string starterLabel))
            {
                return starterLabel;
            }

            if (TryGetOrdinal(nodeId, out int ordinal))
            {
                return $"Точка {ordinal}";
            }

            return string.IsNullOrWhiteSpace(nodeId) ? "Неизвестная точка" : nodeId;
        }

        /// <summary>
        /// Returns a compact label suitable for world-space markers.
        /// </summary>
        public static string GetShortLabel(string nodeId)
        {
            string authoredLabel = BoardGraph.Instance != null ? BoardGraph.Instance.GetShortLabel(nodeId) : null;
            if (!string.IsNullOrWhiteSpace(authoredLabel))
            {
                return authoredLabel;
            }

            if (TryGetStarterNodeLabel(nodeId, shortForm: true, out string starterLabel))
            {
                return starterLabel;
            }

            if (TryGetOrdinal(nodeId, out int ordinal))
            {
                return ordinal.ToString(CultureInfo.InvariantCulture);
            }

            return string.IsNullOrWhiteSpace(nodeId) ? "?" : nodeId;
        }

        public static string GetCharacterShortLabel(CharacterType type)
        {
            return type switch
            {
                CharacterType.Blacksmith => "К",
                CharacterType.BlacksmithAssistant => "П",
                CharacterType.Warrior => "В",
                CharacterType.Hunter => "О",
                CharacterType.Shaman => "Ш",
                _ => "?"
            };
        }

        public static string GetCharacterDisplayName(CharacterType type)
        {
            return type switch
            {
                CharacterType.Blacksmith => "Кузнец",
                CharacterType.BlacksmithAssistant => "Помощник кузнеца",
                CharacterType.Warrior => "Воин",
                CharacterType.Hunter => "Охотник",
                CharacterType.Shaman => "Шаман",
                _ => "Старт"
            };
        }

        private static bool TryGetOrdinal(string nodeId, out int ordinal)
        {
            ordinal = 0;
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length < 2 || nodeId[0] != 'N')
            {
                return false;
            }

            if (!int.TryParse(nodeId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawIndex))
            {
                return false;
            }

            ordinal = rawIndex + 1;
            return true;
        }

        private static bool TryGetStarterNodeLabel(string nodeId, bool shortForm, out string label)
        {
            label = null;
            if (BoardGraph.Instance == null || !BoardGraph.Instance.TryGetNode(nodeId, out BoardNode node) || !node.IsStarterNode)
            {
                return false;
            }

            label = shortForm
                ? GetCharacterShortLabel(node.StarterCharacterType)
                : GetCharacterDisplayName(node.StarterCharacterType);
            return true;
        }
    }

    public static class BoardNodeVisualUtility
    {
        public static readonly Color DefaultNodeColor = new(0.9f, 0.88f, 0.79f);
        public static readonly Color StarterNodeColor = new(0.95f, 0.79f, 0.29f);
        public static readonly Color GreenDeckNodeColor = new(0.32f, 0.74f, 0.35f);
        public static readonly Color RedDeckNodeColor = new(0.82f, 0.26f, 0.22f);
        public static readonly Color TeleportNodeColor = new(0.66f, 0.37f, 0.86f);
        public static readonly Color CustomNodeColor = new(0.32f, 0.7f, 0.84f);

        public static bool TryGetSpecialNodeColor(BoardNodeKind nodeKind, out Color color)
        {
            color = nodeKind switch
            {
                BoardNodeKind.GreenDeck => GreenDeckNodeColor,
                BoardNodeKind.RedDeck => RedDeckNodeColor,
                BoardNodeKind.Teleport => TeleportNodeColor,
                BoardNodeKind.Custom => CustomNodeColor,
                _ => default
            };

            return nodeKind != BoardNodeKind.Normal;
        }

        public static Color GetAuthoredNodeColor(BoardNode node)
        {
            if (node == null)
            {
                return DefaultNodeColor;
            }

            return GetAuthoredNodeColor(node.NodeKind, node.IsStarterNode);
        }

        public static Color GetAuthoredNodeColor(BoardNodeKind nodeKind, bool isStarterNode)
        {
            if (isStarterNode)
            {
                return StarterNodeColor;
            }

            return TryGetSpecialNodeColor(nodeKind, out Color color)
                ? color
                : DefaultNodeColor;
        }
    }
}
