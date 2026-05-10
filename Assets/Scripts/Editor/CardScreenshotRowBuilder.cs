using System.Collections.Generic;
using System.Linq;
using Mirror;
using RRaM.Core.Cards;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RRaM.Editor
{
    public static class CardScreenshotRowBuilder
    {
        private const string MenuRoot = "RRaM/Cards/";
        private const string RootName = "Card Screenshot Row";
        private const string CameraName = "Card Screenshot Camera";
        private const string CardPrefabPath = "Assets/Prefabs/Card.prefab";
        private const string CardsFolder = "Assets/Objects";

        private const float CardSpacing = 0.065001f;
        private const float CardScale = 1f;
        private const float CameraHeight = 8f;
        private const float ScreenshotAspect = 16f / 9f;

        [MenuItem(MenuRoot + "Create Screenshot Row")]
        public static void CreateScreenshotRow()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogError("[Cards] No active scene is loaded.");
                return;
            }

            CardInstance cardPrefab = AssetDatabase.LoadAssetAtPath<CardInstance>(CardPrefabPath);
            if (cardPrefab == null)
            {
                Debug.LogError($"[Cards] Card prefab was not found at '{CardPrefabPath}'.");
                return;
            }

            List<BaseCard> cards = LoadCardsInDeckOrder();
            if (cards.Count == 0)
            {
                Debug.LogError($"[Cards] No card assets found in '{CardsFolder}'.");
                return;
            }

            ClearExistingRow();

            GameObject root = new(RootName);
            SceneManager.MoveGameObjectToScene(root, activeScene);
            Undo.RegisterCreatedObjectUndo(root, "Create card screenshot row");

            float startX = -((cards.Count - 1) * CardSpacing) * 0.5f;
            for (int i = 0; i < cards.Count; i++)
            {
                BaseCard card = cards[i];
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab.gameObject, root.transform);
                instance.name = $"{i + 1:00}_{card.DisplayName}";
                instance.transform.localPosition = new Vector3(startX + (i * CardSpacing), 0f, 0f);
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * CardScale;

                BindCard(instance, card);
                StripRuntimeComponents(instance);
            }

            CreateCamera(root.transform, cards.Count);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log($"[Cards] Created screenshot row with {cards.Count} cards. Use camera '{CameraName}' for the screenshot.");
        }

        [MenuItem(MenuRoot + "Clear Screenshot Row")]
        public static void ClearScreenshotRow()
        {
            ClearExistingRow();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static List<BaseCard> LoadCardsInDeckOrder()
        {
            List<BaseCard> cards = LoadCardsFromSceneDeck();
            HashSet<string> knownIds = new(cards.Select(card => card.CardId));

            foreach (BaseCard card in LoadCardsFromObjectsFolder())
            {
                if (knownIds.Add(card.CardId))
                {
                    cards.Add(card);
                }
            }

            return cards;
        }

        private static List<BaseCard> LoadCardsFromSceneDeck()
        {
            Deck deck = Object.FindFirstObjectByType<Deck>();
            if (deck == null)
            {
                return new List<BaseCard>();
            }

            SerializedObject serializedDeck = new(deck);
            SerializedProperty cardsProperty = serializedDeck.FindProperty("cards");
            if (cardsProperty == null || !cardsProperty.isArray)
            {
                return new List<BaseCard>();
            }

            List<BaseCard> cards = new(cardsProperty.arraySize);
            for (int i = 0; i < cardsProperty.arraySize; i++)
            {
                BaseCard card = cardsProperty.GetArrayElementAtIndex(i).objectReferenceValue as BaseCard;
                if (card != null)
                {
                    cards.Add(card);
                }
            }

            return cards;
        }

        private static IEnumerable<BaseCard> LoadCardsFromObjectsFolder()
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { CardsFolder });
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<BaseCard>)
                .Where(card => card != null);
        }

        private static void BindCard(GameObject instance, BaseCard card)
        {
            CardInstance cardInstance = instance.GetComponent<CardInstance>();
            if (cardInstance != null)
            {
                SerializedObject serializedCard = new(cardInstance);
                SerializedProperty dataProperty = serializedCard.FindProperty("data");
                if (dataProperty != null)
                {
                    dataProperty.objectReferenceValue = card;
                    serializedCard.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            CardView view = instance.GetComponent<CardView>();
            if (view == null)
            {
                return;
            }

            view.Bind(card, revealFace: true);
            EditorUtility.SetDirty(view);
            if (view.image != null)
            {
                EditorUtility.SetDirty(view.image);
            }

            if (view.title != null)
            {
                EditorUtility.SetDirty(view.title);
            }
        }

        private static void StripRuntimeComponents(GameObject instance)
        {
            DestroyComponent<CardInteraction>(instance);
            DestroyComponent<CardAnimator>(instance);
            DestroyComponent<CardInstance>(instance);
            DestroyComponent<NetworkIdentity>(instance);
        }

        private static void DestroyComponent<T>(GameObject instance) where T : Component
        {
            T component = instance.GetComponent<T>();
            if (component != null)
            {
                Object.DestroyImmediate(component);
            }
        }

        private static void CreateCamera(Transform root, int cardCount)
        {
            float rowWidth = Mathf.Max(CardSpacing, (cardCount - 1) * CardSpacing);
            Camera camera = new GameObject(CameraName).AddComponent<Camera>();
            Undo.RegisterCreatedObjectUndo(camera.gameObject, "Create card screenshot camera");

            camera.transform.SetParent(root, false);
            camera.transform.localPosition = new Vector3(0f, CameraHeight, 0f);
            camera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(1.2f, (rowWidth + CardSpacing) / (2f * ScreenshotAspect));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        }

        private static void ClearExistingRow()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }
    }
}
