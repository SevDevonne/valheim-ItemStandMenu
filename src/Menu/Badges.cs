using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ItemStandMenu
{
    // Show H or V on each item icon, and clear old letters when reused.
    [HarmonyPatch(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.Setup))]
    internal static class OrientationBadge
    {
        private static readonly FieldInfo IconField = AccessTools.Field(typeof(BuildUiPieceButton), "m_icon");
        private static readonly FieldInfo LanguageChangeField =
            AccessTools.Field(AccessTools.TypeByName("Localization"), "OnLanguageChange");
        private static bool warnedOrientationLabels;

        private static void Prefix(BuildUiPieceButton __instance)
        {
            // Remove the old callback so each button responds only once.
            if (!__instance || LanguageChangeField == null) return;
            var handlers = LanguageChangeField.GetValue(null) as Action;
            if (handlers == null) return;
            LanguageChangeField.SetValue(null, (Action)Delegate.Remove(
                handlers, new Action(__instance.RefreshSearchTerm)));
        }

        private static string GetOrientation(string name)
        {
            if (name == null) return null;
            if (name.EndsWith(" (Horizontal)", StringComparison.Ordinal)) return "H";
            if (name.EndsWith(" (Vertical)", StringComparison.Ordinal)) return "V";
            return null;
        }

        internal static void WarnOrientationLabels()
        {
            if (warnedOrientationLabels) return;
            warnedOrientationLabels = true;
            Debug.LogWarning("[Valerius Item Stand Menu] Could not display orientation labels on Hammer menu icons.");
        }

        internal static void Apply(BuildUiPieceButton button, Piece piece)
        {
            Postfix(button, piece);
        }

        private static void Postfix(BuildUiPieceButton __instance, Piece pieceInfo)
        {
            if (!__instance) return;

            var badge = __instance.GetComponent<OrientationBadgeView>();
            if (!pieceInfo || !pieceInfo.GetComponent<MenuChoice>())
            {

                if (badge) badge.SetOrientation(null);
                return;
            }

            string orientation = GetOrientation(pieceInfo.m_name);
            if (orientation == null)
            {
                if (badge) badge.SetOrientation(null);
                return;
            }

            if (!badge)
            {
                Image icon = IconField?.GetValue(__instance) as Image;
                if (!icon)
                {
                    WarnOrientationLabels();
                    return;
                }

                badge = __instance.gameObject.AddComponent<OrientationBadgeView>();
                badge.Initialize(icon.rectTransform);
            }

            badge.SetOrientation(orientation);
        }
    }

    internal sealed class OrientationBadgeView : MonoBehaviour
    {
        private static TMP_FontAsset cachedFont;
        private static Material outlinedMaterial;
        private TextMeshProUGUI label;

        internal void Initialize(RectTransform icon)
        {

            TMP_FontAsset font = FindGameFont(icon);
            if (!font || !font.material)
            {
                OrientationBadge.WarnOrientationLabels();
                return;
            }

            var obj = new GameObject("ItemStand_OrientationBadge", typeof(RectTransform));
            obj.SetActive(false);
            var rect = (RectTransform)obj.transform;
            rect.SetParent(icon, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-2f, 2f);
            rect.sizeDelta = new Vector2(27f, 27f);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            var textObject = new GameObject("Letter", typeof(RectTransform));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(0.5f, 0f);
            textRect.offsetMax = new Vector2(-0.5f, 0f);
            textRect.localScale = Vector3.one;

            label = textObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 25f;
            label.fontStyle = FontStyles.Bold;
            label.enableWordWrapping = false;
            label.color = new Color32(255, 202, 96, 255);
            // A shared text material avoids crashes when hidden menu buttons are updated.
            if (!outlinedMaterial)
            {
                outlinedMaterial = new Material(font.material);
                outlinedMaterial.name = "ItemStand_BadgeOutline";
                if (outlinedMaterial.HasProperty(ShaderUtilities.ID_OutlineColor))
                    outlinedMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                if (outlinedMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
                    outlinedMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.20f);
                // Make the letters thicker without enlarging their black border.
                if (outlinedMaterial.HasProperty(ShaderUtilities.ID_FaceDilate))
                    outlinedMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.12f);
            }
            label.fontSharedMaterial = outlinedMaterial;

        }

        private static TMP_FontAsset FindGameFont(RectTransform icon)
        {
            if (cachedFont && cachedFont.material) return cachedFont;
            // Match the game menu font, even when some buttons are hidden.
            var ui = icon.GetComponentInParent<BuildUi>();
            if (ui)
                foreach (TextMeshProUGUI text in ui.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (text && text.font && text.font.material) return cachedFont = text.font;

            foreach (TextMeshProUGUI text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                if (text && text.font && text.font.material) return cachedFont = text.font;

            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font && font.material) return cachedFont = font;
            return null;
        }

        internal void SetOrientation(string orientation)
        {
            if (!label) return;
            label.text = orientation ?? string.Empty;
            label.transform.parent.gameObject.SetActive(orientation != null);
        }
    }
}
