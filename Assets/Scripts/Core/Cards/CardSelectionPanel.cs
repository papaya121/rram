using System.Collections;
using System.Collections.Generic;
using RRaM.Core.Characters;
using RRaM.Core.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRaM.Core.Cards
{
    public sealed class CardSelectionPanel : MonoBehaviour
    {
        public static CardSelectionPanel Instance { get; private set; }

        private const string CardsBankName = "Cards Bank";
        private const string CardSelectName = "Card Select";
        private const string CardParentName = "Card Parent";
        private const string CharactersGroupName = "Characters Group";
        private const string CharacterButtonResourceName = "Character Button";
        private const string CharacterButtonPrefabPath = "Assets/Prefabs/Character Button.prefab";
        private const string TransferButtonFallbackText = "Передать";
        private const string TransferButtonCloseText = "Закрыть";
        private const float EnabledButtonAlpha = 1f;
        private const float DisabledButtonAlpha = 0.45f;
        private const float TransferTargetButtonHeight = 30f;
        private const float TransferTargetButtonSpacing = 4f;
        private const float TransferTargetsTopOffset = 4f;
        private const float TransferMenuCardOffsetX = -0.035f;
        private const float TransferMenuCardShiftDuration = 0.16f;

        [SerializeField] private GameObject cardsBank;
        [SerializeField] private GameObject cardSelect;
        [SerializeField] private Transform cardParent;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dropButton;
        [SerializeField] private Button transferButton;
        [SerializeField] private Button useButton;
        [SerializeField] private GameObject characterButtonPrefab;
        [SerializeField] private Transform transferTargetsParent;
        [SerializeField] private float selectDuration = 0.26f;
        [SerializeField] private float returnDuration = 0.28f;
        [SerializeField] private float discardDuration = 0.26f;
        [SerializeField] private Vector3 discardLocalOffset = new(360f, 80f, 0f);
        [SerializeField] private Vector3 discardRotation = new(0f, 0f, -24f);

        private readonly List<GameObject> transferTargetButtons = new();
        private CardInstance selectedCard;
        private Transform returnParent;
        private GameObject runtimeCharacterButtonTemplate;
        private TMP_Text transferButtonText;
        private string transferButtonDefaultText;
        private int returnSiblingIndex;
        private Coroutine activeRoutine;
        private Coroutine transferShiftRoutine;
        private bool isBusy;
        private bool isInitialized;
        private bool transferTargetsVisible;

        public static CardSelectionPanel EnsureInitialized()
        {
            if (Instance != null)
            {
                Instance.InitializeIfNeeded();
                return Instance;
            }

            CardSelectionPanel existing = FindSceneComponent<CardSelectionPanel>();
            if (existing != null)
            {
                existing.InitializeIfNeeded();
                return existing;
            }

            GameObject runner = new("Card Selection Panel Runtime");
            CardSelectionPanel panel = runner.AddComponent<CardSelectionPanel>();
            panel.InitializeIfNeeded();
            return panel;
        }

        public void Show(CardInstance card)
        {
            InitializeIfNeeded();
            if (cardSelect == null || cardParent == null)
            {
                Debug.LogWarning("[Cards] Card selection UI was not found in the scene.");
                return;
            }

            if (card == null || card == selectedCard || isBusy || !card.CanSelectFromLocalClient())
            {
                return;
            }

            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            activeRoutine = StartCoroutine(ShowRoutine(card));
        }

        public void NotifyCardRemoved(CardInstance card)
        {
            if (card == null || card != selectedCard)
            {
                return;
            }

            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            HideTransferTargets(animateCard: false);
            ResetTransferCardShiftImmediate();
            selectedCard = null;
            returnParent = null;
            isBusy = false;
            SetPanelVisible(false);
            SetCardsBankVisible(true);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeIfNeeded();
        }

        private void Update()
        {
            if (selectedCard == null)
            {
                return;
            }

            RefreshButtonStates();
        }

        private IEnumerator ShowRoutine(CardInstance card)
        {
            isBusy = true;
            HideTransferTargets(animateCard: false);
            selectedCard = card;
            returnParent = card.transform.parent;
            returnSiblingIndex = card.transform.GetSiblingIndex();

            HideCardParentPlaceholders();
            SetPanelVisible(true);
            card.BeginSelectionPresentation();
            card.transform.SetParent(cardParent, true);
            SetCardsBankVisible(false);

            RefreshButtonStates();
            yield return AnimateSelectedCard(Vector3.zero, Quaternion.identity, Vector3.one, selectDuration);

            isBusy = false;
            RefreshButtonStates();
            activeRoutine = null;
        }

        private void Close()
        {
            if (selectedCard == null || isBusy)
            {
                return;
            }

            HideTransferTargets(animateCard: false);
            ResetTransferCardShiftImmediate();
            StartCardRoutine(CloseRoutine());
        }

        private IEnumerator CloseRoutine()
        {
            isBusy = true;
            RefreshButtonStates();
            SetCardsBankVisible(true);

            CardInstance card = selectedCard;
            Transform targetParent = returnParent != null ? returnParent : ResolveReturnParent(card);
            if (card != null && targetParent != null)
            {
                card.transform.SetParent(targetParent, true);
                if (returnSiblingIndex >= 0)
                {
                    card.transform.SetSiblingIndex(Mathf.Min(returnSiblingIndex, targetParent.childCount - 1));
                }

                card.EndSelectionPresentation(animate: true);
                yield return new WaitForSeconds(returnDuration);
            }

            selectedCard = null;
            returnParent = null;
            SetPanelVisible(false);
            isBusy = false;
            activeRoutine = null;
        }

        private void Drop()
        {
            if (selectedCard == null || isBusy || !selectedCard.CanDiscardFromLocalClient())
            {
                return;
            }

            HideTransferTargets(animateCard: false);
            ResetTransferCardShiftImmediate();
            StartCardRoutine(DropRoutine());
        }

        private IEnumerator DropRoutine()
        {
            isBusy = true;
            RefreshButtonStates();

            CardInstance card = selectedCard;
            if (card != null)
            {
                Vector3 startPosition = card.transform.localPosition;
                Quaternion startRotation = card.transform.localRotation;
                Vector3 targetPosition = startPosition + discardLocalOffset;
                Quaternion targetRotation = startRotation * Quaternion.Euler(discardRotation);
                yield return AnimateSelectedCard(targetPosition, targetRotation, card.transform.localScale, discardDuration);
                card.TryDiscardFromLocalClient();
            }

            selectedCard = null;
            returnParent = null;
            SetPanelVisible(false);
            SetCardsBankVisible(true);
            isBusy = false;
            activeRoutine = null;
        }

        private void Use()
        {
            if (selectedCard == null || isBusy || !selectedCard.CanUseFromLocalClient())
            {
                return;
            }

            HideTransferTargets(animateCard: false);
            ResetTransferCardShiftImmediate();
            selectedCard.TryUseFromLocalClient();
            RefreshButtonStates();
        }

        private void Transfer()
        {
            if (selectedCard == null || isBusy)
            {
                return;
            }

            if (transferTargetsVisible)
            {
                HideTransferTargets();
                return;
            }

            if (!selectedCard.CanTransferFromLocalClient())
            {
                return;
            }

            ShowTransferTargets();
            RefreshButtonStates();
        }

        private void TransferToCharacter(uint targetCharacterNetId)
        {
            if (selectedCard == null || isBusy || !selectedCard.CanTransferToLocalCharacter(targetCharacterNetId))
            {
                return;
            }

            selectedCard.TryTransferToLocalCharacter(targetCharacterNetId);
            HideTransferTargets(animateCard: false);
            ResetTransferCardShiftImmediate();
            StartCardRoutine(CloseRoutine());
        }

        private void StartCardRoutine(IEnumerator routine)
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            activeRoutine = StartCoroutine(routine);
        }

        private IEnumerator AnimateSelectedCard(Vector3 localPosition, Quaternion localRotation, Vector3 localScale, float duration)
        {
            if (selectedCard == null)
            {
                yield break;
            }

            CardAnimator animator = selectedCard.Animator;
            if (animator != null)
            {
                yield return animator.PlayLocalPoseAnimation(localPosition, localRotation, localScale, duration);
                yield break;
            }

            Transform cardTransform = selectedCard.transform;
            Vector3 startPosition = cardTransform.localPosition;
            Quaternion startRotation = cardTransform.localRotation;
            Vector3 startScale = cardTransform.localScale;
            float elapsed = 0f;
            float clampedDuration = Mathf.Max(0.01f, duration);
            while (elapsed < clampedDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / clampedDuration));
                cardTransform.localPosition = Vector3.Lerp(startPosition, localPosition, t);
                cardTransform.localRotation = Quaternion.Slerp(startRotation, localRotation, t);
                cardTransform.localScale = Vector3.Lerp(startScale, localScale, t);
                yield return null;
            }

            cardTransform.localPosition = localPosition;
            cardTransform.localRotation = localRotation;
            cardTransform.localScale = localScale;
        }

        private void InitializeIfNeeded()
        {
            if (isInitialized)
            {
                ResolveReferences();
                BindButtons();
                return;
            }

            ResolveReferences();
            BindButtons();
            ClearCharacterGroupPlaceholders();
            HideCardParentPlaceholders();
            SetPanelVisible(false);
            isInitialized = true;
        }

        private void ResolveReferences()
        {
            cardsBank ??= FindSceneGameObject(CardsBankName);
            cardSelect ??= FindSceneGameObject(CardSelectName);
            cardParent ??= FindSceneTransform(CardParentName);
            characterButtonPrefab ??= ResolveCharacterButtonPrefab();

            if (cardSelect == null)
            {
                return;
            }

            closeButton ??= FindButton(cardSelect.transform, "Close", "Закрыть");
            dropButton ??= FindButton(cardSelect.transform, "Drop", "Discard", "Выкинуть");
            transferButton ??= FindButton(cardSelect.transform, "Transfer", "Передать");
            useButton ??= FindButton(cardSelect.transform, "Use", "Использовать");

            if (transferButton != null)
            {
                transferButtonText ??= FindTransferButtonText();
                if (transferButtonText != null && string.IsNullOrEmpty(transferButtonDefaultText))
                {
                    transferButtonDefaultText = transferButtonText.text;
                }

                transferTargetsParent ??= FindChildTransform(transferButton.transform, CharactersGroupName);
                characterButtonPrefab ??= CreateCharacterButtonTemplateFromGroup();
            }
        }

        private void BindButtons()
        {
            BindButton(closeButton, Close);
            BindButton(dropButton, Drop);
            BindButton(transferButton, Transfer);
            BindButton(useButton, Use);
            RefreshButtonStates();
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick = new Button.ButtonClickedEvent();
            if (action != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void RefreshButtonStates()
        {
            bool hasSelection = selectedCard != null && !isBusy;
            bool canTransfer = hasSelection && selectedCard.CanTransferFromLocalClient();
            if (!canTransfer)
            {
                HideTransferTargets();
            }

            SetButtonEnabled(closeButton, hasSelection);
            SetButtonEnabled(dropButton, hasSelection && selectedCard.CanDiscardFromLocalClient());
            SetButtonEnabled(transferButton, canTransfer);
            SetButtonEnabled(useButton, hasSelection && selectedCard.CanUseFromLocalClient());
        }

        private void ShowTransferTargets()
        {
            HideTransferTargets();

            Transform targetsParent = EnsureTransferTargetsParent();
            if (targetsParent == null)
            {
                return;
            }

            LocalPlayerController local = LocalPlayerController.Instance;
            if (local?.Player == null)
            {
                return;
            }

            List<CharacterSnapshot> targets = BuildTransferTargets(local);
            if (targets.Count == 0)
            {
                return;
            }

            ResizeTransferTargetsParent(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                CharacterSnapshot target = targets[i];
                GameObject buttonObject = InstantiateCharacterButton(targetsParent);
                if (buttonObject == null)
                {
                    continue;
                }

                buttonObject.name = $"Character Button {target.CharacterType}";
                ConfigureTransferTargetButtonRect(buttonObject, i);
                SetCharacterButtonText(buttonObject, ResolveCharacterButtonLabel(target));

                Button button = buttonObject.GetComponent<Button>();
                if (button != null)
                {
                    uint targetNetId = target.NetId;
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(() => TransferToCharacter(targetNetId));
                    SetButtonEnabled(button, selectedCard != null && selectedCard.CanTransferToLocalCharacter(targetNetId));
                }

                transferTargetButtons.Add(buttonObject);
            }

            targetsParent.gameObject.SetActive(true);
            transferTargetsVisible = transferTargetButtons.Count > 0;
            SetTransferButtonText(transferTargetsVisible ? TransferButtonCloseText : ResolveTransferButtonDefaultText());
            if (transferTargetsVisible)
            {
                AnimateSelectedCardTransferShift(TransferMenuCardOffsetX);
            }
        }

        private void HideTransferTargets(bool animateCard = true)
        {
            bool wasVisible = transferTargetsVisible;
            for (int i = 0; i < transferTargetButtons.Count; i++)
            {
                if (transferTargetButtons[i] != null)
                {
                    Destroy(transferTargetButtons[i]);
                }
            }

            transferTargetButtons.Clear();
            transferTargetsVisible = false;

            if (transferTargetsParent != null)
            {
                transferTargetsParent.gameObject.SetActive(false);
            }

            SetTransferButtonText(ResolveTransferButtonDefaultText());

            if (animateCard && wasVisible)
            {
                AnimateSelectedCardTransferShift(0f);
            }
        }

        private void AnimateSelectedCardTransferShift(float targetX)
        {
            if (selectedCard == null)
            {
                return;
            }

            if (transferShiftRoutine != null)
            {
                StopCoroutine(transferShiftRoutine);
            }

            transferShiftRoutine = StartCoroutine(AnimateSelectedCardTransferShiftRoutine(targetX));
        }

        private IEnumerator AnimateSelectedCardTransferShiftRoutine(float targetX)
        {
            Transform cardTransform = selectedCard != null ? selectedCard.transform : null;
            if (cardTransform == null)
            {
                transferShiftRoutine = null;
                yield break;
            }

            Vector3 startPosition = cardTransform.localPosition;
            Vector3 targetPosition = startPosition;
            targetPosition.x = targetX;

            float elapsed = 0f;
            float clampedDuration = Mathf.Max(0.01f, TransferMenuCardShiftDuration);
            while (elapsed < clampedDuration && cardTransform != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / clampedDuration));
                cardTransform.localPosition = Vector3.Lerp(startPosition, targetPosition, t);
                yield return null;
            }

            if (cardTransform != null)
            {
                cardTransform.localPosition = targetPosition;
            }

            transferShiftRoutine = null;
        }

        private void ResetTransferCardShiftImmediate()
        {
            if (transferShiftRoutine != null)
            {
                StopCoroutine(transferShiftRoutine);
                transferShiftRoutine = null;
            }

            if (selectedCard == null)
            {
                return;
            }

            Transform cardTransform = selectedCard.transform;
            if (cardTransform == null)
            {
                return;
            }

            Vector3 position = cardTransform.localPosition;
            position.x = 0f;
            cardTransform.localPosition = position;
        }

        private void ClearCharacterGroupPlaceholders()
        {
            if (transferTargetsParent == null)
            {
                return;
            }

            for (int i = transferTargetsParent.childCount - 1; i >= 0; i--)
            {
                Transform child = transferTargetsParent.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            transferTargetButtons.Clear();
            transferTargetsVisible = false;
            transferTargetsParent.gameObject.SetActive(false);
        }

        private List<CharacterSnapshot> BuildTransferTargets(LocalPlayerController local)
        {
            List<CharacterSnapshot> targets = new();
            if (local?.Player == null || selectedCard == null)
            {
                return targets;
            }

            for (int i = 0; i < local.Player.Characters.Count; i++)
            {
                CharacterSnapshot character = local.Player.Characters[i];
                if (character.NetId == 0 ||
                    character.NetId == selectedCard.AssignedCharacterNetId ||
                    character.IsDead)
                {
                    continue;
                }

                targets.Add(character);
            }

            targets.Sort((left, right) => left.CharacterType.CompareTo(right.CharacterType));
            return targets;
        }

        private Transform EnsureTransferTargetsParent()
        {
            if (transferTargetsParent != null)
            {
                return transferTargetsParent;
            }

            if (transferButton == null)
            {
                return null;
            }

            GameObject container = new(CharactersGroupName);
            container.layer = transferButton.gameObject.layer;
            RectTransform rectTransform = container.AddComponent<RectTransform>();
            rectTransform.SetParent(transferButton.transform, false);
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -TransferTargetsTopOffset);
            rectTransform.sizeDelta = Vector2.zero;
            container.SetActive(false);

            transferTargetsParent = rectTransform;
            return transferTargetsParent;
        }

        private void ResizeTransferTargetsParent(int buttonCount)
        {
            RectTransform rectTransform = transferTargetsParent as RectTransform;
            if (rectTransform == null)
            {
                return;
            }

            float height = buttonCount * TransferTargetButtonHeight + Mathf.Max(0, buttonCount - 1) * TransferTargetButtonSpacing;
            rectTransform.sizeDelta = new Vector2(0f, height);
        }

        private GameObject InstantiateCharacterButton(Transform parent)
        {
            GameObject prefab = ResolveCharacterButtonPrefab();
            GameObject buttonObject = prefab != null
                ? Instantiate(prefab, parent, false)
                : CreateFallbackCharacterButton(parent);

            if (buttonObject != null)
            {
                buttonObject.SetActive(true);
            }

            return buttonObject;
        }

        private static void ConfigureTransferTargetButtonRect(GameObject buttonObject, int index)
        {
            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -index * (TransferTargetButtonHeight + TransferTargetButtonSpacing));
            rectTransform.sizeDelta = new Vector2(0f, TransferTargetButtonHeight);
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        private void SetCharacterButtonText(GameObject buttonObject, string text)
        {
            TMP_Text label = buttonObject != null ? buttonObject.GetComponentInChildren<TMP_Text>(true) : null;
            if (label != null)
            {
                label.text = text;
            }
        }

        private GameObject CreateFallbackCharacterButton(Transform parent)
        {
            GameObject buttonObject = new(CharacterButtonResourceName);
            buttonObject.layer = transferButton != null ? transferButton.gameObject.layer : gameObject.layer;
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.SetParent(parent, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            buttonObject.AddComponent<CanvasGroup>();

            GameObject textObject = new("Text (TMP)");
            textObject.layer = buttonObject.layer;
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.SetParent(buttonObject.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TMP_Text label = textObject.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = true;
            label.text = string.Empty;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = transferButtonText != null ? transferButtonText.fontSize : 19f;
            if (transferButtonText != null && transferButtonText.font != null)
            {
                label.font = transferButtonText.font;
            }

            return buttonObject;
        }

        private GameObject CreateCharacterButtonTemplateFromGroup()
        {
            if (runtimeCharacterButtonTemplate != null || transferTargetsParent == null)
            {
                return runtimeCharacterButtonTemplate;
            }

            for (int i = 0; i < transferTargetsParent.childCount; i++)
            {
                Transform child = transferTargetsParent.GetChild(i);
                if (child == null || child.GetComponent<Button>() == null)
                {
                    continue;
                }

                runtimeCharacterButtonTemplate = Instantiate(child.gameObject, transform, false);
                runtimeCharacterButtonTemplate.name = $"{child.name} Template";
                runtimeCharacterButtonTemplate.SetActive(false);
                return runtimeCharacterButtonTemplate;
            }

            return null;
        }

        private TMP_Text FindTransferButtonText()
        {
            if (transferButton == null)
            {
                return null;
            }

            TMP_Text[] texts = transferButton.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null ||
                    (transferTargetsParent != null && texts[i].transform.IsChildOf(transferTargetsParent)))
                {
                    continue;
                }

                return texts[i];
            }

            return null;
        }

        private void SetTransferButtonText(string text)
        {
            transferButtonText ??= FindTransferButtonText();
            if (transferButtonText != null)
            {
                transferButtonText.text = text;
            }
        }

        private string ResolveTransferButtonDefaultText()
        {
            return string.IsNullOrEmpty(transferButtonDefaultText)
                ? TransferButtonFallbackText
                : transferButtonDefaultText;
        }

        private GameObject ResolveCharacterButtonPrefab()
        {
            if (characterButtonPrefab != null)
            {
                return characterButtonPrefab;
            }

            characterButtonPrefab = CreateCharacterButtonTemplateFromGroup();
            if (characterButtonPrefab != null)
            {
                return characterButtonPrefab;
            }

            characterButtonPrefab = Resources.Load<GameObject>(CharacterButtonResourceName);
#if UNITY_EDITOR
            if (characterButtonPrefab == null)
            {
                characterButtonPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CharacterButtonPrefabPath);
            }
#endif

            return characterButtonPrefab;
        }

        private static string ResolveCharacterButtonLabel(CharacterSnapshot character)
        {
            return !string.IsNullOrWhiteSpace(character.DisplayName)
                ? character.DisplayName.Trim()
                : DescribeCharacterType(character.CharacterType);
        }

        private static string DescribeCharacterType(CharacterType type)
        {
            return type switch
            {
                CharacterType.Blacksmith => "Кузнец",
                CharacterType.BlacksmithAssistant => "Помощник",
                CharacterType.Warrior => "Воин",
                CharacterType.Hunter => "Охотник",
                CharacterType.Shaman => "Шаман",
                _ => type.ToString()
            };
        }

        private static void SetButtonEnabled(Button button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = enabled;

            CanvasGroup canvasGroup = button.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = button.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = enabled ? EnabledButtonAlpha : DisabledButtonAlpha;
            canvasGroup.interactable = enabled;
        }

        private void SetPanelVisible(bool visible)
        {
            if (cardSelect != null && cardSelect.activeSelf != visible)
            {
                cardSelect.SetActive(visible);
            }
        }

        private void SetCardsBankVisible(bool visible)
        {
            if (cardsBank == null)
            {
                cardsBank = FindSceneGameObject(CardsBankName);
            }

            visible = visible && HandController.ShouldShowCardsBank();
            if (cardsBank != null && cardsBank.activeSelf != visible)
            {
                cardsBank.SetActive(visible);
            }
        }

        private void HideCardParentPlaceholders()
        {
            if (cardParent == null)
            {
                return;
            }

            for (int i = 0; i < cardParent.childCount; i++)
            {
                Transform child = cardParent.GetChild(i);
                if (selectedCard != null && child == selectedCard.transform)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
            }
        }

        private static Transform ResolveReturnParent(CardInstance card)
        {
            if (card == null)
            {
                return null;
            }

            HandController hand = HandController.Resolve(activateIfInactive: true);
            return hand != null && hand.TryGetSlot(card.HandSlotIndex, out Transform slot) ? slot : null;
        }

        private static Button FindButton(Transform root, params string[] names)
        {
            if (root == null)
            {
                return null;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                for (int j = 0; j < names.Length; j++)
                {
                    if (buttons[i].name == names[j])
                    {
                        return buttons[i];
                    }
                }
            }

            return null;
        }

        private static Transform FindChildTransform(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (child.name == objectName)
                {
                    return child;
                }

                Transform nested = FindChildTransform(child, objectName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static GameObject FindSceneGameObject(string objectName)
        {
            Transform transform = FindSceneTransform(objectName);
            return transform != null ? transform.gameObject : null;
        }

        private static Transform FindSceneTransform(string objectName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null ||
                    candidate.name != objectName ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            T[] components = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < components.Length; i++)
            {
                T candidate = components[i];
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }
    }
}
