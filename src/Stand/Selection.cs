using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace ItemStandMenu
{
    public sealed partial class Plugin
    {
        private static Piece CreateProxy(GameObject original, bool isHorizontal, Piece.PieceCategory category)
        {
            bool oldFlag = ZNetView.m_forceDisableInit;
            GameObject obj;
            try
            {
                SmoothBrainSailing.CreatingProxy = true;
                ZNetView.m_forceDisableInit = true;
                obj = UnityEngine.Object.Instantiate(original);
            }
            finally
            {
                ZNetView.m_forceDisableInit = oldFlag;
                SmoothBrainSailing.CreatingProxy = false;
            }
            if (!obj) return null;
            obj.name = PiecePrefix + (isHorizontal ? "Horizontal" : "Vertical");
            obj.transform.position = new Vector3(0f, -10000f, 0f);
            obj.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Piece piece = obj.GetComponent<Piece>();
            if (!piece) { UnityEngine.Object.Destroy(obj); return null; }
            piece.m_name = isHorizontal ? "Item Stand (Horizontal)" : "Item Stand (Vertical)";
            piece.m_description = "Place a normal item stand with the selected item attached.";
            piece.m_category = category;
            piece.m_usage = Piece.UsageTagFlags.Decor;
            // Build with the normal stand materials; handle the displayed item separately.
            var marker = obj.AddComponent<ShortcutPiece>();
            marker.StandPrefabName = original.name;
            marker.StandPiece = original.GetComponent<Piece>();
            if (isHorizontal) PlacementGhost.AddDirectionArrow(obj);
            return piece;
        }

        internal bool SelectChoice(Player player, MenuChoice choice)
        {
            if (!player || !choice) return false;
            Piece target = choice.Horizontal ? horizontal : vertical;
            if (!target || !table || !table.IsPieceAvailable(target)) return false;
            Piece previous = player.GetSelectedPiece();
            ShortcutPiece marker = target.GetComponent<ShortcutPiece>();
            marker.ItemPrefabName = choice.ItemName;

            bool selected = player.SetSelectedPiece(target);
            if (!selected) return false;
            selectedPlacementProxy = target;
            // Remember the selected item before the menu reuses its button.
            if (!selectedDisplayPiece)
            {
                var displayObj = new GameObject("ItemStand_SelectedDisplay");
                displayObj.SetActive(false);
                DontDestroyOnLoad(displayObj);
                selectedDisplayPiece = displayObj.AddComponent<Piece>();
            }
            Piece choicePiece = choice.GetComponent<Piece>();
            selectedDisplayPiece.m_name = choicePiece.m_name;
            selectedDisplayPiece.m_description = choicePiece.m_description;
            selectedDisplayPiece.m_icon = choicePiece.m_icon;
            selectedDisplayPiece.m_resources = choicePiece.m_resources;
            selectedDisplayPiece.m_usage = choicePiece.m_usage;
            selectedDisplayPiece.m_craftingStation = choicePiece.m_craftingStation;
            if (previous == target)
                AccessTools.Method(typeof(Player), "SetupPlacementGhost")?.Invoke(player, null);

            return true;
        }
    }

    // This menu choice is not a separate building piece.
    internal sealed class MenuChoice : MonoBehaviour
    {
        internal string ItemName;
        internal bool Horizontal;
    }
}
