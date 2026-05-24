using System.Collections.Generic;
using RRaM.Core.Characters;
using RRaM.Core.Match;
using RRaM.Core.Networking;
using UnityEngine;

namespace RRaM.Core.Cards
{
    public sealed class HandController : MonoBehaviour
    {
        public static HandController Instance { get; private set; }

        public Transform[] characterSlots = new Transform[5];

        [SerializeField] private Vector3 stackOffset = new(0.08f, 0f, -0.015f);
        [SerializeField] private Vector3 cardEulerAngles = new(70f, 0f, 0f);
        [SerializeField] private Vector3 cardScale = Vector3.one;
        [SerializeField] private Vector3 ownerLaneOffset = new(0f, 0f, -0.35f);
        [SerializeField, Range(0f, 1f)] private float hiddenCharacterSlotAlpha = 0.2f;

        private readonly Dictionary<string, List<CardInstance>> cardsByGroup = new();
        private CanvasGroup[] characterSlotCanvasGroups;
        private int lastSelectedCharacterIndex = int.MinValue;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            CacheCharacterSlotCanvasGroups();
            if (!ApplyCardsBankActiveState())
            {
                return;
            }

            ApplyCharacterSlotVisibility(force: true);
        }

        private void LateUpdate()
        {
            if (!ApplyCardsBankActiveState())
            {
                return;
            }

            ApplyCharacterSlotVisibility(force: false);
        }

        public static HandController Resolve(bool activateIfInactive)
        {
            if (Instance != null)
            {
                if (!ShouldShowCardsBank())
                {
                    if (Instance.gameObject.activeSelf)
                    {
                        Instance.gameObject.SetActive(false);
                    }

                    return null;
                }

                if (activateIfInactive && !Instance.gameObject.activeSelf)
                {
                    Instance.gameObject.SetActive(true);
                }

                return Instance;
            }

            HandController[] controllers = Resources.FindObjectsOfTypeAll<HandController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                HandController controller = controllers[i];
                if (controller == null ||
                    !controller.gameObject.scene.IsValid() ||
                    !controller.gameObject.scene.isLoaded)
                {
                    continue;
                }

                if (!ShouldShowCardsBank())
                {
                    if (controller.gameObject.activeSelf)
                    {
                        controller.gameObject.SetActive(false);
                    }

                    return null;
                }

                if (activateIfInactive && !controller.gameObject.activeSelf)
                {
                    controller.gameObject.SetActive(true);
                }

                Instance = controller;
                return controller;
            }

            return null;
        }

        public static bool ShouldShowCardsBank()
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            MatchManager matchManager = MatchManager.Instance;
            if (matchManager == null)
            {
                return false;
            }

            return matchManager.State == MatchState.Starting ||
                   matchManager.State == MatchState.PlayerTurn ||
                   matchManager.State == MatchState.ResolvingDwarfs;
        }

        public void AddCard(CardInstance card, int characterIndex, bool animate = true)
        {
            if (card == null || !TryGetSlot(characterIndex, out Transform slot))
            {
                return;
            }

            string groupKey = GetGroupKey(card.OwnerPlayerSlot, characterIndex);
            if (!cardsByGroup.TryGetValue(groupKey, out List<CardInstance> stack))
            {
                stack = new List<CardInstance>();
                cardsByGroup[groupKey] = stack;
            }

            if (!stack.Contains(card))
            {
                stack.Add(card);
            }

            card.transform.SetParent(slot, true);
            RebuildGroupLayout(card.OwnerPlayerSlot, characterIndex, animate);
        }

        public void RemoveCard(CardInstance card)
        {
            if (card == null)
            {
                return;
            }

            RemoveCardFromSlot(card, card.OwnerPlayerSlot, card.HandSlotIndex);
        }

        public void RemoveCardFromSlot(CardInstance card, int ownerPlayerSlot, int characterIndex)
        {
            if (card == null)
            {
                return;
            }

            string groupKey = GetGroupKey(ownerPlayerSlot, characterIndex);
            if (!cardsByGroup.TryGetValue(groupKey, out List<CardInstance> stack))
            {
                return;
            }

            stack.Remove(card);
            if (stack.Count == 0)
            {
                cardsByGroup.Remove(groupKey);
                return;
            }

            RebuildGroupLayout(ownerPlayerSlot, characterIndex, animate: true);
        }

        public void RefreshCard(CardInstance card, bool animate)
        {
            if (card == null)
            {
                return;
            }

            RebuildGroupLayout(card.OwnerPlayerSlot, card.HandSlotIndex, animate);
        }

        private void RebuildGroupLayout(int ownerPlayerSlot, int characterIndex, bool animate)
        {
            if (!TryGetSlot(characterIndex, out _))
            {
                return;
            }

            string groupKey = GetGroupKey(ownerPlayerSlot, characterIndex);
            if (!cardsByGroup.TryGetValue(groupKey, out List<CardInstance> stack))
            {
                return;
            }

            int layoutIndex = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                CardInstance card = stack[i];
                if (card == null)
                {
                    continue;
                }

                if (card.IsInSelectionPanel)
                {
                    continue;
                }

                Vector3 localPosition = ResolveLocalPosition(ownerPlayerSlot, layoutIndex);
                card.ApplyHandLayout(localPosition, Quaternion.Euler(cardEulerAngles), cardScale, animate);
                layoutIndex++;
            }
        }

        private Vector3 ResolveLocalPosition(int ownerPlayerSlot, int stackIndex)
        {
            return (stackOffset * stackIndex) + (ownerLaneOffset * Mathf.Max(0, ownerPlayerSlot));
        }

        public bool TryGetSlot(int characterIndex, out Transform slot)
        {
            if (characterSlots == null || characterIndex < 0 || characterIndex >= characterSlots.Length)
            {
                slot = null;
                return false;
            }

            slot = characterSlots[characterIndex];
            return slot != null;
        }

        private static string GetGroupKey(int ownerPlayerSlot, int characterIndex)
        {
            return $"{ownerPlayerSlot}:{characterIndex}";
        }

        private bool ApplyCardsBankActiveState()
        {
            bool shouldShow = ShouldShowCardsBank();
            if (gameObject.activeSelf != shouldShow)
            {
                gameObject.SetActive(shouldShow);
            }

            return shouldShow;
        }

        private void ApplyCharacterSlotVisibility(bool force)
        {
            int selectedCharacterIndex = ResolveSelectedCharacterIndex();
            if (!force && selectedCharacterIndex == lastSelectedCharacterIndex)
            {
                return;
            }

            CacheCharacterSlotCanvasGroups();
            lastSelectedCharacterIndex = selectedCharacterIndex;

            if (characterSlots == null)
            {
                return;
            }

            for (int i = 0; i < characterSlots.Length; i++)
            {
                CanvasGroup canvasGroup = characterSlotCanvasGroups != null && i < characterSlotCanvasGroups.Length
                    ? characterSlotCanvasGroups[i]
                    : null;
                if (canvasGroup == null)
                {
                    continue;
                }

                bool isVisible = selectedCharacterIndex < 0 || i == selectedCharacterIndex;
                canvasGroup.alpha = isVisible ? 1f : hiddenCharacterSlotAlpha;
                canvasGroup.interactable = isVisible;
                canvasGroup.blocksRaycasts = isVisible;
            }
        }

        private void CacheCharacterSlotCanvasGroups()
        {
            if (characterSlots == null)
            {
                characterSlotCanvasGroups = System.Array.Empty<CanvasGroup>();
                return;
            }

            if (characterSlotCanvasGroups == null || characterSlotCanvasGroups.Length != characterSlots.Length)
            {
                characterSlotCanvasGroups = new CanvasGroup[characterSlots.Length];
            }

            for (int i = 0; i < characterSlots.Length; i++)
            {
                Transform slot = characterSlots[i];
                if (slot == null)
                {
                    characterSlotCanvasGroups[i] = null;
                    continue;
                }

                CanvasGroup canvasGroup = slot.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = slot.gameObject.AddComponent<CanvasGroup>();
                }

                characterSlotCanvasGroups[i] = canvasGroup;
            }
        }

        private int ResolveSelectedCharacterIndex()
        {
            if (characterSlots == null || characterSlots.Length == 0)
            {
                return -1;
            }

            LocalPlayerController local = LocalPlayerController.Instance;
            if (local == null || local.Player == null)
            {
                return -1;
            }

            uint selectedCharacterNetId = local.EffectiveSelectedCharacterNetId;
            if (selectedCharacterNetId == 0)
            {
                return -1;
            }

            CharacterSnapshot selectedCharacter = local.ResolveOwnedCharacterSnapshot(selectedCharacterNetId);
            if (selectedCharacter.NetId == 0 && local.PredictedSelectedCharacter.NetId == selectedCharacterNetId)
            {
                selectedCharacter = local.PredictedSelectedCharacter;
            }

            return selectedCharacter.NetId != 0
                ? Mathf.Clamp((int)selectedCharacter.CharacterType, 0, characterSlots.Length - 1)
                : -1;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
