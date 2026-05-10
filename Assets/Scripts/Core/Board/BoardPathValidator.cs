using System.Collections.Generic;
using RRaM.Core.Match;
using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Validates board movement on the server.
    /// </summary>
    public sealed class BoardPathValidator : MonoBehaviour
    {
        [SerializeField] private BoardGraph boardGraph;

        /// <summary>
        /// Assigns the board graph used for validation.
        /// </summary>
        public void Configure(BoardGraph graph)
        {
            boardGraph = graph;
        }

        /// <summary>
        /// Finds the shortest valid path within the provided step budget.
        /// </summary>
        public bool TryFindPathWithinRange(string startNodeId, string destinationNodeId, int maxSteps, out List<string> path)
        {
            path = new List<string>();
            if (!TryResolveBoardGraph() || maxSteps < 0 || string.IsNullOrWhiteSpace(startNodeId) || string.IsNullOrWhiteSpace(destinationNodeId))
            {
                return false;
            }

            if (startNodeId == destinationNodeId)
            {
                return false;
            }

            if (!boardGraph.TryGetNode(startNodeId, out _) || !boardGraph.TryGetNode(destinationNodeId, out _))
            {
                return false;
            }

            if (!boardGraph.TryGetShortestPath(startNodeId, destinationNodeId, out path))
            {
                return false;
            }

            int usedSteps = Mathf.Max(0, path.Count - 1);
            if (usedSteps > maxSteps)
            {
                path.Clear();
                return false;
            }

            return true;
        }

        private bool TryResolveBoardGraph()
        {
            if (boardGraph != null)
            {
                boardGraph.EnsureInitialized();
                return true;
            }

            boardGraph = GetComponent<BoardGraph>();
            boardGraph ??= MatchContext.Instance != null ? MatchContext.Instance.BoardGraph : null;
            boardGraph ??= BoardGraph.Instance;
            boardGraph ??= FindAnyObjectByType<BoardGraph>();
            if (boardGraph == null)
            {
                return false;
            }

            boardGraph.EnsureInitialized();
            return true;
        }

        private void Reset()
        {
            boardGraph = GetComponent<BoardGraph>();
        }
    }
}
