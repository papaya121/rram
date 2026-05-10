using System;
using System.Collections.Generic;
using RRaM.Core.Characters;
using UnityEngine;

namespace RRaM.Core.Board
{
    [Serializable]
    public sealed class BoardNode
    {
        [SerializeField] private string nodeId;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private string displayName;
        [SerializeField] private string shortLabel;
        [SerializeField] private BoardNodeKind nodeKind;
        [SerializeField] private bool isStarterNode;
        [SerializeField] private CharacterType starterCharacterType;
        [SerializeField] private List<string> neighbours = new();

        public string NodeId => nodeId;
        public Vector3 LocalPosition => localPosition;
        public string DisplayName => displayName;
        public string ShortLabel => shortLabel;
        public BoardNodeKind NodeKind => nodeKind;
        public bool IsStarterNode => isStarterNode;
        public CharacterType StarterCharacterType => starterCharacterType;
        public IReadOnlyList<string> Neighbours => neighbours;

        public BoardNode(string nodeId, Vector3 localPosition)
        {
            this.nodeId = nodeId;
            this.localPosition = localPosition;
        }

        public BoardNode(
            string nodeId,
            Vector3 localPosition,
            string displayName,
            string shortLabel,
            BoardNodeKind nodeKind,
            bool isStarterNode,
            CharacterType starterCharacterType)
        {
            this.nodeId = nodeId;
            this.localPosition = localPosition;
            this.displayName = displayName;
            this.shortLabel = shortLabel;
            this.nodeKind = nodeKind;
            this.isStarterNode = isStarterNode;
            this.starterCharacterType = starterCharacterType;
        }

        public void AddNeighbour(string neighbourId)
        {
            if (!neighbours.Contains(neighbourId))
            {
                neighbours.Add(neighbourId);
            }
        }
    }
}
