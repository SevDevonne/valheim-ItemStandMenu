using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ItemStandMenu
{
    internal static partial class ProgressiveBuildMenu
    {
        // Save only the full, unfiltered list.
        private static readonly List<BuildUiPieceButton> cachedAllButtons = new List<BuildUiPieceButton>();
        private static PieceTable cachedAllTool;
        private static int cachedAllFingerprint;
        private static bool hasCachedAll;
        private static int lastRenderedList = -1, lastRenderedTag = int.MinValue;
        private static PieceTable lastRenderedTool;
        private static string lastRenderedSearch = "";

        // Do not reuse the list if its item order has changed.
        private static int Fingerprint(PieceTable table)
        {
            if (!table) return 0;
            unchecked
            {
                int hash = 17;
                foreach (Piece piece in table.m_availablePieces)
                    hash = hash * 31 + (piece ? piece.GetInstanceID() : 0);
                return hash;
            }
        }

        private static void ReleaseShowAllCache()
        {
            if (!hasCachedAll) return;
            var nativePool = ui && NativePoolField != null
                ? NativePoolField.GetValue(ui) as Stack<BuildUiPieceButton> : null;
            foreach (BuildUiPieceButton button in cachedAllButtons)
            {
                if (!button) continue;
                button.gameObject.SetActive(false);
                if (nativePool != null) nativePool.Push(button);
            }
            cachedAllButtons.Clear();
            cachedAllTool = null;
            hasCachedAll = false;
        }

        private static void SaveShowAllBeforeEnteringStands(BuildUi buildUi)
        {
            if (hasCachedAll || lastRenderedList != 0 || lastRenderedTag != -1 ||
                lastRenderedSearch.Length != 0 || !string.IsNullOrEmpty(Search(buildUi))) return;
            PieceTable tool = Tool(buildUi);
            var native = ButtonsField?.GetValue(buildUi) as List<BuildUiPieceButton>;
            if (!tool || tool != lastRenderedTool || native == null || native.Count == 0) return;
            cachedAllTool = tool;
            cachedAllFingerprint = Fingerprint(tool);
            cachedAllButtons.AddRange(native);
            native.Clear(); // Keep Show All buttons out of BuildUi's temporary/pool stacks.
            foreach (BuildUiPieceButton button in cachedAllButtons)
                if (button) button.gameObject.SetActive(false);
            hasCachedAll = true;
        }

        private static bool RestoreShowAllWithoutRebuild(BuildUi buildUi)
        {
            if (!hasCachedAll || !ShowAllTag(buildUi) || Search(buildUi).Length != 0 ||
                Tool(buildUi) != cachedAllTool || Fingerprint(cachedAllTool) != cachedAllFingerprint)
                return false;

            var native = ButtonsField?.GetValue(buildUi) as List<BuildUiPieceButton>;
            var nativePool = NativePoolField?.GetValue(buildUi) as Stack<BuildUiPieceButton>;
            if (native == null || nativePool == null) return false;

            // Return unused menu buttons so Valheim can reuse them.
            foreach (BuildUiPieceButton button in native)
            {
                if (!button) continue;
                button.gameObject.SetActive(false);
                nativePool.Push(button);
            }
            native.Clear();
            native.AddRange(cachedAllButtons);
            cachedAllButtons.Clear();
            cachedAllTool = null;
            hasCachedAll = false;
            foreach (BuildUiPieceButton button in native)
                if (button) button.gameObject.SetActive(true);
            ConfigureNavigationMethod?.Invoke(buildUi, null);
            return true;
        }
    }
}
