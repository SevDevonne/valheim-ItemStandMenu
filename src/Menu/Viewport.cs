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
        private static readonly FieldInfo ScrollField = AccessTools.Field(typeof(BuildUi), "m_pieceScrollRect");
        private static readonly FieldInfo SearchField = AccessTools.Field(typeof(BuildUi), "m_searchField");
        private static readonly FieldInfo TagField = AccessTools.Field(typeof(BuildUi), "m_currentTagId");
        private static readonly FieldInfo TabField = AccessTools.Field(typeof(BuildUi), "m_currentPieceList");
        private static readonly FieldInfo ButtonsField = AccessTools.Field(typeof(BuildUi), "m_pieceButtons");
        private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(BuildUi), "m_pieceButtonsContainer");
        private static readonly FieldInfo ButtonPrefabField = AccessTools.Field(typeof(BuildUi), "m_pieceButtonPrefab");
        private static readonly PropertyInfo ButtonPiece = AccessTools.Property(typeof(BuildUiPieceButton), "Piece");
        private static readonly FieldInfo ButtonIcon = AccessTools.Field(typeof(BuildUiPieceButton), "m_icon");
        private static readonly FieldInfo ButtonUpgrade = AccessTools.Field(typeof(BuildUiPieceButton), "m_upgradeArrow");
        private static readonly FieldInfo ButtonFavorite = AccessTools.Field(typeof(BuildUiPieceButton), "m_favoriteStar");
        private static readonly FieldInfo ButtonOwner = AccessTools.Field(typeof(BuildUiPieceButton), "m_buildUi");
        private static readonly MethodInfo RefreshMethod = AccessTools.Method(typeof(BuildUi), "UpdatePieceButtons");
        private static readonly MethodInfo ConfigureNavigationMethod = AccessTools.Method(typeof(BuildUi), "ConfigureButtonNavigation");
        private static readonly FieldInfo ToolField = AccessTools.Field(typeof(BuildUi), "m_currentBuildTool");
        private static readonly FieldInfo NativePoolField = AccessTools.Field(typeof(BuildUi), "m_pieceButtonPool");

        private static BuildUi ui;
        private static ScrollRect scroll;
        private static RectTransform content;
        private static GridLayoutGroup grid;
        private static ContentSizeFitter fitter;
        private static bool originalGridEnabled, originalFitterEnabled;
        private static Vector2 originalContentSize;
        private static readonly List<BuildUiPieceButton> buttons = new List<BuildUiPieceButton>();
        private static readonly List<int> boundIndex = new List<int>();
        private static bool virtualMode, rebuilding, binding;
        private static int columns = 1, capacity, cachedRows, cachedColumns;
        private static float rowStep, colStep, totalHeight;
        private static string lastSearch = "";
        private static string savedStandSearch = "";
        private static float savedStandScroll = 1f;
        private static bool restoringStandScroll;

        private static int Tag(BuildUi buildUi) => buildUi != null && TagField != null ? (int)TagField.GetValue(buildUi) : -1;
        private static bool StandTag(BuildUi buildUi) => buildUi && TabField != null &&
            (int)TabField.GetValue(buildUi) == 0 && ItemStandUsageTab.IsStandTagForAny(Tag(buildUi));
        private static bool ShowAllTag(BuildUi buildUi) => buildUi && TabField != null &&
            (int)TabField.GetValue(buildUi) == 0 && Tag(buildUi) == -1;
        private static PieceTable Tool(BuildUi buildUi) =>
            buildUi && ToolField != null ? ToolField.GetValue(buildUi) as PieceTable : null;

        private static string Search(BuildUi buildUi)
        {
            object field = buildUi && SearchField != null ? SearchField.GetValue(buildUi) : null;
            return field == null ? "" : (field.GetType().GetProperty("text")?.GetValue(field, null) as string ?? "").Trim();
        }

        internal static void Attach(BuildUi buildUi)
        {
            if (!buildUi || ScrollField == null) return;
            var candidate = ScrollField.GetValue(buildUi) as ScrollRect;
            if (!candidate) return;
            if (ui == buildUi && scroll == candidate) return;
            if (scroll) scroll.onValueChanged.RemoveListener(OnScroll);
            Restore();
            ReleaseShowAllCache();
            lastRenderedList = -1;
            lastRenderedTag = int.MinValue;
            lastRenderedTool = null;
            ui = buildUi;
            scroll = candidate;
            scroll.onValueChanged.AddListener(OnScroll);
        }

        // Reuse the full item list only when nothing relevant has changed.
        internal static bool BeforeNativeRebuild(BuildUi buildUi)
        {
            rebuilding = true;
            if (buildUi != ui) Attach(buildUi);
            bool isStand = StandTag(buildUi);
            if (isStand)
                SaveShowAllBeforeEnteringStands(buildUi);
            if (virtualMode) Restore();
            if (!isStand)
            {
                if (RestoreShowAllWithoutRebuild(buildUi))
                {
                    return false;
                }
                ReleaseShowAllCache();
                return true;
            }
            lastSearch = Search(buildUi);
            // A different search produces a different list, so start at the top.
            if (!string.Equals(lastSearch, savedStandSearch, StringComparison.OrdinalIgnoreCase))
            {
                savedStandSearch = lastSearch;
                savedStandScroll = 1f;
            }
            Plugin.Instance?.SetSearch(lastSearch);
            return true;
        }

        internal static void AfterNativeRebuild(BuildUi buildUi)
        {
            rebuilding = false;
            if (StandTag(buildUi))
            {
                Activate();
                return;
            }
            if (!buildUi || TabField == null) return;
            lastRenderedList = (int)TabField.GetValue(buildUi);
            lastRenderedTag = Tag(buildUi);
            lastRenderedTool = Tool(buildUi);
            lastRenderedSearch = Search(buildUi);
        }

        private static void Activate()
        {
            if (virtualMode || !ui || !scroll || Plugin.Instance == null ||
                ContainerField == null || ButtonsField == null || ButtonPrefabField == null) return;
            content = ContainerField.GetValue(ui) as RectTransform;
            grid = content ? content.GetComponent<GridLayoutGroup>() : null;
            var native = ButtonsField.GetValue(ui) as List<BuildUiPieceButton>;
            var prefab = ButtonPrefabField.GetValue(ui) as GameObject;
            if (!content || !grid || native == null || !prefab || ButtonPiece == null || ButtonIcon == null || ButtonOwner == null)
                return;

            float width = scroll.viewport ? scroll.viewport.rect.width : content.rect.width;
            float height = scroll.viewport ? scroll.viewport.rect.height : content.rect.height;
            colStep = Mathf.Max(1f, grid.cellSize.x + grid.spacing.x);
            rowStep = Mathf.Max(1f, grid.cellSize.y + grid.spacing.y);
            columns = Mathf.Max(1, Mathf.FloorToInt((width - grid.padding.left - grid.padding.right + grid.spacing.x) / colStep));
            int visibleRows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(height, grid.cellSize.y) / rowStep));
            capacity = (visibleRows + 2) * columns;
            cachedRows = visibleRows;
            cachedColumns = columns;
            if (capacity <= 0 || capacity > 512) return; // unexpected UI scale: fail safe

            Plugin.Instance.EnsureMenuPool(capacity);
                // Let Valheim manage its original buttons.
            while (native.Count < capacity)
            {
                var created = UnityEngine.Object.Instantiate(prefab, content).GetComponent<BuildUiPieceButton>();
                if (!created) return;
                native.Add(created);
                created.gameObject.SetActive(false);
            }
            buttons.Clear();
            boundIndex.Clear();
            for (int i = 0; i < capacity; i++)
            {
                buttons.Add(native[i]);
                boundIndex.Add(-1);
            }
            originalGridEnabled = grid.enabled;
            originalContentSize = content.sizeDelta;
            fitter = content.GetComponent<ContentSizeFitter>();
            originalFitterEnabled = fitter && fitter.enabled;
            if (fitter) fitter.enabled = false;
            grid.enabled = false;
            virtualMode = true;
            restoringStandScroll = true;
            try
            {
                UpdateHeight();
                scroll.verticalNormalizedPosition = savedStandScroll;
            }
            finally { restoringStandScroll = false; }
            Bind(CurrentScrollRow());
        }

        private static void UpdateHeight()
        {
            if (!content || !grid || !scroll || Plugin.Instance == null) return;
            int rows = Mathf.CeilToInt((float)Plugin.Instance.MatchCount / columns);
            totalHeight = grid.padding.top + grid.padding.bottom +
                (rows > 0 ? rows * rowStep - grid.spacing.y : 0f);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(totalHeight, scroll.viewport ? scroll.viewport.rect.height : grid.cellSize.y));
        }

        private static void Bind(int scrollRow)
        {
            if (!virtualMode || binding || Plugin.Instance == null) return;
            int firstRow = Mathf.Max(0, scrollRow - 1); // one spare row above and below
            int first = firstRow * columns;
            binding = true;
            try
            {
                for (int absolute = first; absolute < first + capacity; absolute++)
                {
                    int slot = absolute % capacity;
                    var button = buttons[slot];
                    if (!button) continue;
                    if (absolute >= Plugin.Instance.MatchCount)
                    {
                        if (button.gameObject.activeSelf) button.gameObject.SetActive(false);
                        boundIndex[slot] = -1;
                        continue;
                    }
                    if (boundIndex[slot] != absolute)
                    {
                        Piece piece = Plugin.Instance.BindVisibleSlot(slot, absolute);
                        if (!piece) { button.gameObject.SetActive(false); boundIndex[slot] = -1; continue; }
                        // Avoid rebuilding button text and callbacks unnecessarily.
                        ButtonPiece.SetValue(button, piece, null);
                        ButtonOwner.SetValue(button, ui);
                        var icon = ButtonIcon.GetValue(button) as Image;
                        if (icon) { icon.sprite = piece.m_icon; icon.color = Color.white; }
                        var upgrade = ButtonUpgrade?.GetValue(button) as Image;
                        if (upgrade) upgrade.gameObject.SetActive(false);
                        var favorite = ButtonFavorite?.GetValue(button) as Image;
                        if (favorite) favorite.gameObject.SetActive(false);
                        OrientationBadge.Apply(button, piece);
                        var rect = button.transform as RectTransform;
                        if (rect)
                        {
                            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                            rect.pivot = new Vector2(0f, 1f);
                            rect.sizeDelta = grid.cellSize;
                            rect.anchoredPosition = new Vector2(grid.padding.left + (absolute % columns) * colStep,
                                -grid.padding.top - (absolute / columns) * rowStep);
                        }
                        boundIndex[slot] = absolute;
                    }
                    if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
                }
            }
            finally { binding = false; }
        }

        private static int CurrentScrollRow()
        {
            float viewport = scroll.viewport ? scroll.viewport.rect.height : 0f;
            float offset = Mathf.Max(0f, (content.rect.height - viewport) *
                (1f - scroll.verticalNormalizedPosition));
            return Mathf.FloorToInt(offset / rowStep);
        }

        private static void OnScroll(Vector2 _)
        {
            if (!virtualMode || rebuilding || binding || restoringStandScroll ||
                !ui || !scroll || !StandTag(ui)) return;
            savedStandScroll = Mathf.Clamp01(scroll.verticalNormalizedPosition);
            Bind(CurrentScrollRow());
        }

        private static void Restore()
        {
            if (!virtualMode) return;
            virtualMode = false;
            if (content) content.sizeDelta = originalContentSize;
            if (grid) grid.enabled = originalGridEnabled;
            if (fitter) fitter.enabled = originalFitterEnabled;
            buttons.Clear();
            boundIndex.Clear();
        }

        internal static void Tick()
        {
            if (virtualMode && ui && !StandTag(ui)) Restore();
            // Rebuild the menu when the screen or UI size changes.
            if (!virtualMode || !ui || !scroll || rebuilding) return;
            float width = scroll.viewport ? scroll.viewport.rect.width : content.rect.width;
            float height = scroll.viewport ? scroll.viewport.rect.height : content.rect.height;
            int cols = Mathf.Max(1, Mathf.FloorToInt((width - grid.padding.left - grid.padding.right + grid.spacing.x) / colStep));
            int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(height, grid.cellSize.y) / rowStep));
            if (cols != cachedColumns || rows != cachedRows) RefreshMethod?.Invoke(ui, null);
        }

        internal static bool SearchChanged(BuildUi buildUi, string term)
        {
            if (!buildUi || binding || !StandTag(buildUi)) return true;
            if (rebuilding) return false;
            term = (term ?? "").Trim();
            if (string.Equals(term, lastSearch, StringComparison.OrdinalIgnoreCase)) return false;
            lastSearch = term;
            savedStandSearch = term;
            savedStandScroll = 1f;
            Plugin.Instance?.SetSearch(term);
            if (virtualMode)
            {
                restoringStandScroll = true;
                try
                {
                    UpdateHeight();
                    for (int i = 0; i < boundIndex.Count; i++) boundIndex[i] = -1;
                    scroll.verticalNormalizedPosition = 1f;
                }
                finally { restoringStandScroll = false; }
                Bind(0);
            }
            else RefreshMethod?.Invoke(buildUi, null);
            return false;
        }

        internal static void Filter(ByUsagePieceList usage, PieceTable table, int tagId, IList<Piece> results)
        {
            if (results == null) return;
            for (int i = results.Count - 1; i >= 0; i--)
            {
                Piece p = results[i];
                if (p && p.gameObject.name.StartsWith(Plugin.PiecePrefix, StringComparison.Ordinal))
                    results.RemoveAt(i);
            }
            Plugin.Instance?.AppendChoices(table, tagId, usage, results);
        }
    }
}
