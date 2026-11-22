// VR-KoboldKare — VR Equipment System
// Lightweight bridge to the player's KoboldInventory: list equipped items and
// unequip (drop) them. Used by the radial menu.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRKoboldKare
{
    public class VREquipment : MonoBehaviour
    {
        private VRManager _manager;
        private KoboldInventory _inventory;

        internal void Initialize(VRManager manager)
        {
            _manager = manager;
            _inventory = FindInventory(manager);
            if (!ReferenceEquals(manager.Plugin, null) && manager.Plugin.Logger != null)
                manager.Plugin.Logger.LogInfo("VR: equipment subsystem initialized (inventory " +
                    (_inventory != null ? "found" : "not found") + ").");
        }

        private static KoboldInventory FindInventory(VRManager manager)
        {
            try
            {
                if (manager != null && manager.Kobold != null)
                {
                    var inv = manager.Kobold.GetComponent<KoboldInventory>();
                    if (inv != null) return inv;
                }
            }
            catch { }
            try
            {
                var all = FindObjectsOfType<KoboldInventory>();
                if (all != null && all.Length > 0) return all[0];
            }
            catch { }
            return null;
        }

        /// <summary>Names of currently equipped items (empty if none).</summary>
        public string[] ListEquipment()
        {
            var result = new List<string>();
            if (_inventory == null) return result.ToArray();
            try
            {
                var list = _inventory.GetAllEquipment();
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        result.Add(e != null ? e.name : ("item " + i));
                    }
                }
            }
            catch { }
            return result.ToArray();
        }

        /// <summary>Unequip (and drop on ground) the item at the given list index.</summary>
        public bool UnequipAt(int index, bool dropOnGround)
        {
            if (_inventory == null) return false;
            try
            {
                var list = _inventory.GetAllEquipment();
                if (list == null || index < 0 || index >= list.Count) return false;
                var e = list[index];
                if (e == null) return false;
                _inventory.RemoveEquipment(e, dropOnGround);
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>Re-locate the inventory (e.g. after a scene load / re-possession).</summary>
        public void Refresh()
        {
            _inventory = FindInventory(_manager);
        }

        void OnDestroy()
        {
            _inventory = null;
        }
    }
}