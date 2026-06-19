
// InventorySystem.cs
// FIX: Float precision — old code used (currentWeight + item.weight > maxWeight)
//      which fails at exact-match capacity due to floating-point rounding.
//      Fixed: use epsilon tolerance:  > maxWeight + 0.001f
// All other logic was correct.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [System.Serializable]
    public class ItemData
    {
        public string id;
        public string displayName;
        public Sprite icon;
        [Min(0.01f)] public float weight = 0.1f;
        public ItemType type;
        public int      stackSize = 1;
        public int      maxStack  = 1;
    }

    public enum ItemType { Weapon, Ammo, Healing, Armor, Attachment, Throwable, Misc }

    [System.Serializable]
    public class InventorySlot
    {
        public ItemData item;
        public int      count;
        public InventorySlot(ItemData i, int c) { item = i; count = c; }
    }

    public class InventorySystem : MonoBehaviour
    {
        [Header("Capacity")]
        [SerializeField] private float maxWeight    = 30f;
        [SerializeField] private int   maxSlots     = 20;

        private readonly List<InventorySlot> _slots = new();
        private float _currentWeight;

        public IReadOnlyList<InventorySlot> Slots         => _slots;
        public float                        CurrentWeight => _currentWeight;
        public float                        MaxWeight     => maxWeight;
        public float                        WeightPct     => _currentWeight / maxWeight;

        public event Action<InventorySlot, bool> OnSlotChanged; // (slot, isNew)
        public event Action                      OnWeightChanged;
        public event Action<ItemData>            OnItemDropped;

        private const float _kEpsilon = 0.001f; // FIX: epsilon for float precision

        public bool CanAdd(ItemData item, int count = 1)
        {
            if (_slots.Count >= maxSlots && !HasItem(item.id)) return false;
            // FIX: epsilon tolerance prevents precision rejection at exact max weight
            float addWeight = item.weight * count;
            return (_currentWeight + addWeight) <= (maxWeight + _kEpsilon);
        }

        public bool TryAdd(ItemData item, int count = 1)
        {
            if (!CanAdd(item, count)) return false;

            // Try stacking first
            foreach (var slot in _slots)
            {
                if (slot.item.id != item.id) continue;
                int space = item.maxStack - slot.count;
                if (space <= 0) continue;
                int add   = Mathf.Min(count, space);
                slot.count += add;
                count      -= add;
                _currentWeight += item.weight * add;
                OnSlotChanged?.Invoke(slot, false);
                if (count <= 0) { OnWeightChanged?.Invoke(); return true; }
            }

            // New slot(s)
            while (count > 0 && _slots.Count < maxSlots)
            {
                int add = Mathf.Min(count, item.maxStack);
                var s   = new InventorySlot(item, add);
                _slots.Add(s);
                _currentWeight += item.weight * add;
                count -= add;
                OnSlotChanged?.Invoke(s, true);
            }

            OnWeightChanged?.Invoke();
            return count <= 0;
        }

        public bool TryRemove(string itemId, int count = 1)
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (slot.item.id != itemId) continue;

                int remove = Mathf.Min(count, slot.count);
                slot.count         -= remove;
                _currentWeight     -= slot.item.weight * remove;
                _currentWeight      = Mathf.Max(0f, _currentWeight); // clamp against float error
                count              -= remove;
                OnSlotChanged?.Invoke(slot, false);

                if (slot.count <= 0) _slots.RemoveAt(i);
                if (count <= 0) { OnWeightChanged?.Invoke(); return true; }
            }
            return false;
        }

        public bool HasItem(string itemId, int count = 1)
        {
            int total = 0;
            foreach (var s in _slots)
                if (s.item.id == itemId) total += s.count;
            return total >= count;
        }

        public int Count(string itemId)
        {
            int total = 0;
            foreach (var s in _slots)
                if (s.item.id == itemId) total += s.count;
            return total;
        }

        public void DropAll()
        {
            foreach (var s in _slots) OnItemDropped?.Invoke(s.item);
            _slots.Clear();
            _currentWeight = 0f;
            OnWeightChanged?.Invoke();
        }
    }
}
