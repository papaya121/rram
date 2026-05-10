using System.Globalization;
using RRaM.Core.Characters;

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
}
