using System.Collections.Generic;
using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Stores the map graph and predefined dwarf routes.
    /// </summary>
    public sealed class BoardGraph : MonoBehaviour
    {
        public static BoardGraph Instance { get; private set; }

        [SerializeField] private List<BoardNode> nodes = new();
        [SerializeField] private List<DwarfRouteDefinition> dwarfRoutes = new();
        [Header("Authoring")]
        [SerializeField] private Transform authoringRoot;
        [SerializeField] private string autoNodePrefix = "N";
        [SerializeField] private int autoNodeStartIndex = 1;
        [SerializeField] private float autoLinkDistanceOverride;
        [SerializeField] private float autoLinkDistanceMultiplier = 1.2f;
        [SerializeField] private bool autoRenameAnchorObjects = true;

        private readonly Dictionary<string, BoardNode> nodesById = new();
        private readonly Dictionary<string, BoardNodeAnchor> anchorsById = new();
        private bool isInitialized;

        public IReadOnlyList<BoardNode> Nodes => nodes;
        public IReadOnlyList<DwarfRouteDefinition> DwarfRoutes => dwarfRoutes;
        public bool UsesAuthoredAnchors { get; private set; }
        public Transform AuthoringRoot => authoringRoot != null ? authoringRoot : transform;
        public string AutoNodePrefix => string.IsNullOrWhiteSpace(autoNodePrefix) ? "N" : autoNodePrefix.Trim();
        public int AutoNodeStartIndex => Mathf.Max(0, autoNodeStartIndex);
        public float AutoLinkDistanceOverride => Mathf.Max(0f, autoLinkDistanceOverride);
        public float AutoLinkDistanceMultiplier => Mathf.Max(1.01f, autoLinkDistanceMultiplier);
        public bool AutoRenameAnchorObjects => autoRenameAnchorObjects;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Board] Duplicate board graph detected. Destroying the newer instance.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureInitialized();
            if (Application.isPlaying && GetComponent<BoardInteractionController>() == null)
            {
                gameObject.AddComponent<BoardInteractionController>();
            }

            if (Application.isPlaying && GetComponent<BoardNodeOccupancyController>() == null)
            {
                gameObject.AddComponent<BoardNodeOccupancyController>();
            }
        }

        /// <summary>
        /// Builds a small default graph if no authored graph is present.
        /// </summary>
        public void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            if (TryBuildGraphFromAnchors())
            {
                UsesAuthoredAnchors = true;
                RebuildLookup();
                isInitialized = true;
                return;
            }

            UsesAuthoredAnchors = false;
            anchorsById.Clear();
            if (nodes.Count == 0)
            {
                BuildDefaultGraph();
            }

            RebuildLookup();
            isInitialized = true;
        }

        public void InvalidateInitialization()
        {
            isInitialized = false;
        }

        /// <summary>
        /// Tries to get a board node by id.
        /// </summary>
        public bool TryGetNode(string nodeId, out BoardNode node)
        {
            EnsureInitialized();
            return nodesById.TryGetValue(nodeId, out node);
        }

        public bool TryGetAnchor(string nodeId, out BoardNodeAnchor anchor)
        {
            EnsureInitialized();
            return anchorsById.TryGetValue(nodeId, out anchor);
        }

        /// <summary>
        /// Returns a node world position for visuals and pathing.
        /// </summary>
        public Vector3 GetWorldPosition(string nodeId)
        {
            EnsureInitialized();
            if (TryGetNode(nodeId, out BoardNode node))
            {
                return transform.TransformPoint(node.LocalPosition);
            }

            return transform.position;
        }

        /// <summary>
        /// Collects all destination ids reachable within the given step budget.
        /// </summary>
        public List<string> GetReachableDestinations(string startNodeId, int maxSteps)
        {
            EnsureInitialized();
            List<string> results = new();
            if (maxSteps < 0 || !TryGetNode(startNodeId, out BoardNode startNode))
            {
                return results;
            }

            Queue<string> frontier = new();
            Dictionary<string, int> distances = new()
            {
                [startNodeId] = 0
            };

            frontier.Enqueue(startNodeId);
            while (frontier.Count > 0)
            {
                string currentNodeId = frontier.Dequeue();
                if (!TryGetNode(currentNodeId, out BoardNode currentNode))
                {
                    continue;
                }

                int currentDistance = distances[currentNodeId];
                for (int i = 0; i < currentNode.Neighbours.Count; i++)
                {
                    string neighbourId = currentNode.Neighbours[i];
                    int nextDistance = currentDistance + 1;
                    if (nextDistance > maxSteps || distances.ContainsKey(neighbourId))
                    {
                        continue;
                    }

                    distances[neighbourId] = nextDistance;
                    frontier.Enqueue(neighbourId);
                    results.Add(neighbourId);
                }
            }

            results.Sort();
            return results;
        }

        /// <summary>
        /// Finds the shortest path between two board nodes.
        /// </summary>
        public bool TryGetShortestPath(string startNodeId, string destinationNodeId, out List<string> path)
        {
            EnsureInitialized();
            path = new List<string>();
            if (string.IsNullOrWhiteSpace(startNodeId) ||
                string.IsNullOrWhiteSpace(destinationNodeId) ||
                !TryGetNode(startNodeId, out BoardNode startNode) ||
                !TryGetNode(destinationNodeId, out _))
            {
                return false;
            }

            Queue<string> frontier = new();
            Dictionary<string, string> cameFrom = new()
            {
                [startNodeId] = null
            };

            frontier.Enqueue(startNodeId);
            while (frontier.Count > 0)
            {
                string currentNodeId = frontier.Dequeue();
                if (currentNodeId == destinationNodeId)
                {
                    break;
                }

                if (!TryGetNode(currentNodeId, out BoardNode currentNode))
                {
                    continue;
                }

                for (int i = 0; i < currentNode.Neighbours.Count; i++)
                {
                    string neighbourId = currentNode.Neighbours[i];
                    if (cameFrom.ContainsKey(neighbourId))
                    {
                        continue;
                    }

                    cameFrom[neighbourId] = currentNodeId;
                    frontier.Enqueue(neighbourId);
                }
            }

            if (!cameFrom.ContainsKey(destinationNodeId))
            {
                return false;
            }

            string stepNodeId = destinationNodeId;
            while (stepNodeId != null)
            {
                path.Add(stepNodeId);
                stepNodeId = cameFrom[stepNodeId];
            }

            path.Reverse();
            return path.Count > 0 && path[0] == startNode.NodeId;
        }

        public string GetDisplayName(string nodeId)
        {
            EnsureInitialized();
            if (TryGetNode(nodeId, out BoardNode node) && !string.IsNullOrWhiteSpace(node.DisplayName))
            {
                return node.DisplayName;
            }

            return null;
        }

        public string GetShortLabel(string nodeId)
        {
            EnsureInitialized();
            if (TryGetNode(nodeId, out BoardNode node) && !string.IsNullOrWhiteSpace(node.ShortLabel))
            {
                return node.ShortLabel;
            }

            return null;
        }

        private bool TryBuildGraphFromAnchors()
        {
            anchorsById.Clear();
            Transform root = AuthoringRoot;
            BoardNodeAnchor[] anchors = root != null ? root.GetComponentsInChildren<BoardNodeAnchor>(true) : GetComponentsInChildren<BoardNodeAnchor>(true);
            if (anchors == null || anchors.Length == 0)
            {
                return false;
            }

            List<BoardNodeAnchor> orderedAnchors = new(anchors);
            orderedAnchors.Sort((left, right) => string.CompareOrdinal(left.NodeId, right.NodeId));

            Dictionary<BoardNodeAnchor, BoardNode> nodesByAnchor = new();
            nodes = new List<BoardNode>(orderedAnchors.Count);
            for (int i = 0; i < orderedAnchors.Count; i++)
            {
                BoardNodeAnchor anchor = orderedAnchors[i];
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.NodeId))
                {
                    continue;
                }

                if (nodes.Exists(node => node.NodeId == anchor.NodeId))
                {
                    Debug.LogWarning($"[Board] Duplicate node id '{anchor.NodeId}' on authored map.", anchor);
                    continue;
                }

                BoardNode node = new(
                    anchor.NodeId,
                    transform.InverseTransformPoint(anchor.transform.position),
                    anchor.DisplayName,
                    anchor.ShortLabel,
                    anchor.NodeKind,
                    anchor.IsStarterNode,
                    anchor.StarterCharacterType);
                nodes.Add(node);
                nodesByAnchor[anchor] = node;
                anchorsById[anchor.NodeId] = anchor;
            }

            foreach (KeyValuePair<BoardNodeAnchor, BoardNode> pair in nodesByAnchor)
            {
                IReadOnlyList<BoardNodeAnchor> neighbours = pair.Key.Neighbours;
                for (int i = 0; i < neighbours.Count; i++)
                {
                    BoardNodeAnchor neighbourAnchor = neighbours[i];
                    if (neighbourAnchor == null || !nodesByAnchor.TryGetValue(neighbourAnchor, out BoardNode neighbourNode))
                    {
                        continue;
                    }

                    pair.Value.AddNeighbour(neighbourNode.NodeId);
                }
            }

            return nodes.Count > 0;
        }

        public List<BoardNodeAnchor> GetDirectAuthoringAnchors()
        {
            Transform root = AuthoringRoot;
            List<BoardNodeAnchor> anchors = new();
            if (root == null)
            {
                return anchors;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                BoardNodeAnchor anchor = child.GetComponent<BoardNodeAnchor>();
                if (anchor != null)
                {
                    anchors.Add(anchor);
                }
            }

            return anchors;
        }

        public float ResolveAutoLinkDistance(IReadOnlyList<BoardNodeAnchor> anchors)
        {
            if (AutoLinkDistanceOverride > 0f)
            {
                return AutoLinkDistanceOverride;
            }

            if (anchors == null || anchors.Count < 2)
            {
                return 0f;
            }

            List<float> nearestDistances = new(anchors.Count);
            for (int i = 0; i < anchors.Count; i++)
            {
                BoardNodeAnchor current = anchors[i];
                if (current == null)
                {
                    continue;
                }

                float nearest = float.MaxValue;
                Vector3 currentPosition = current.transform.position;
                for (int j = 0; j < anchors.Count; j++)
                {
                    if (i == j || anchors[j] == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(currentPosition, anchors[j].transform.position);
                    if (distance > 0.0001f && distance < nearest)
                    {
                        nearest = distance;
                    }
                }

                if (nearest < float.MaxValue)
                {
                    nearestDistances.Add(nearest);
                }
            }

            if (nearestDistances.Count == 0)
            {
                return 0f;
            }

            nearestDistances.Sort();
            float median = nearestDistances[nearestDistances.Count / 2];
            return median * AutoLinkDistanceMultiplier;
        }

        private void Reset()
        {
            authoringRoot = transform;
            isInitialized = false;
        }

        private void OnValidate()
        {
            autoLinkDistanceMultiplier = Mathf.Max(1.01f, autoLinkDistanceMultiplier);
            autoNodeStartIndex = Mathf.Max(0, autoNodeStartIndex);
            isInitialized = false;
        }

        private void RebuildLookup()
        {
            nodesById.Clear();
            for (int i = 0; i < nodes.Count; i++)
            {
                nodesById[nodes[i].NodeId] = nodes[i];
            }
        }

        private void BuildDefaultGraph()
        {
            nodes.Clear();
            dwarfRoutes.Clear();

            const int nodeCount = 12;
            float radius = 7.5f;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = Mathf.PI * 2f * i / nodeCount;
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                nodes.Add(new BoardNode($"N{i:00}", position));
            }

            for (int i = 0; i < nodeCount; i++)
            {
                Link(i, (i + 1) % nodeCount);
            }

            Link(1, 5);
            Link(3, 9);
            Link(7, 11);

            dwarfRoutes.Add(new DwarfRouteDefinition
            {
                RouteId = "clockwise",
                LowerRoute = new List<string> { "N00", "N01", "N02", "N03", "N04" },
                UpperRoute = new List<string> { "N04", "N05", "N06", "N07" },
                ReturnRoute = new List<string> { "N07", "N08", "N09", "N10", "N11", "N00" }
            });

            dwarfRoutes.Add(new DwarfRouteDefinition
            {
                RouteId = "counter_clockwise",
                LowerRoute = new List<string> { "N06", "N05", "N04", "N03", "N02" },
                UpperRoute = new List<string> { "N02", "N01", "N00", "N11" },
                ReturnRoute = new List<string> { "N11", "N10", "N09", "N08", "N07", "N06" }
            });
        }

        private void Link(int a, int b)
        {
            BoardNode nodeA = nodes[a];
            BoardNode nodeB = nodes[b];
            nodeA.AddNeighbour(nodeB.NodeId);
            nodeB.AddNeighbour(nodeA.NodeId);
        }
    }
}
