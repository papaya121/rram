using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RRaM.Core.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RRaM.Editor
{
    [CustomEditor(typeof(BoardGraph))]
    public sealed class BoardGraphEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            BoardGraph graph = (BoardGraph)target;
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Auto Configure uses direct children of Authoring Root. Keep only marker objects in that container.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Auto Configure Anchors", GUILayout.Height(28f)))
                {
                    AutoConfigureAnchors(graph);
                }

                if (GUILayout.Button("Rebuild Visuals", GUILayout.Height(28f)))
                {
                    RebuildVisuals(graph);
                }
            }
        }

        private static void AutoConfigureAnchors(BoardGraph graph)
        {
            if (graph == null)
            {
                return;
            }

            Transform root = graph.AuthoringRoot;
            if (root == null)
            {
                Debug.LogWarning("[Board] Missing authoring root.", graph);
                return;
            }

            List<Transform> markerTransforms = new();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                markerTransforms.Add(child);
            }

            if (markerTransforms.Count == 0)
            {
                Debug.LogWarning("[Board] Authoring root has no direct children to configure.", graph);
                return;
            }

            Undo.SetCurrentGroupName("Auto Configure Board Anchors");

            List<BoardNodeAnchor> anchors = new(markerTransforms.Count);
            for (int i = 0; i < markerTransforms.Count; i++)
            {
                Transform marker = markerTransforms[i];
                BoardNodeAnchor anchor = marker.GetComponent<BoardNodeAnchor>();
                if (anchor == null)
                {
                    anchor = Undo.AddComponent<BoardNodeAnchor>(marker.gameObject);
                }

                anchors.Add(anchor);
            }

            anchors = anchors
                .Where(anchor => anchor != null)
                .OrderByDescending(anchor => anchor.transform.position.z)
                .ThenBy(anchor => anchor.transform.position.x)
                .ThenBy(anchor => anchor.transform.position.y)
                .ToList();

            int firstIndex = graph.AutoNodeStartIndex;
            int width = Mathf.Max(2, (firstIndex + anchors.Count).ToString(CultureInfo.InvariantCulture).Length);
            for (int i = 0; i < anchors.Count; i++)
            {
                int index = firstIndex + i;
                string nodeId = $"{graph.AutoNodePrefix}{index.ToString($"D{width}", CultureInfo.InvariantCulture)}";
                anchors[i].ConfigureIdentity(
                    nodeId,
                    $"Точка {index}",
                    index.ToString(CultureInfo.InvariantCulture));

                if (graph.AutoRenameAnchorObjects)
                {
                    anchors[i].gameObject.name = nodeId;
                }

                EditorUtility.SetDirty(anchors[i]);
            }

            float linkDistance = graph.ResolveAutoLinkDistance(anchors);
            if (linkDistance <= 0f)
            {
                Debug.LogWarning("[Board] Could not infer link distance. Add more markers or set Auto Link Distance Override.", graph);
                return;
            }

            for (int i = 0; i < anchors.Count; i++)
            {
                List<BoardNodeAnchor> neighbours = new();
                Vector3 origin = anchors[i].transform.position;
                for (int j = 0; j < anchors.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(origin, anchors[j].transform.position);
                    if (distance <= linkDistance)
                    {
                        neighbours.Add(anchors[j]);
                    }
                }

                anchors[i].SetNeighbours(neighbours);
                EditorUtility.SetDirty(anchors[i]);
            }

            EditorUtility.SetDirty(graph);
            EditorSceneManager.MarkSceneDirty(graph.gameObject.scene);
            RebuildVisuals(graph);

            Debug.Log($"[Board] Auto configured {anchors.Count} anchors. Link distance={linkDistance:F3}", graph);
        }

        private static void RebuildVisuals(BoardGraph graph)
        {
            if (graph == null)
            {
                return;
            }

            graph.EnsureInitialized();
            BoardPresentation presentation = graph.GetComponent<BoardPresentation>();
            if (presentation != null)
            {
                presentation.BuildVisuals();
                EditorUtility.SetDirty(presentation);
            }

            EditorUtility.SetDirty(graph);
            EditorSceneManager.MarkSceneDirty(graph.gameObject.scene);
        }
    }
}
