using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    // Track the selected item only while placing the stand.
    internal sealed class ShortcutPiece : MonoBehaviour
    {
        internal string ItemPrefabName;
        internal string StandPrefabName;
        internal Piece StandPiece;
    }

    internal static class ShortcutPlacement
    {
        internal sealed class Pending
        {
            internal string ItemName;
            internal string StandName;
            internal ItemStand Spawned;
            internal bool FreeBuild;
        }
        internal static Pending Current;

        internal static bool IsFreeBuild(Player player, Piece piece)
        {
            return player && (player.PlacementCostDisabled ||
                (ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())));
        }

        internal static bool IsShortcut(Piece piece, out ShortcutPiece shortcut)
        {
            shortcut = piece ? piece.GetComponent<ShortcutPiece>() : null;
            return shortcut;
        }

        internal static ItemDrop.ItemData FindInventoryItem(Player player, string prefabName)
        {
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                if (item != null && item.m_dropPrefab && item.m_dropPrefab.name == prefabName && item.m_stack > 0)
                    return item;
            return null;
        }

        internal static bool HasItemPlusMaterials(Player player, Piece piece, ShortcutPiece shortcut)
            => HasItemPlusMaterials(player, shortcut.ItemPrefabName, shortcut.StandPiece);

        internal static bool HasItemPlusMaterials(Player player, string itemPrefabName, Piece standPiece)
        {
            if (!player || !standPiece || string.IsNullOrEmpty(itemPrefabName)) return false;
            GameObject itemPrefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(itemPrefabName) : null;
            ItemDrop drop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (!drop || drop.m_itemData?.m_shared == null) return false;
            string itemName = drop.m_itemData.m_shared.m_name;
            int required = 1;
            // Count the displayed item even if it is also needed to build the stand.
            foreach (Piece.Requirement r in standPiece.m_resources)
                if (r.m_resItem && r.m_resItem.m_itemData.m_shared.m_name == itemName)
                    required += r.GetAmount(0);
            return FindInventoryItem(player, itemPrefabName) != null &&
                player.GetInventory().CountItems(itemName) >= required;
        }
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
    internal static class ShortcutRecipeVisibility
    {
        private static void Prefix(PieceTable __instance, HashSet<string> knownRecipies, out List<string> __state)
        {
            __state = new List<string>();
            foreach (GameObject prefab in __instance.m_pieces)
            {
                ShortcutPiece shortcut = prefab ? prefab.GetComponent<ShortcutPiece>() : null;
                if (!shortcut || !shortcut.StandPiece) continue;
                Piece piece = prefab.GetComponent<Piece>();

                if (knownRecipies.Contains(shortcut.StandPiece.m_name) && knownRecipies.Add(piece.m_name))
                    __state.Add(piece.m_name);
            }
        }
        private static void Postfix(HashSet<string> knownRecipies, List<string> __state)
        {
            if (__state == null) return;
            foreach (string name in __state) knownRecipies.Remove(name);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class ShortcutRequirements
    {
        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            MenuChoice choice = piece ? piece.GetComponent<MenuChoice>() : null;
            bool proxy = ShortcutPlacement.IsShortcut(piece, out ShortcutPiece shortcut);
            if (!proxy && !choice) return;
            if (mode == Player.RequirementMode.CanBuild && ShortcutPlacement.IsFreeBuild(__instance, piece))
            {
    
                __result = true;
                return;
            }
            if (!__result) return;
            if (mode == Player.RequirementMode.CanBuild || mode == Player.RequirementMode.CanAlmostBuild)
            {
                if (proxy)
                    __result = ShortcutPlacement.HasItemPlusMaterials(__instance, piece, shortcut);
                else
                    __result = ShortcutPlacement.HasItemPlusMaterials(__instance, choice.ItemName,
                        Plugin.Instance?.StandPieceFor(choice.Horizontal));
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class ShortcutResourceConsumption
    {
        private static void Prefix(Player __instance, ref Piece.Requirement[] requirements)
        {
            Piece selected = __instance.GetSelectedPiece();
            if (ShortcutPlacement.IsShortcut(selected, out ShortcutPiece shortcut) &&
                ReferenceEquals(requirements, selected.m_resources))
            {
                // Let Valheim pay for the stand, then pay for the displayed item.
                requirements = ShortcutPlacement.IsFreeBuild(__instance, selected)
                    ? Array.Empty<Piece.Requirement>()
                    : shortcut.StandPiece.m_resources;
            }
        }
    }
}
