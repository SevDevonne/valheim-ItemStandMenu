using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class ShortcutPlaceStand
    {
        private static void Prefix(Player __instance, ref Piece piece, out ShortcutPlacement.Pending __state)
        {
            __state = null;
            if (!ShortcutPlacement.IsShortcut(piece, out ShortcutPiece shortcut)) return;
            __state = new ShortcutPlacement.Pending
            {
                ItemName = shortcut.ItemPrefabName,
                StandName = shortcut.StandPrefabName,
                FreeBuild = ShortcutPlacement.IsFreeBuild(__instance, piece)
            };
            ShortcutPlacement.Current = __state;

            piece = shortcut.StandPiece;
        }

        private static void Postfix(Player __instance, ShortcutPlacement.Pending __state)
        {
            if (__state == null) return;
            ShortcutPlacement.Current = null;
            if (__state.Spawned)
                __instance.StartCoroutine(AttachAfterMaterials(__instance, __state));
            else
                AttachmentError(__state.ItemName);
        }

        private static void AttachmentError(string item)
            => Debug.LogError("[Valerius Item Stand Menu] Could not attach " + item + " to the item stand.");

        private static IEnumerator AttachAfterMaterials(Player player, ShortcutPlacement.Pending state)
        {
            // Add the displayed item only after Valheim finishes building the stand.
            yield return null;
            if (!player || !state.Spawned || !ObjectDB.instance) yield break;
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(state.ItemName);
            ItemDrop drop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (!drop) yield break;
            if (state.FreeBuild)
            {
                // In free-build mode, add the item without taking it from inventory.
                ZNetView view = state.Spawned.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || !view.IsOwner())
                {
                    AttachmentError(state.ItemName);
                    yield break;
                }
                ItemDrop.ItemData freeItem = drop.m_itemData.Clone();
                freeItem.m_stack = 1;
                freeItem.m_dropPrefab = itemPrefab;
                int hash = itemPrefab.name.GetStableHashCode();
                ZDO zdo = view.GetZDO();
                zdo.Set(ZDOVars.s_item, hash);
                ItemDrop.SaveToZDO(freeItem, zdo);
                view.InvokeRPC(ZNetView.Everybody, "SetVisualItem", hash, freeItem.m_variant, freeItem.m_quality, 0);
                yield break;
            }

            ItemDrop.ItemData inventoryItem = ShortcutPlacement.FindInventoryItem(player, state.ItemName);
            if (inventoryItem == null) yield break;

            state.Spawned.UseItem(player, inventoryItem);
        }
    }
}
