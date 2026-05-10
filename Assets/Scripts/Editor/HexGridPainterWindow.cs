using System.Collections.Generic;
using RRaM.Core.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RRaM.Editor
{
    public sealed class HexGridPainterWindow : EditorWindow
    {
        private enum HexOrientation
        {
            PointyTop = 0,
            FlatTop = 1
        }

        [SerializeField] private GameObject brushPrefab;
        [SerializeField] private Transform parentRoot;
        [SerializeField] private Vector3 gridOffset;
        [SerializeField] private float hexWidth = 0.6f;
        [SerializeField] private float hexHeight = 0.7f;
        [SerializeField] private float paintHeight;
        [SerializeField] private float gridRotationDegrees;
        [SerializeField] private HexOrientation orientation = HexOrientation.PointyTop;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private int gridExtentQ = 12;
        [SerializeField] private int gridExtentR = 12;
        [SerializeField] private bool paintingEnabled;
        [SerializeField] private bool autoAddBoardNodeAnchor = true;
        [SerializeField] private bool avoidDuplicates = true;
        [SerializeField] private bool alignToParentRotation;
        [SerializeField] private float gridLineThickness = 1.75f;
        [SerializeField] private Color gridColor = new(1f, 1f, 1f, 0.32f);
        [SerializeField] private Color previewColor = new(0.2f, 0.95f, 0.45f, 0.95f);
        [SerializeField] private Color eraseColor = new(0.95f, 0.25f, 0.2f, 0.95f);
        [SerializeField] private bool hasPinnedOrigin;

        private static HexGridPainterWindow windowInstance;
        private bool pickingOriginPoint;

        [MenuItem("RRaM/Tools/Hex Grid Painter")]
        public static void OpenWindow()
        {
            HexGridPainterWindow window = GetWindow<HexGridPainterWindow>("Hex Grid Painter");
            window.minSize = new Vector2(340f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            windowInstance = this;
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            if (windowInstance == this)
            {
                windowInstance = null;
            }

            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            brushPrefab = (GameObject)EditorGUILayout.ObjectField("Brush Prefab", brushPrefab, typeof(GameObject), true);
            parentRoot = (Transform)EditorGUILayout.ObjectField("Parent Root", parentRoot, typeof(Transform), true);
            autoAddBoardNodeAnchor = EditorGUILayout.Toggle("Add BoardNodeAnchor", autoAddBoardNodeAnchor);
            avoidDuplicates = EditorGUILayout.Toggle("Avoid Duplicates", avoidDuplicates);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
            orientation = (HexOrientation)EditorGUILayout.EnumPopup("Orientation", orientation);
            float widthX100 = EditorGUILayout.FloatField("Hex Width x100", hexWidth * 100f);
            float heightX100 = EditorGUILayout.FloatField("Hex Height x100", hexHeight * 100f);
            hexWidth = Mathf.Max(0.001f, widthX100 / 100f);
            hexHeight = Mathf.Max(0.001f, heightX100 / 100f);
            paintHeight = EditorGUILayout.FloatField("Paint Height", paintHeight);
            gridOffset = EditorGUILayout.Vector3Field("Grid Offset", gridOffset);
            gridRotationDegrees = EditorGUILayout.FloatField("Grid Rotation", gridRotationDegrees);
            alignToParentRotation = EditorGUILayout.Toggle("Use Parent Rotation", alignToParentRotation);
            showGrid = EditorGUILayout.Toggle("Show Grid", showGrid);
            gridExtentQ = Mathf.Max(0, EditorGUILayout.IntField("Grid Width", gridExtentQ));
            gridExtentR = Mathf.Max(0, EditorGUILayout.IntField("Grid Height", gridExtentR));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            gridColor = EditorGUILayout.ColorField("Grid Color", gridColor);
            gridLineThickness = EditorGUILayout.Slider("Grid Thickness", gridLineThickness, 0.5f, 4f);
            previewColor = EditorGUILayout.ColorField("Paint Color", previewColor);
            eraseColor = EditorGUILayout.ColorField("Erase Color", eraseColor);

            EditorGUILayout.Space(8f);
            DrawOriginGui();

            EditorGUILayout.Space(8f);
            DrawStatusHelp();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = parentRoot != null;
                if (GUILayout.Button("Snap Children To Grid", GUILayout.Height(26f)))
                {
                    SnapChildrenToGrid();
                }

                GUI.enabled = true;
                if (GUILayout.Button(paintingEnabled ? "Stop Painting" : "Start Painting", GUILayout.Height(26f)))
                {
                    paintingEnabled = !paintingEnabled;
                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = parentRoot != null;
                if (GUILayout.Button("Clear Parent", GUILayout.Height(24f)))
                {
                    parentRoot = null;
                }

                GUI.enabled = true;
                if (GUILayout.Button("Use Selection As Parent", GUILayout.Height(24f)))
                {
                    parentRoot = Selection.activeTransform;
                }
            }

            GUI.enabled = true;
        }

        private void DrawStatusHelp()
        {
            string status = paintingEnabled ? "Painting is active in Scene View." : "Painting is disabled.";
            EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.HelpBox(
                "LMB paints. Shift + LMB erases. Ctrl/Cmd is ignored so camera navigation stays usable.",
                MessageType.None);

            if (pickingOriginPoint)
            {
                EditorGUILayout.HelpBox("Origin: click Scene View to place the center of cell 0,0.", MessageType.Info);
            }

            if (brushPrefab == null)
            {
                EditorGUILayout.HelpBox("Assign a brush prefab or template object before painting.", MessageType.Warning);
            }

            if (parentRoot == null)
            {
                EditorGUILayout.HelpBox("Assign a parent root. Placed objects will be created under it.", MessageType.Warning);
            }
        }

        private void DrawOriginGui()
        {
            EditorGUILayout.LabelField("Origin", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pick a point that should stay at the center of cell 0,0. Changing Hex Width or Hex Height will keep the grid pinned to this point.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f, 0.95f);
                if (GUILayout.Button("Pick Origin", GUILayout.Height(24f)))
                {
                    pickingOriginPoint = true;
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;

                GUI.enabled = Selection.activeTransform != null;
                if (GUILayout.Button("Origin From Selection", GUILayout.Height(24f)))
                {
                    SetOriginFromWorldPoint(Selection.activeTransform.position);
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = hasPinnedOrigin;
                if (GUILayout.Button("Clear Origin", GUILayout.Height(22f)))
                {
                    hasPinnedOrigin = false;
                    SceneView.RepaintAll();
                }

                GUI.enabled = true;
            }

            if (hasPinnedOrigin)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 originLocal = new(gridOffset.x, paintHeight, gridOffset.z);
                originLocal = EditorGUILayout.Vector3Field("Origin Local", originLocal);
                if (EditorGUI.EndChangeCheck())
                {
                    SetOriginFromLocalPoint(originLocal);
                }
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (windowInstance != this)
            {
                return;
            }

            if (showGrid)
            {
                DrawGridPreview();
            }

            DrawOriginPreview();

            Event currentEvent = Event.current;
            if (!TryGetPlanePoint(currentEvent.mousePosition, out Vector3 planePoint))
            {
                return;
            }

            if (pickingOriginPoint)
            {
                DrawOriginMarker(planePoint, "Pick Origin", new Color(0.2f, 0.75f, 1f, 0.95f));
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && !currentEvent.control && !currentEvent.command)
                {
                    SetOriginFromWorldPoint(planePoint);
                    pickingOriginPoint = false;
                    Repaint();
                    SceneView.RepaintAll();
                    currentEvent.Use();
                }

                return;
            }

            if (!TryGetHoveredCellFromPlanePoint(planePoint, out _, out Vector3 worldPosition))
            {
                return;
            }

            DrawCellPreview(worldPosition, currentEvent.shift ? eraseColor : previewColor);

            if (!paintingEnabled)
            {
                return;
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (currentEvent.type == EventType.Layout)
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && !currentEvent.control && !currentEvent.command)
            {
                if (currentEvent.shift)
                {
                    EraseAt(worldPosition);
                }
                else
                {
                    PaintAt(worldPosition);
                }

                currentEvent.Use();
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && !currentEvent.control && !currentEvent.command)
            {
                if (currentEvent.shift)
                {
                    EraseAt(worldPosition);
                }
                else
                {
                    PaintAt(worldPosition);
                }

                currentEvent.Use();
            }
        }

        private void SetOriginFromWorldPoint(Vector3 worldPoint)
        {
            SetOriginFromLocalPoint(WorldToGridLocal(worldPoint));
        }

        private void SetOriginFromLocalPoint(Vector3 localPoint)
        {
            gridOffset = new Vector3(localPoint.x, 0f, localPoint.z);
            paintHeight = localPoint.y;
            hasPinnedOrigin = true;
            pickingOriginPoint = false;
            SceneView.RepaintAll();
        }

        private void PaintAt(Vector3 worldPosition)
        {
            if (brushPrefab == null || parentRoot == null)
            {
                return;
            }

            GameObject existingObject = FindObjectNear(worldPosition);
            if (avoidDuplicates && existingObject != null)
            {
                Selection.activeGameObject = existingObject;
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Paint Hex Grid Object");

            GameObject instance = PrefabUtility.IsPartOfPrefabAsset(brushPrefab)
                ? (GameObject)PrefabUtility.InstantiatePrefab(brushPrefab, parentRoot)
                : Instantiate(brushPrefab, parentRoot);
            if (instance == null)
            {
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Paint Hex Grid Object");
            instance.transform.SetParent(parentRoot, true);
            instance.transform.position = worldPosition;
            if (alignToParentRotation)
            {
                instance.transform.rotation = parentRoot.rotation;
            }

            if (autoAddBoardNodeAnchor && instance.GetComponent<BoardNodeAnchor>() == null)
            {
                Undo.AddComponent<BoardNodeAnchor>(instance);
            }

            Selection.activeGameObject = instance;
            EditorUtility.SetDirty(instance);
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }

        private void EraseAt(Vector3 worldPosition)
        {
            GameObject existingObject = FindObjectNear(worldPosition);
            if (existingObject == null)
            {
                return;
            }

            Undo.DestroyObjectImmediate(existingObject);
            EditorSceneManager.MarkSceneDirty(parentRoot != null ? parentRoot.gameObject.scene : existingObject.scene);
        }

        private void SnapChildrenToGrid()
        {
            if (parentRoot == null)
            {
                return;
            }

            Undo.SetCurrentGroupName("Snap Children To Hex Grid");
            for (int i = 0; i < parentRoot.childCount; i++)
            {
                Transform child = parentRoot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Undo.RecordObject(child, "Snap Child To Hex Grid");
                Vector2Int cell = WorldToCell(child.position);
                child.position = CellToWorld(cell);
                if (autoAddBoardNodeAnchor && child.GetComponent<BoardNodeAnchor>() == null)
                {
                    Undo.AddComponent<BoardNodeAnchor>(child.gameObject);
                }

                EditorUtility.SetDirty(child);
            }

            EditorSceneManager.MarkSceneDirty(parentRoot.gameObject.scene);
            SceneView.RepaintAll();
        }

        private GameObject FindObjectNear(Vector3 worldPosition)
        {
            if (parentRoot == null)
            {
                return null;
            }

            float maxDistance = Mathf.Max(0.01f, Mathf.Min(hexWidth, hexHeight) * 0.25f);
            float maxSqrDistance = maxDistance * maxDistance;
            for (int i = 0; i < parentRoot.childCount; i++)
            {
                Transform child = parentRoot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if ((child.position - worldPosition).sqrMagnitude <= maxSqrDistance)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private bool TryGetPlanePoint(Vector2 mousePosition, out Vector3 planePoint)
        {
            planePoint = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            Vector3 origin = ResolvePaintPlanePoint();
            Vector3 normal = alignToParentRotation && parentRoot != null ? parentRoot.up : Vector3.up;
            Plane plane = new(normal, origin);
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            planePoint = ray.GetPoint(distance);
            return true;
        }

        private bool TryGetHoveredCellFromPlanePoint(Vector3 planePoint, out Vector2Int cell, out Vector3 worldPosition)
        {
            cell = default;
            worldPosition = default;
            if (hexWidth <= 0.001f || hexHeight <= 0.001f)
            {
                return false;
            }

            cell = WorldToCell(planePoint);
            worldPosition = CellToWorld(cell);
            return true;
        }

        private Vector2Int WorldToCell(Vector3 worldPosition)
        {
            Vector3 local = WorldToGridLocal(worldPosition) - gridOffset;
            Vector2 rotated = Rotate2D(new Vector2(local.x, local.z), -gridRotationDegrees);
            Vector2 axial = WorldToAxial(rotated);
            return RoundAxial(axial);
        }

        private Vector3 CellToWorld(Vector2Int cell)
        {
            Vector2 planar = Rotate2D(AxialToLocal2D(cell), gridRotationDegrees);
            Vector3 local = new(gridOffset.x + planar.x, paintHeight, gridOffset.z + planar.y);
            return GridLocalToWorld(local);
        }

        private Vector3 WorldToGridLocal(Vector3 worldPosition)
        {
            if (parentRoot == null)
            {
                return worldPosition;
            }

            if (alignToParentRotation)
            {
                return parentRoot.InverseTransformPoint(worldPosition);
            }

            return worldPosition - parentRoot.position;
        }

        private Vector3 GridLocalToWorld(Vector3 localPosition)
        {
            if (parentRoot == null)
            {
                return localPosition;
            }

            if (alignToParentRotation)
            {
                return parentRoot.TransformPoint(localPosition);
            }

            return parentRoot.position + localPosition;
        }

        private Vector3 ResolvePaintPlanePoint()
        {
            if (parentRoot == null)
            {
                return new Vector3(0f, paintHeight, 0f);
            }

            if (alignToParentRotation)
            {
                return parentRoot.TransformPoint(new Vector3(0f, paintHeight, 0f));
            }

            return parentRoot.position + new Vector3(0f, paintHeight, 0f);
        }

        private Vector2 AxialToLocal2D(Vector2Int cell)
        {
            Vector2 qStep = GetQStep();
            Vector2 rStep = GetRStep();
            return qStep * cell.x + rStep * cell.y;
        }

        private Vector2 WorldToAxial(Vector2 local)
        {
            Vector2 qStep = GetQStep();
            Vector2 rStep = GetRStep();
            float determinant = qStep.x * rStep.y - rStep.x * qStep.y;
            if (Mathf.Abs(determinant) <= 0.000001f)
            {
                return Vector2.zero;
            }

            float q = (local.x * rStep.y - rStep.x * local.y) / determinant;
            float r = (qStep.x * local.y - local.x * qStep.y) / determinant;
            return new Vector2(q, r);
        }

        private Vector2 GetQStep()
        {
            return orientation == HexOrientation.PointyTop
                ? new Vector2(hexWidth, 0f)
                : new Vector2(hexWidth * 0.75f, hexHeight * 0.5f);
        }

        private Vector2 GetRStep()
        {
            return orientation == HexOrientation.PointyTop
                ? new Vector2(hexWidth * 0.5f, hexHeight * 0.75f)
                : new Vector2(0f, hexHeight);
        }

        private static Vector2 Rotate2D(Vector2 value, float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }

        private static Vector2Int RoundAxial(Vector2 axial)
        {
            float x = axial.x;
            float z = axial.y;
            float y = -x - z;

            int rx = Mathf.RoundToInt(x);
            int ry = Mathf.RoundToInt(y);
            int rz = Mathf.RoundToInt(z);

            float dx = Mathf.Abs(rx - x);
            float dy = Mathf.Abs(ry - y);
            float dz = Mathf.Abs(rz - z);

            if (dx > dy && dx > dz)
            {
                rx = -ry - rz;
            }
            else if (dy > dz)
            {
                ry = -rx - rz;
            }
            else
            {
                rz = -rx - ry;
            }

            return new Vector2Int(rx, rz);
        }

        private void DrawCellPreview(Vector3 center, Color color)
        {
            using (new Handles.DrawingScope(color))
            {
                Vector3[] corners = BuildHexCorners(center);
                Handles.DrawAAPolyLine(3f, corners);
                Handles.DrawSolidDisc(center, Vector3.up, Mathf.Max(0.005f, Mathf.Min(hexWidth, hexHeight) * 0.035f));
            }
        }

        private void DrawGridPreview()
        {
            int extentQ = Mathf.Max(0, gridExtentQ);
            int extentR = Mathf.Max(0, gridExtentR);
            if (hexWidth <= 0.001f || hexHeight <= 0.001f || (extentQ == 0 && extentR == 0))
            {
                return;
            }

            using (new Handles.DrawingScope(gridColor))
            {
                for (int q = -extentQ; q <= extentQ; q++)
                {
                    for (int r = -extentR; r <= extentR; r++)
                    {
                        Vector3 center = CellToWorld(new Vector2Int(q, r));
                        Vector3[] corners = BuildHexCorners(center);
                        Handles.DrawAAPolyLine(gridLineThickness, corners);
                    }
                }
            }
        }

        private void DrawOriginPreview()
        {
            if (!hasPinnedOrigin)
            {
                return;
            }

            Vector3 worldOrigin = CellToWorld(Vector2Int.zero);
            DrawOriginMarker(worldOrigin, "Origin", new Color(0.2f, 0.75f, 1f, 0.95f));
        }

        private void DrawOriginMarker(Vector3 worldPoint, string label, Color color)
        {
            float markerSize = Mathf.Max(0.005f, Mathf.Min(hexWidth, hexHeight) * 0.035f);
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawSolidDisc(worldPoint, Vector3.up, markerSize);
                Handles.Label(worldPoint + Vector3.up * Mathf.Max(0.03f, markerSize * 6f), label);
            }
        }

        private Vector3[] BuildHexCorners(Vector3 center)
        {
            Vector3[] corners = new Vector3[7];
            Quaternion worldRotation = alignToParentRotation && parentRoot != null
                ? parentRoot.rotation * Quaternion.AngleAxis(gridRotationDegrees, Vector3.up)
                : Quaternion.AngleAxis(gridRotationDegrees, Vector3.up);

            Vector2[] planarCorners = orientation == HexOrientation.PointyTop
                ? new[]
                {
                    new Vector2(0f, hexHeight * 0.5f),
                    new Vector2(hexWidth * 0.5f, hexHeight * 0.25f),
                    new Vector2(hexWidth * 0.5f, -hexHeight * 0.25f),
                    new Vector2(0f, -hexHeight * 0.5f),
                    new Vector2(-hexWidth * 0.5f, -hexHeight * 0.25f),
                    new Vector2(-hexWidth * 0.5f, hexHeight * 0.25f)
                }
                : new[]
                {
                    new Vector2(hexWidth * 0.5f, 0f),
                    new Vector2(hexWidth * 0.25f, hexHeight * 0.5f),
                    new Vector2(-hexWidth * 0.25f, hexHeight * 0.5f),
                    new Vector2(-hexWidth * 0.5f, 0f),
                    new Vector2(-hexWidth * 0.25f, -hexHeight * 0.5f),
                    new Vector2(hexWidth * 0.25f, -hexHeight * 0.5f)
                };

            for (int i = 0; i < planarCorners.Length; i++)
            {
                Vector3 localOffset = new(planarCorners[i].x, 0f, planarCorners[i].y);
                corners[i] = center + worldRotation * localOffset;
            }

            corners[6] = corners[0];
            return corners;
        }
    }
}
