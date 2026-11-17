using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ItemStandMenu
{
    [HarmonyPatch(typeof(ByUsagePieceList), nameof(ByUsagePieceList.UpdateAvailableTags))]
    [HarmonyPriority(Priority.Last)]
    internal static class StandTabAvailability
    {
        private static void Postfix(ByUsagePieceList __instance, PieceTable pieceTable)
            => ItemStandUsageTab.Updated(__instance, pieceTable);
    }

    [HarmonyPatch(typeof(ByUsagePieceList), nameof(ByUsagePieceList.GetAvailablePiecesWithTag))]
    [HarmonyPriority(Priority.Last)]
    internal static class BuildMenuFilter
    {
        private static void Postfix(ByUsagePieceList __instance, int tagId, PieceTable pieceTable, IList<Piece> resultOut)
        {
            ProgressiveBuildMenu.Filter(__instance, pieceTable, tagId, resultOut);
        }
    }

    [HarmonyPatch(typeof(BuildUi), "OpenBuildMenu")]
    internal static class BuildMenuScrollSetup
    {
        private static void Postfix(BuildUi __instance) => ProgressiveBuildMenu.Attach(__instance);
    }

    [HarmonyPatch(typeof(BuildUi), "UpdateSearch")]
    internal static class BuildMenuSearchRefresh
    {
        private static bool Prefix(BuildUi __instance, string term) => ProgressiveBuildMenu.SearchChanged(__instance, term);
    }

    [HarmonyPatch(typeof(BuildUi), "UpdatePieceButtons")]
    internal static class BuildButtonVirtualization
    {
        private static bool Prefix(BuildUi __instance) => ProgressiveBuildMenu.BeforeNativeRebuild(__instance);
        private static void Postfix(BuildUi __instance) => ProgressiveBuildMenu.AfterNativeRebuild(__instance);
    }

    // Do not show temporary stands as extra crafting materials.
    [HarmonyPatch(typeof(ByMaterialPieceList), nameof(ByMaterialPieceList.GetAvailablePiecesWithTag))]
    internal static class MaterialsFilter
    {
        private static void Postfix(IList<Piece> resultOut)
        {
            for (int i = resultOut.Count - 1; i >= 0; i--)
                if (resultOut[i] && resultOut[i].gameObject.name.StartsWith(Plugin.PiecePrefix, StringComparison.Ordinal))
                    resultOut.RemoveAt(i);
        }
    }

    [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OnSelectPiece))]
    internal static class ChoiceSelection
    {
        private static bool Prefix(Piece piece)
        {
            MenuChoice choice = piece ? piece.GetComponent<MenuChoice>() : null;
            if (!choice) return true;
            Player player = Player.m_localPlayer;
            if (Plugin.Instance == null || !Plugin.Instance.SelectChoice(player, choice))
            {
                Debug.LogWarning("[Valerius Item Stand Menu] Could not select " + choice.ItemName + " from the Hammer menu.");
                return false;
            }
            if (!ZInput.IsTouchActive() || ZInput.HasDoubleTapped())
            {
                player.PlayButtonSound();
                Hud.CloseBuildUi();
            }
            return false;
        }
    }

    // Let players inspect item choices that exist only in this menu.
    [HarmonyPatch(typeof(Player), nameof(Player.IsPieceAvailable))]
    internal static class CatalogueHoverAvailability
    {
        private static void Postfix(Piece piece, ref bool __result)
        {
            if (!__result && piece && piece.GetComponent<MenuChoice>() && Plugin.Instance != null)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class CatalogueDetailPanel
    {
        private static void Prefix(ref Piece piece)
        {
            if (Plugin.Instance != null && Plugin.Instance.TryGetSelectedDisplayPiece(piece, out Piece display))
                piece = display;
        }
    }

    // Use the category number assigned by Jötunn.
    internal static class ItemStandUsageTab
    {
        private const string CategoryToken = "$jotunn_cat_item_stands";
        private static readonly Dictionary<ByUsagePieceList, int> TagIds =
            new Dictionary<ByUsagePieceList, int>();

        internal static void Updated(ByUsagePieceList usage, PieceTable table)
        {
            if (usage == null) return;
            TagIds.Remove(usage);
            if (Plugin.Instance == null || !Plugin.Instance.IsCatalogueTable(table)) return;

            // Read the categories Jötunn has already added.
            for (int index = 0; index < usage.TagCount; index++)
            {
                if (usage.GetTagDisplayName(index) != CategoryToken) continue;
                TagIds[usage] = usage.GetTagIdByIndex(index);
                return;
            }
        }

        internal static bool IsStandTagForAny(int tagId)
        {
            foreach (var pair in TagIds)
                if (pair.Key != null && pair.Value == tagId) return true;
            return false;
        }

        internal static bool IsStandTag(ByUsagePieceList usage, int tagId)
        {
            return usage != null && TagIds.TryGetValue(usage, out int registeredId)
                && registeredId == tagId;
        }
    }
}
