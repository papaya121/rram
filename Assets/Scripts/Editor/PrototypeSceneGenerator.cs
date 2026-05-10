using System.Collections.Generic;
using Mirror;
using RRaM.Core.Board;
using RRaM.Core.Cards;
using RRaM.Core.Characters;
using RRaM.Core.Data;
using RRaM.Core.Dice;
using RRaM.Core.Dwarfs;
using RRaM.Core.Match;
using RRaM.Core.Networking;
using RRaM.Core.Turns;
using RRaM.Core.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using kcp2k;

namespace RRaM.Editor
{
    public static class PrototypeSceneGenerator
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string GeneratedRootPath = "Assets/Generated/Prototype";
        private const string GeneratedPrefabPath = GeneratedRootPath + "/Prefabs";
        private const string GeneratedConfigPath = GeneratedRootPath + "/Config";
        private const string GeneratedMaterialPath = GeneratedRootPath + "/Materials";
        private const string BaseMaterialAssetPath = GeneratedConfigPath + "/PrototypeBaseMaterial.mat";
        private const string MatchConfigAssetPath = GeneratedConfigPath + "/MatchPrototypeConfig.asset";
        private const string PlayerPrefabPath = GeneratedPrefabPath + "/NetworkPlayer.prefab";
        private const string CharacterPrefabPath = GeneratedPrefabPath + "/NetworkCharacter.prefab";
        private const string DwarfPrefabPath = GeneratedPrefabPath + "/NetworkDwarf.prefab";
        private const string ServiceRootPrefabPath = GeneratedPrefabPath + "/NetworkServices.prefab";

        [MenuItem("RRaM/Generate/Refresh Prototype Scene")]
        public static void GenerateActiveScene()
        {
            EnsureFolders();
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogError("No active scene is loaded.");
                return;
            }

            GenerateScene(activeScene, saveScene: true);
        }

        public static void GenerateSampleScene()
        {
            EnsureFolders();
            Scene scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            GenerateScene(scene, saveScene: true);
        }

        private static void GenerateScene(Scene scene, bool saveScene)
        {
            MatchPrototypeConfig config = GetOrCreateMatchConfig();
            Material baseMaterial = GetOrCreateBaseMaterial();

            GameObject playerPrefab = CreateOrUpdatePlayerPrefab();
            GameObject characterPrefab = CreateOrUpdateCharacterPrefab();
            GameObject dwarfPrefab = CreateOrUpdateDwarfPrefab();
            GameObject serviceRootPrefab = CreateOrUpdateServiceRootPrefab();

            PrototypeVisualSettings visualSettings = GetOrCreateSceneObject<PrototypeVisualSettings>(scene, "Prototype Visual Settings");
            visualSettings.Configure(baseMaterial);

            RramNetworkManager networkManager = GetOrCreateSceneObject<RramNetworkManager>(scene, "RRaM Network Manager");
            KcpTransport transport = networkManager.GetComponent<KcpTransport>();
            if (transport == null)
            {
                transport = networkManager.gameObject.AddComponent<KcpTransport>();
            }

            networkManager.transport = transport;
            networkManager.ConfigureAuthoredSetup(config, playerPrefab, characterPrefab, dwarfPrefab, serviceRootPrefab);

            BoardGraph boardGraph = GetOrCreateSceneObject<BoardGraph>(scene, "Board Graph");
            boardGraph.EnsureInitialized();

            BoardPresentation boardPresentation = boardGraph.GetComponent<BoardPresentation>();
            if (boardPresentation == null)
            {
                boardPresentation = boardGraph.gameObject.AddComponent<BoardPresentation>();
            }

            Transform boardVisualRoot = RebuildBoardVisuals(boardGraph.transform, boardGraph);
            AssignObjectReference(boardPresentation, "visualsRoot", boardVisualRoot);

            BoardPathValidator boardPathValidator = GetOrCreateSceneObject<BoardPathValidator>(scene, "Board Path Validator");
            boardPathValidator.Configure(boardGraph);

            MatchContext matchContext = GetOrCreateSceneObject<MatchContext>(scene, "Match Context");
            matchContext.Configure(config, networkManager, null, null, null, boardGraph, boardPathValidator, null, null, null);

            GetOrCreateSceneObject<PrototypeHud>(scene, "Prototype HUD");
            ConfigureSceneCamera(scene);
            ConfigureSceneLighting(scene);
            ConfigureRenderSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (saveScene)
            {
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"Prototype scene regenerated: {scene.path}");
        }

        private static MatchPrototypeConfig GetOrCreateMatchConfig()
        {
            MatchPrototypeConfig config = AssetDatabase.LoadAssetAtPath<MatchPrototypeConfig>(MatchConfigAssetPath);
            if (config != null)
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<MatchPrototypeConfig>();
            AssetDatabase.CreateAsset(config, MatchConfigAssetPath);
            return config;
        }

        private static Material GetOrCreateBaseMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialAssetPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = "PrototypeBaseMaterial",
                color = new Color(0.7f, 0.7f, 0.7f)
            };

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.18f);
            }

            AssetDatabase.CreateAsset(material, BaseMaterialAssetPath);
            return material;
        }

        private static GameObject CreateOrUpdatePlayerPrefab()
        {
            GameObject root = new("NetworkPlayer");
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkPlayerConnection>();
            root.AddComponent<LocalPlayerController>();
            return SavePrefab(root, PlayerPrefabPath);
        }

        private static GameObject CreateOrUpdateCharacterPrefab()
        {
            GameObject root = new("NetworkCharacter");
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<NetworkCharacterPawn>();
            return SavePrefab(root, CharacterPrefabPath);
        }

        private static GameObject CreateOrUpdateDwarfPrefab()
        {
            GameObject root = new("NetworkDwarf");
            root.AddComponent<NetworkIdentity>();
            NetworkDwarfPawn pawn = root.AddComponent<NetworkDwarfPawn>();
            Transform visualRoot = BuildDwarfVisual(root.transform);
            AssignObjectReference(pawn, "visualRoot", visualRoot);
            return SavePrefab(root, DwarfPrefabPath);
        }

        private static GameObject CreateOrUpdateServiceRootPrefab()
        {
            GameObject root = new("NetworkServices");
            root.AddComponent<NetworkIdentity>();
            root.AddComponent<MatchManager>();
            root.AddComponent<TurnManager>();
            root.AddComponent<DiceManager>();
            root.AddComponent<CharacterManager>();
            root.AddComponent<CardManager>();
            root.AddComponent<DwarfManager>();
            return SavePrefab(root, ServiceRootPrefabPath);
        }

        private static GameObject SavePrefab(GameObject root, string prefabPath)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssignNetworkIdentityAssetId(prefab, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static T GetOrCreateSceneObject<T>(Scene scene, string objectName) where T : Component
        {
            T[] components = Object.FindObjectsByType<T>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i].gameObject.scene == scene)
                {
                    components[i].gameObject.name = objectName;
                    return components[i];
                }
            }

            GameObject root = new(objectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.AddComponent<T>();
        }

        private static void ConfigureSceneCamera(Scene scene)
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null || camera.gameObject.scene != scene)
            {
                GameObject cameraObject = new("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                camera.gameObject.tag = "MainCamera";
            }

            Transform cameraTransform = camera.transform;
            camera.gameObject.SetActive(true);
            cameraTransform.position = new Vector3(0f, 18f, -9.5f);
            cameraTransform.rotation = Quaternion.Euler(57f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
        }

        private static void ConfigureSceneLighting(Scene scene)
        {
            Light directional = GetOrCreateLight(scene, "Prototype Directional Light", LightType.Directional);
            directional.intensity = 1.35f;
            directional.color = new Color(1f, 0.93f, 0.84f);
            directional.shadows = LightShadows.Soft;
            directional.transform.rotation = Quaternion.Euler(50f, -32f, 0f);
            RenderSettings.sun = directional;

            Light fill = GetOrCreateLight(scene, "Prototype Fill Light", LightType.Point);
            fill.range = 40f;
            fill.intensity = 18f;
            fill.color = new Color(0.33f, 0.52f, 0.6f);
            fill.transform.position = new Vector3(0f, 12f, 8f);
        }

        private static Light GetOrCreateLight(Scene scene, string objectName, LightType lightType)
        {
            Light[] lights = Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].gameObject.scene == scene && lights[i].name == objectName)
                {
                    lights[i].type = lightType;
                    return lights[i];
                }
            }

            GameObject lightObject = new(objectName);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = lightType;
            return light;
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.31f, 0.34f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.16f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.11f, 0.11f, 0.12f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 48f;
        }

        private static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets", "Generated");
            CreateFolderIfMissing("Assets/Generated", "Prototype");
            CreateFolderIfMissing(GeneratedRootPath, "Prefabs");
            CreateFolderIfMissing(GeneratedRootPath, "Config");
            CreateFolderIfMissing(GeneratedRootPath, "Materials");
        }

        private static void CreateFolderIfMissing(string parentFolder, string newFolderName)
        {
            string combinedPath = $"{parentFolder}/{newFolderName}";
            if (AssetDatabase.IsValidFolder(combinedPath))
            {
                return;
            }

            AssetDatabase.CreateFolder(parentFolder, newFolderName);
        }

        private static Transform RebuildBoardVisuals(Transform boardRoot, BoardGraph boardGraph)
        {
            Transform existingRoot = boardRoot.Find("Board Visuals");
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot.gameObject);
            }

            Transform visualsRoot = new GameObject("Board Visuals").transform;
            visualsRoot.SetParent(boardRoot, false);

            Material floorMaterial = GetOrCreateMaterialAsset("Board_Floor", new Color(0.11f, 0.12f, 0.14f), 0.35f);
            Material outerRingMaterial = GetOrCreateMaterialAsset("Board_OuterRing", new Color(0.64f, 0.58f, 0.46f), 0.5f);
            Material innerRingMaterial = GetOrCreateMaterialAsset("Board_InnerRing", new Color(0.19f, 0.24f, 0.29f), 0.2f, new Color(0.05f, 0.08f, 0.09f));
            Material centerMaterial = GetOrCreateMaterialAsset("Board_CenterPlate", new Color(0.34f, 0.28f, 0.23f), 0.1f);
            Material linkBaseMaterial = GetOrCreateMaterialAsset("Board_LinkBase", new Color(0.31f, 0.25f, 0.19f), 0.15f);
            Material linkAccentMaterial = GetOrCreateMaterialAsset("Board_LinkAccent", new Color(0.82f, 0.68f, 0.43f), 0.6f, new Color(0.12f, 0.08f, 0.02f));
            Material pedestalMaterial = GetOrCreateMaterialAsset("Board_Pedestal", new Color(0.18f, 0.16f, 0.13f), 0.1f);
            Material capMaterial = GetOrCreateMaterialAsset("Board_Cap", new Color(0.83f, 0.77f, 0.66f), 0.55f, new Color(0.08f, 0.08f, 0.04f));
            Material activeCapMaterial = GetOrCreateMaterialAsset("Board_ActiveCap", new Color(0.39f, 0.71f, 0.78f), 0.7f, new Color(0.12f, 0.3f, 0.34f));
            Material rockMaterial = GetOrCreateMaterialAsset("Board_Rock", new Color(0.24f, 0.22f, 0.2f), 0.08f);
            Material crystalMaterial = GetOrCreateMaterialAsset("Board_Crystal", new Color(0.24f, 0.58f, 0.66f), 0.7f, new Color(0.08f, 0.18f, 0.2f));
            bool isAuthoredLayout = boardGraph.UsesAuthoredAnchors;

            if (!isAuthoredLayout)
            {
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
                        CreateLink(edgeKey, visualsRoot, start, end, 0.022f, linkBaseMaterial, 0.006f);
                    }
                    else
                    {
                        CreateLink(edgeKey, visualsRoot, start, end, 0.05f, linkBaseMaterial, 0.015f);
                        CreateLink($"{edgeKey}_Accent", visualsRoot, start, end, 0.018f, linkAccentMaterial, 0.045f);
                    }
                }
            }

            for (int i = 0; i < boardGraph.Nodes.Count; i++)
            {
                BoardNode node = boardGraph.Nodes[i];
                Transform nodeRoot = new GameObject(node.NodeId).transform;
                nodeRoot.SetParent(visualsRoot, false);
                nodeRoot.position = boardGraph.GetWorldPosition(node.NodeId);

                if (isAuthoredLayout)
                {
                    Color authoredColor = ResolveAuthoredNodeColor(node);
                    Material authoredTopMaterial = GetOrCreateMaterialAsset($"Board_Authoring_{node.NodeKind}_{node.IsStarterNode}", authoredColor, 0.2f, authoredColor * 0.02f);

                    GameObject authoredPedestal = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Pedestal", nodeRoot, pedestalMaterial);
                    authoredPedestal.transform.localPosition = new Vector3(0f, 0.006f, 0f);
                    authoredPedestal.transform.localScale = new Vector3(0.2f, 0.008f, 0.2f);

                    GameObject authoredTop = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Cap", nodeRoot, authoredTopMaterial);
                    authoredTop.transform.localPosition = new Vector3(0f, 0.014f, 0f);
                    authoredTop.transform.localScale = new Vector3(0.16f, 0.01f, 0.16f);

                    PrototypeVisualFactory.CreateLabel(
                        BoardNodeDisplayUtility.GetShortLabel(node.NodeId),
                        nodeRoot,
                        new Vector3(0f, 0.36f, 0f),
                        Color.white,
                        42);
                    continue;
                }

                GameObject pedestal = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Pedestal", nodeRoot, pedestalMaterial);
                pedestal.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                pedestal.transform.localScale = new Vector3(0.55f, 0.03f, 0.55f);

                Material topMaterial = i % 3 == 0 ? activeCapMaterial : capMaterial;
                GameObject top = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Cap", nodeRoot, topMaterial);
                top.transform.localPosition = new Vector3(0f, 0.14f, 0f);
                top.transform.localScale = new Vector3(0.34f, 0.12f, 0.34f);

                PrototypeVisualFactory.CreateLabel(
                    BoardNodeDisplayUtility.GetShortLabel(node.NodeId),
                    nodeRoot,
                    new Vector3(0f, 2f, 0f),
                    new Color(0.94f, 0.9f, 0.78f),
                    56);
            }

            for (int i = 0; i < 10 && !isAuthoredLayout; i++)
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

            return visualsRoot;
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

        private static Transform BuildDwarfVisual(Transform root)
        {
            Transform visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(root, false);

            Material armorMaterial = GetOrCreateMaterialAsset("Dwarf_Armor", new Color(0.36f, 0.38f, 0.42f), 0.55f, new Color(0.0072f, 0.0076f, 0.0084f));
            Material beardMaterial = GetOrCreateMaterialAsset("Dwarf_Beard", new Color(0.72f, 0.56f, 0.28f), 0.15f, new Color(0.0072f, 0.0056f, 0.0028f));
            Material gemMaterial = GetOrCreateMaterialAsset("Dwarf_Gem", new Color(0.22f, 0.63f, 0.74f), 0.75f, new Color(0.022f, 0.063f, 0.074f));

            GameObject baseDisk = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Base", visualRoot, armorMaterial);
            baseDisk.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            baseDisk.transform.localScale = new Vector3(0.46f, 0.05f, 0.46f);

            GameObject body = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cube, "Body", visualRoot, armorMaterial);
            body.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            body.transform.localScale = new Vector3(0.56f, 0.56f, 0.56f);

            GameObject helmet = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, "Helmet", visualRoot, armorMaterial);
            helmet.transform.localPosition = new Vector3(0f, 0.46f, 0f);
            helmet.transform.localScale = new Vector3(0.34f, 0.12f, 0.34f);

            GameObject beard = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Beard", visualRoot, beardMaterial);
            beard.transform.localPosition = new Vector3(0f, -0.02f, 0.16f);
            beard.transform.localScale = new Vector3(0.26f, 0.24f, 0.18f);

            GameObject gem = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Sphere, "Gem", visualRoot, gemMaterial);
            gem.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            gem.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);

            return visualRoot;
        }

        private static void CreateLink(string name, Transform parent, Vector3 start, Vector3 end, float radius, Material material, float yOffset)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            GameObject link = PrototypeVisualFactory.CreatePrimitive(PrimitiveType.Cylinder, name, parent, material);
            link.transform.position = (start + end) * 0.5f + new Vector3(0f, yOffset, 0f);
            link.transform.up = delta.normalized;
            link.transform.localScale = new Vector3(radius, length * 0.5f, radius);
        }

        private static Material GetOrCreateMaterialAsset(string materialName, Color color, float smoothness, Color? emission = null)
        {
            string assetPath = $"{GeneratedMaterialPath}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(GetOrCreateBaseMaterial())
                {
                    name = materialName
                };

                AssetDatabase.CreateAsset(material, assetPath);
            }

            PrototypeVisualFactory.ApplyColor(material, color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", emission.Value);
                }
            }
            else if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            SerializedObject serializedObject = new(target);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void AssignNetworkIdentityAssetId(GameObject prefab, string prefabPath)
        {
            if (prefab == null)
            {
                return;
            }

            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                return;
            }

            string guidString = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrWhiteSpace(guidString))
            {
                return;
            }

            uint assetId = NetworkIdentity.AssetGuidToUint(new System.Guid(guidString));
            SerializedObject serializedObject = new(identity);
            SerializedProperty property = serializedObject.FindProperty("_assetId");
            if (property == null)
            {
                return;
            }

            property.uintValue = assetId;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(identity);
            AssetDatabase.SaveAssetIfDirty(identity);
        }
    }
}
