using System.Collections.Generic;
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

        private readonly Dictionary<string, List<CardInstance>> cardsByGroup = new();

        private void Awake()
        {
            Instance = this;
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

            string groupKey = GetGroupKey(card.OwnerPlayerSlot, card.HandSlotIndex);
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

            RebuildGroupLayout(card.OwnerPlayerSlot, card.HandSlotIndex, animate: true);
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

            for (int i = 0; i < stack.Count; i++)
            {
                CardInstance card = stack[i];
                if (card == null)
                {
                    continue;
                }

                Vector3 localPosition = ResolveLocalPosition(ownerPlayerSlot, i);
                card.ApplyHandLayout(localPosition, Quaternion.Euler(cardEulerAngles), cardScale, animate);
            }
        }

        private Vector3 ResolveLocalPosition(int ownerPlayerSlot, int stackIndex)
        {
            return (stackOffset * stackIndex) + (ownerLaneOffset * Mathf.Max(0, ownerPlayerSlot));
        }

        private bool TryGetSlot(int characterIndex, out Transform slot)
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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
