using System;
using System.Collections.Generic;
using Jotunn.Utils;

namespace ItemStandMenu
{
    // Prepare search data once instead of doing it for every key press.
    internal static class SearchIndex
    {
        internal sealed class Record
        {
            internal string Name;
            internal string Prefab;
            internal string Mod;
            internal string Category;
            internal string Orientation;
        }

        internal static Dictionary<string, string> GetModOrigins()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (IModPrefab registered in ModQuery.GetPrefabs())
            {
                if (registered?.Prefab == null || registered.SourceMod == null) continue;
                string name = registered.Prefab.name;
                string mod = registered.SourceMod.Name;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(mod))
                    result[name] = mod;
            }
            return result;
        }

        internal static Record Build(string label, string prefab, string mod, ItemDrop item, bool horizontal)
        {
            var fields = new List<string>();
            var shared = item.m_itemData?.m_shared;
            if (shared != null)
            {
                string type = shared.m_itemType.ToString();
                fields.Add(type);
                if (type.Contains("Weapon") || type == "Bow" || type == "Attach_Atgeir")
                {
                    fields.Add("weapon weapons");
                    string skill = shared.m_skillType.ToString();
                    if (skill != "None") fields.Add(skill);
                }
                if (type == "Helmet" || type == "Chest" || type == "Legs" ||
                    type == "Hands" || type == "Shoulder") fields.Add("armor armour equipment");
                if (type == "Material") fields.Add("material materials");
                if (type == "Consumable") fields.Add("consumable consumables food");
            }
            return new Record
            {
                Name = Normalize(label),
                Prefab = Normalize(prefab),
                Mod = Normalize(mod),
                Category = Normalize(string.Join(" ", fields.ToArray())),
                Orientation = horizontal ? "horizontal" : "vertical"
            };
        }

        internal static string[] Terms(string query)
        {
            return (query ?? string.Empty).ToLowerInvariant().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Prioritize item names over category and mod names in search results.
        internal static int Score(Record record, string[] terms)
        {
            if (record == null) return -1;
            if (terms.Length == 0) return 0;
            int total = 0;
            for (int i = 0; i < terms.Length; i++)
            {
                string term = Normalize(terms[i]);
                if (term.Length == 0) continue;
                int best = -1;
                if (record.Name == term) best = 100;
                else if (record.Name.StartsWith(term, StringComparison.Ordinal)) best = 75;
                else if (record.Name.Contains(term)) best = 60;
                if (record.Prefab == term) best = Math.Max(best, 55);
                else if (record.Prefab.Contains(term)) best = Math.Max(best, 40);
                if (record.Category.Contains(term)) best = Math.Max(best, 30);
                if (record.Mod.Contains(term)) best = Math.Max(best, 25);
                if (record.Orientation.Contains(term)) best = Math.Max(best, 10);
                if (best < 0) return -1;
                total += best;
            }
            return total;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).ToLowerInvariant()
                .Replace("_", "").Replace("-", "").Replace(" ", "");
        }
    }
}
