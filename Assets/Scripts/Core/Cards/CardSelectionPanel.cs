using System.Collections;
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
        private const float EnabledButtonAlpha = 1f;
        private const float DisabledButtonAlpha = 0.45f;

        [SerializeField] private GameObject cardsBank;
        [SerializeField] private GameObject cardSelect;
        [SerializeField] private Transform cardParent;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dropButton;
        [SerializeField] private Button transferButton;
        [SerializeField] private Button useButton;
        [SerializeField] private float selectDuration = 0.26f;
        [SerializeField] private float returnDuration = 0.28f;
        [SerializeField] private float discardDuration = 0.26f;
        [SerializeField] private Vector3 discardLocalOffset = new(360f, 80f, 0f);
        [SerializeField] private Vector3 discardRotation = new(0f, 0f, -24f);

        private CardInstance selectedCard;
        private Transform returnParent;
        private int returnSiblingIndex;
        private Coroutine activeRoutine;
        private bool isBusy;
        private bool isInitialized;

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

            selectedCard.TryUseFromLocalClient();
            RefreshButtonStates();
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
            HideCardParentPlaceholders();
            SetPanelVisible(false);
            isInitialized = true;
        }

        private void ResolveReferences()
        {
            cardsBank ??= FindSceneGameObject(CardsBankName);
            cardSelect ??= FindSceneGameObject(CardSelectName);
            cardParent ??= FindSceneTransform(CardParentName);

            if (cardSelect == null)
            {
                return;
            }

            closeButton ??= FindButton(cardSelect.transform, "Close", "Закрыть");
            dropButton ??= FindButton(cardSelect.transform, "Drop", "Discard", "Выкинуть");
            transferButton ??= FindButton(cardSelect.transform, "Transfer", "Передать");
            useButton ??= FindButton(cardSelect.transform, "Use", "Использовать");
        }

        private void BindButtons()
        {
            BindButton(closeButton, Close);
            BindButton(dropButton, Drop);
            BindButton(transferButton, null);
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
            SetButtonEnabled(closeButton, hasSelection);
            SetButtonEnabled(dropButton, hasSelection && selectedCard.CanDiscardFromLocalClient());
            SetButtonEnabled(transferButton, false);
            SetButtonEnabled(useButton, hasSelection && selectedCard.CanUseFromLocalClient());
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
