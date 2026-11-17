using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace ItemStandMenu
{
    public sealed partial class Plugin
    {
        private void Register()
        {
            if (!ObjectDB.instance || building) return;
            if (HasCatalogue && lastDatabaseCount == ObjectDB.instance.m_items.Count) return;
            PieceTable hammer = PieceManager.Instance.GetPieceTable("Hammer");
            GameObject h = PrefabManager.Instance.GetPrefab("itemstandh");
            GameObject v = PrefabManager.Instance.GetPrefab("itemstand");
            if (!hammer || !h || !v) return;
            building = true;
            try
            {
                if (table && table != hammer)
                {
                    if (horizontal) { table.m_pieces.Remove(horizontal.gameObject); Destroy(horizontal.gameObject); }
                    if (vertical) { table.m_pieces.Remove(vertical.gameObject); Destroy(vertical.gameObject); }
                    horizontal = null;
                    vertical = null;
                }
                table = hammer;
                // Add only one horizontal and one vertical stand to the build menu.
                Piece.PieceCategory category = PieceManager.Instance.AddPieceCategory("Item Stands");
                if (!horizontal) horizontal = CreateProxy(h, true, category);
                if (!vertical) vertical = CreateProxy(v, false, category);
                if (!horizontal || !vertical) return;
                if (!table.m_pieces.Contains(horizontal.gameObject)) table.m_pieces.Add(horizontal.gameObject);
                if (!table.m_pieces.Contains(vertical.gameObject)) table.m_pieces.Add(vertical.gameObject);

                var items = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                foreach (GameObject item in ObjectDB.instance.m_items)
                    if (item && item.GetComponent<ItemDrop>() && !item.name.EndsWith("(Clone)", StringComparison.Ordinal)) items[item.name] = item;
                foreach (ItemDrop drop in Resources.FindObjectsOfTypeAll<ItemDrop>())
                {
                    if (!drop || !drop.gameObject || drop.gameObject.name.EndsWith("(Clone)", StringComparison.Ordinal)) continue;
                    string name = drop.gameObject.name;
                    if (items.ContainsKey(name)) continue;
                    GameObject prefab = PrefabManager.Instance.GetPrefab(name);
                    if (prefab && prefab.GetComponent<ItemDrop>()) items[name] = prefab;
                }
                var origins = SearchIndex.GetModOrigins();
                var keys = new List<string>(items.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                catalogue.Clear();
                foreach (string name in keys)
                {
                    ItemDrop drop = items[name].GetComponent<ItemDrop>();
                    if (!drop || drop.m_itemData == null) continue;
                    // Include only items that can be displayed on a stand.
                    if (!Attachment.HasModel(items[name])) continue;
                    Sprite icon = drop.m_itemData.GetIcon();
                    if (!icon) continue;
                    string token = drop.m_itemData.m_shared?.m_name;
                    string label = string.IsNullOrEmpty(token) ? name : LocalizationManager.Instance.TryTranslate(token);
                    if (string.IsNullOrEmpty(label)) label = name;
                    string description = drop.m_itemData.m_shared?.m_description ?? string.Empty;
                    // Show both stand choices without adding new building pieces.
                    origins.TryGetValue(name, out string mod);
                    AddEntry(name, label, icon, description, drop, true, SearchIndex.Build(label, name, mod, drop, true));
                    AddEntry(name, label, icon, description, drop, false, SearchIndex.Build(label, name, mod, drop, false));
                }
                RebuildMatches();
                SetLoadedCount(BatchSize);
                lastDatabaseCount = ObjectDB.instance.m_items.Count;
            }
            finally { building = false; }
        }

        private void AddEntry(string name, string label, Sprite icon, string description, ItemDrop attachment, bool horizontalSide, SearchIndex.Record searchable)
        {
            Piece stand = StandPieceFor(horizontalSide);
            Piece.Requirement[] original = stand?.m_resources ?? Array.Empty<Piece.Requirement>();
            // Show the displayed item as a requirement only in the menu.
            var recipe = new Piece.Requirement[original.Length + 1];
            Array.Copy(original, recipe, original.Length);
            recipe[original.Length] = new Piece.Requirement
            {
                m_resItem = attachment,
                m_amount = 1,
                m_recover = false
            };
            catalogue.Add(new Entry
            {
                ItemName = name, Label = label, Icon = icon, Horizontal = horizontalSide,
                Description = description, DisplayRequirements = recipe,
                SearchKey = searchable
            });
        }

        private void RebuildMatches()
        {
            matches.Clear();
            string[] terms = SearchIndex.Terms(search);
            if (terms.Length == 0)
            {
                matches.AddRange(catalogue);
                return;
            }
            var ranked = new List<KeyValuePair<Entry, int>>();
            foreach (Entry entry in catalogue)
            {
                int score = SearchIndex.Score(entry.SearchKey, terms);
                if (score >= 0) ranked.Add(new KeyValuePair<Entry, int>(entry, score));
            }
            // Keep horizontal and vertical choices together when names match.
            foreach (var pair in ranked.OrderByDescending(pair => pair.Value)) matches.Add(pair.Key);
        }

        internal bool SetSearch(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.Equals(search, value, StringComparison.OrdinalIgnoreCase)) return false;
            search = value;
            RebuildMatches();
            SetLoadedCount(BatchSize);
            return true;
        }

        // Reuse menu entries instead of creating world objects.
        internal void EnsureMenuPool(int count)
        {
            while (menuPool.Count < count)
            {
                var obj = new GameObject("ItemStand_MenuIcon");
                obj.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(obj);
                var piece = obj.AddComponent<Piece>();
                obj.AddComponent<MenuChoice>();
                menuPool.Add(piece);
            }
        }

        internal Piece BindVisibleSlot(int slot, int absolute)
        {
            if (slot < 0 || absolute < 0 || absolute >= matches.Count) return null;
            EnsureMenuPool(slot + 1);
            Entry entry = matches[absolute];
            Piece piece = menuPool[slot];
            piece.m_name = entry.Label + (entry.Horizontal ? " (Horizontal)" : " (Vertical)");
            piece.m_description = entry.Description;
            piece.m_icon = entry.Icon;
            piece.m_resources = entry.DisplayRequirements;
            piece.m_usage = Piece.UsageTagFlags.Decor;
            piece.m_craftingStation = StandPieceFor(entry.Horizontal)?.m_craftingStation;
            MenuChoice choice = piece.GetComponent<MenuChoice>();
            choice.ItemName = entry.ItemName;
            choice.Horizontal = entry.Horizontal;
            return piece;
        }

        private void SetLoadedCount(int requested)
        {
            EnsureMenuPool(1);
            shown = Mathf.Min(1, matches.Count);
            windowStart = 0;
            if (shown != 0) BindVisibleSlot(0, 0);
        }

        internal void AppendChoices(PieceTable source, int tagId, ByUsagePieceList usageList, IList<Piece> pieces)
        {
            if (source != table || !horizontal || !vertical) return;

            if (!ItemStandUsageTab.IsStandTag(usageList, tagId)) return;

            if (!source.m_availablePieces.Contains(horizontal) && !source.m_availablePieces.Contains(vertical)) return;
            for (int i = 0; i < shown; i++) pieces.Add(menuPool[i]);
        }

    }
}
