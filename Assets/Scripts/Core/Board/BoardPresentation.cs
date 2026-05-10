using System.Collections.Generic;
using UnityEngine;

namespace RRaM.Core.Board
{
    /// <summary>
    /// Builds a presentable runtime board from the logical graph.
    /// </summary>
    [RequireComponent(typeof(BoardGraph))]
    public sealed class BoardPresentation : MonoBehaviour
    {
        [SerializeField] private Transform visualsRoot;
        [SerializeField] private bool showConnections = true;
        [SerializeField] private bool showNodeLabels = true;

        private readonly List<Material> runtimeMaterials = new();
        private BoardGraph boardGraph;

        private void Awake()
        {
            boardGraph = GetComponent<BoardGraph>();
            EnsureVisuals();
        }

        /// <summary>
        /// Rebuilds the board visuals from the current graph.
        /// </summary>
        public void BuildVisuals()
        {
            boardGraph ??= GetComponent<BoardGraph>();
            boardGraph.EnsureInitialized();

            if (visualsRoot != null)
            {
                DestroyRuntimeMaterials();
                DestroyObjectImmediateSafe(visualsRoot.gameObject);
            }

            visualsRoot = new GameObject("Board Visuals").transform;
            visualsRoot.SetParent(transform, false);

            bool isAuthoredLayout = boardGraph.UsesAuthoredAnchors;
            if (!isAuthoredLayout)
            {
                CreateStage();
            }

            if (showConnections)
            {
                CreateConnections(isAuthoredLayout);
            }

            CreateNodes(isAuthoredLayout);

            if (!isAuthoredLayout)
            {
                CreateDecor();
            }
        }

        [ContextMenu("Rebuild Visuals")]
        private void RebuildVisualsContextMenu()
        {
            BuildVisuals();
        }

        private void EnsureVisuals()
        {
            boardGraph ??= GetComponent<BoardGraph>();
            boardGraph.EnsureInitialized();

            if (visualsRoot == null)
            {
                Transform existingRoot = transform.Find("Board Visuals");
                if (existingRoot != null)
                {
                    visualsRoot = existingRoot;
                }
            }

            if (visualsRoot == null || visualsRoot.childCount == 0)
            {
                BuildVisuals();
            }
        }

        private void CreateStage()
        {
            Material floorMaterial = CreateMaterial(new Color(0.11f, 0.12f, 0.14f), 0.35f);
            Material outerRingMaterial = CreateMaterial(new Color(0.64f, 0.58f, 0.46f), 0.5f);
            Material innerRingMaterial = CreateMaterial(new Color(0.19f, 0.24f, 0.29f), 0.2f, new Color(0.05f, 0.08f, 0.09f));
            Material centerMaterial = CreateMaterial(new Color(0.34f, 0.28f, 0.23f), 0.1f);

            GameObject floor = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Plane, "Floor", visualsRoot, floorMaterial);
            floor.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            floor.transform.localScale = new Vector3(5f, 1f, 5f);

            GameObject outerRing = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Outer Ring", visualsRoot, outerRingMaterial);
            outerRing.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            outerRing.transform.localScale = new Vector3(10.8f, 0.16f, 10.8f);

            GameObject innerRing = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Inner Ring", visualsRoot, innerRingMaterial);
            innerRing.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            innerRing.transform.localScale = new Vector3(8.7f, 0.05f, 8.7f);

            GameObject centerPlate = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Center Plate", visualsRoot, centerMaterial);
            centerPlate.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            centerPlate.transform.localScale = new Vector3(3.8f, 0.025f, 3.8f);
        }

        private void CreateConnections(bool isAuthoredLayout)
        {
            Color baseColor = isAuthoredLayout ? new Color(0.91f, 0.89f, 0.84f) : new Color(0.31f, 0.25f, 0.19f);
            Color accentColor = isAuthoredLayout ? new Color(0.58f, 0.43f, 0.22f) : new Color(0.82f, 0.68f, 0.43f);
            Material baseMaterial = CreateMaterial(baseColor, isAuthoredLayout ? 0.05f : 0.15f);
            Material accentMaterial = CreateMaterial(accentColor, 0.6f, isAuthoredLayout ? accentColor * 0.02f : new Color(0.12f, 0.08f, 0.02f));
            HashSet<string> builtEdges = new();

            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                BoardNode node = boardGraph.Nodes[i];
                Vector3 start = boardGraph.GetWorldPosition(node.NodeId);
                for (int j = 0; j < node.Neighbours.Count; j++)
                {
                    string neighbourId = node.Neighbours[j];
                    string edgeKey = string.CompareOrdinal(node.NodeId, neighbourId) < 0
                        ? $"{node.NodeId}:{neighbourId}"
                        : $"{neighbourId}:{node.NodeId}";
                    if (!builtEdges.Add(edgeKey) || !boardGraph.TryGetNode(neighbourId, out _))
                    {
                        continue;
                    }

                    Vector3 end = boardGraph.GetWorldPosition(neighbourId);
                    if (isAuthoredLayout)
                    {
                        CreateLink(edgeKey, start, end, 0.022f, baseMaterial, 0.006f);
                    }
                    else
                    {
                        CreateLink(edgeKey, start, end, 0.05f, baseMaterial, 0.015f);
                        CreateLink($"{edgeKey}_Accent", start, end, 0.018f, accentMaterial, 0.045f);
                    }
                }
            }
        }

        private void CreateNodes(bool isAuthoredLayout)
        {
            Material pedestalMaterial = CreateMaterial(new Color(0.18f, 0.16f, 0.13f), 0.1f);
            Material capMaterial = CreateMaterial(new Color(0.83f, 0.77f, 0.66f), 0.55f, new Color(0.08f, 0.08f, 0.04f));
            Material activeCapMaterial = CreateMaterial(new Color(0.39f, 0.71f, 0.78f), 0.7f, new Color(0.12f, 0.3f, 0.34f));

            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                BoardNode node = boardGraph.Nodes[i];
                Transform nodeRoot = new GameObject(node.NodeId).transform;
                nodeRoot.SetParent(visualsRoot, false);
                nodeRoot.position = boardGraph.GetWorldPosition(node.NodeId);

                if (isAuthoredLayout)
                {
                    Material authoredCapMaterial = CreateMaterial(ResolveAuthoredNodeColor(node), 0.2f, ResolveAuthoredNodeColor(node) * 0.02f);

                    GameObject authoredPedestal = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Pedestal", nodeRoot, pedestalMaterial);
                    authoredPedestal.transform.localPosition = new Vector3(0f, 0.006f, 0f);
                    authoredPedestal.transform.localScale = new Vector3(0.2f, 0.008f, 0.2f);

                    GameObject authoredTop = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Cap", nodeRoot, authoredCapMaterial);
                    authoredTop.transform.localPosition = new Vector3(0f, 0.014f, 0f);
                    authoredTop.transform.localScale = new Vector3(0.16f, 0.01f, 0.16f);

                    if (showNodeLabels)
                    {
                        PrototypeVisualFactory.CreateLabel(
                            BoardNodeDisplayUtility.GetShortLabel(node.NodeId),
                            nodeRoot,
                            new Vector3(0f, 0.36f, 0f),
                            Color.white,
                            42);
                    }

                    continue;
                }

                GameObject pedestal = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Pedestal", nodeRoot, pedestalMaterial);
                pedestal.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                pedestal.transform.localScale = new Vector3(0.55f, 0.03f, 0.55f);

                Material topMaterial = i % 3 == 0 ? activeCapMaterial : capMaterial;
                GameObject top = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Cap", nodeRoot, topMaterial);
                top.transform.localPosition = new Vector3(0f, 0.14f, 0f);
                top.transform.localScale = new Vector3(0.34f, 0.12f, 0.34f);

                if (showNodeLabels)
                {
                    PrototypeVisualFactory.CreateLabel(
                        BoardNodeDisplayUtility.GetShortLabel(node.NodeId),
                        nodeRoot,
                        new Vector3(0f, 2f, 0f),
                        new Color(0.94f, 0.9f, 0.78f),
                        56);
                }
            }
        }

        private void CreateDecor()
        {
            Material rockMaterial = CreateMaterial(new Color(0.24f, 0.22f, 0.2f), 0.08f);
            Material crystalMaterial = CreateMaterial(new Color(0.24f, 0.58f, 0.66f), 0.7f, new Color(0.08f, 0.18f, 0.2f));

            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f * Mathf.Deg2Rad;
                float radius = 11.5f + (i % 2) * 1.3f;
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                GameObject rock = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cube, $"Rock_{i}", visualsRoot, rockMaterial);
                rock.transform.localPosition = position + new Vector3(0f, 0.18f, 0f);
                rock.transform.localRotation = Quaternion.Euler(0f, i * 24f, i * 7f);
                rock.transform.localScale = new Vector3(0.7f + (i % 3) * 0.18f, 0.28f, 0.48f + (i % 2) * 0.16f);

                GameObject crystal = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, $"Crystal_{i}", visualsRoot, crystalMaterial);
                crystal.transform.localPosition = position + new Vector3(0.15f, 0.48f, -0.08f);
                crystal.transform.localRotation = Quaternion.Euler(12f, i * 31f, -8f);
                crystal.transform.localScale = new Vector3(0.14f, 0.28f + (i % 2) * 0.08f, 0.14f);
            }
        }

        private void CreateLink(string name, Vector3 start, Vector3 end, float radius, Material material, float yOffset)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            GameObject link = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, name, visualsRoot, material);
            link.transform.position = (start + end) * 0.5f + new Vector3(0f, yOffset, 0f);
            link.transform.up = delta.normalized;
            link.transform.localScale = new Vector3(radius, length * 0.5f, radius);
        }

        private Material CreateMaterial(Color color, float smoothness, Color? emission = null)
        {
            Material material = PrototypeVisualFactory.CreateSurfaceMaterial(color, smoothness, emission);
            runtimeMaterials.Add(material);
            return material;
        }

        private static Color ResolveAuthoredNodeColor(BoardNode node)
        {
            if (node.IsStarterNode)
            {
                return new Color(0.95f, 0.79f, 0.29f);
            }

            return node.NodeKind switch
            {
                BoardNodeKind.GreenDeck => new Color(0.32f, 0.74f, 0.35f),
                BoardNodeKind.RedDeck => new Color(0.82f, 0.26f, 0.22f),
                BoardNodeKind.Teleport => new Color(0.66f, 0.37f, 0.86f),
                BoardNodeKind.Custom => new Color(0.32f, 0.7f, 0.84f),
                _ => new Color(0.9f, 0.88f, 0.79f)
            };
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterials();
        }

        private void DestroyRuntimeMaterials()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    DestroyObjectImmediateSafe(runtimeMaterials[i]);
                }
            }

            runtimeMaterials.Clear();
        }

        private static void DestroyObjectImmediateSafe(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
