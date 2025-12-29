using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static OdinsFoodBarrels.OdinsFoodBarrelsPlugin;

namespace OdinsFoodBarrels
{
    /// <summary>
    ///     Class to patch Valheim inventory methods so that the specified
    ///     containers can only contain items of a single type.
    /// </summary>
    [HarmonyPatch]
    internal static class RestrictContainers
    {
        private static string? _loadingContainer;
        private static string? _targetContainer;
        private static HashSet<string>? _allowedItems;
        private static Dictionary<string, HashSet<string>> _allowedItemsByContainer = new();

        /// <summary>
        ///     Set which containers to restrict and which items are allowed to be placed in each one.
        /// </summary>
        public static void SetContainerRestrictions(Dictionary<string, HashSet<string>> allowedItemByContainer)
        {
            _allowedItemsByContainer = allowedItemByContainer;
        }

        /// <summary>
        ///     Check if container should be restricted and get the allowed item type for it.
        /// </summary>
        public static bool IsRestrictedContainer(string containerName, out HashSet<string> allowedItems)
        {
            return _allowedItemsByContainer.TryGetValue(containerName, out allowedItems);
        }

        // --------------------------------------------------
        // Harmony patches
        // --------------------------------------------------

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        private static bool DropItemPrefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos)
        {
            if (!CanAddItem(__instance.m_inventory, item))
            {
                return false;
            }

            ItemDrop.ItemData itemAt = __instance.m_inventory.GetItemAt(pos.x, pos.y);
            if (itemAt != null)
            {
                return CanAddItem(fromInventory, itemAt);
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
        [HarmonyPriority(Priority.First)]
        private static bool AddItemPrefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!CanAddItem(__instance, item))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
        [HarmonyPriority(Priority.First)]
        private static bool MoveItemToThisPrefix_1(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item)
        {
            if (__instance == null || fromInventory == null || item == null)
            {
                return false;
            }

            Log.LogDebug("MoveItemToThisPrefix");
            Log.LogDebug($"Add to: {__instance.m_name}");
            Log.LogDebug($"Item: {item.PrefabName()}");

            return CanAddItem(__instance, item);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
        private static void LoadPrefix(Inventory __instance)
        {
            if (__instance == null)
            {
                return;
            }

            Log.LogDebug($"Load prefix: {__instance.m_name}");
            _loadingContainer = __instance.m_name;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
        private static void LoadPostfix()
        {
            _loadingContainer = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll), new[] { typeof(Inventory) })]
        [HarmonyPriority(Priority.First)]
        private static void MoveAllPrefix(Inventory __instance, Inventory fromInventory)
        {
            if (__instance == null || fromInventory == null)
            {
                return;
            }

            Log.LogDebug("MoveAllPrefix");
            Log.LogDebug($"Move to: {__instance.m_name}");

            if (IsRestrictedContainer(__instance.m_name, out HashSet<string> allowedItems))
            {
                _targetContainer = __instance.m_name;
                _allowedItems = allowedItems;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll), new[] { typeof(Inventory) })]
        [HarmonyPriority(Priority.First)]
        private static void MoveAllPostfix()
        {
            _targetContainer = null;
            _allowedItems = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData) })]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
        [HarmonyPriority(Priority.First)]
        private static bool RemoveItemPrefix(Inventory __instance, ItemDrop.ItemData item)
        {
            return ShouldRemoveItem(__instance, item);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveOneItem), new[] { typeof(ItemDrop.ItemData) })]
        [HarmonyPriority(Priority.First)]
        private static bool RemoveOneItemPrefix(Inventory __instance, ItemDrop.ItemData item)
        {
            return ShouldRemoveItem(__instance, item);
        }

        // --------------------------------------------------
        // Helpers
        // --------------------------------------------------

        private static bool CanAddItem(Inventory inventory, ItemDrop.ItemData item)
        {
            if (inventory == null || item == null)
            {
                return false;
            }

            if (inventory.m_name == _loadingContainer)
            {
                return true; // skip checks while loading from ZDO
            }

            if (IsRestrictedContainer(inventory.m_name, out HashSet<string> allowedItems))
            {
                var result = allowedItems.Contains(item.PrefabName());
                if (!result)
                {
                    var msg = $"{item.m_shared.m_name} cannot be placed in {inventory.m_name}";
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, msg);
                }
                return result;
            }

            return true;
        }

        private static bool ShouldRemoveItem(Inventory __instance, ItemDrop.ItemData item)
        {
            if (__instance == null || item == null)
            {
                return false;
            }

            bool wasAddedToDynamicPile = !string.IsNullOrEmpty(_targetContainer);

            Log.LogDebug("RemoveItemPrefix");
            Log.LogDebug($"Remove from: {__instance.m_name}");
            Log.LogDebug($"Item: {item.PrefabName()}");

            if (wasAddedToDynamicPile && _allowedItems != null)
            {
                return _allowedItems.Contains(item.PrefabName());
            }

            return true;
        }
    }

    internal static class InventoryHelper
    {
        public static string PrefabName(this ItemDrop.ItemData item)
        {
            if (item.m_dropPrefab)
            {
                return item.m_dropPrefab.name;
            }

            Log.LogWarning("Item has missing prefab " + item.m_shared.m_name);
            return item.m_shared.m_name;
        }
    }
}
