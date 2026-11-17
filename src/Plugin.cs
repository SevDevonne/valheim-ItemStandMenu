using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ItemStandMenu
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        internal const string PiecePrefix = "ItemStandMenu_Proxy";
        internal const int BatchSize = 1;

        internal sealed class Entry
        {
            internal string ItemName;
            internal string Label;
            internal Sprite Icon;
            internal bool Horizontal;
            internal SearchIndex.Record SearchKey;
            internal string Description;
            internal Piece.Requirement[] DisplayRequirements;
        }

        internal static Plugin Instance { get; private set; }
        private Harmony harmony;
        private PieceTable table;
        private Piece horizontal;
        private Piece vertical;
        private readonly List<Entry> catalogue = new List<Entry>();
        private readonly List<Entry> matches = new List<Entry>();
        private readonly List<Piece> menuPool = new List<Piece>();
        private string search = string.Empty;
        private int shown;
        private int windowStart;
        private bool building;
        private int lastDatabaseCount = -1;
        private Piece selectedDisplayPiece;
        private Piece selectedPlacementProxy;
        internal bool TryGetSelectedDisplayPiece(Piece requested, out Piece display)
        {
            display = null;
            if (!requested || requested != selectedPlacementProxy || !selectedDisplayPiece) return false;
            display = selectedDisplayPiece;
            return true;
        }

        private void Update() => ProgressiveBuildMenu.Tick();
        internal Piece StandPieceFor(bool h) => h ? horizontal?.GetComponent<ShortcutPiece>()?.StandPiece : vertical?.GetComponent<ShortcutPiece>()?.StandPiece;
        internal int MatchCount => matches.Count;
        internal int LoadedCount => shown;
        internal Piece VisiblePiece(int index) => index >= 0 && index < shown ? menuPool[index] : null;
        internal int WindowStart => windowStart;
        internal bool HasCatalogue => table && horizontal && vertical && catalogue.Count > 0;
        internal bool IsCatalogueTable(PieceTable candidate) => HasCatalogue && candidate == table;

        private void Awake()
        {
            Instance = this;
            ModQuery.Enable();
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            SmoothBrainSailing.TryInstall(harmony);
            PieceManager.OnPiecesRegistered += Register;
            ItemManager.OnItemsRegisteredFejd += Register;
        }

        private void OnDestroy()
        {
            PieceManager.OnPiecesRegistered -= Register;
            ItemManager.OnItemsRegisteredFejd -= Register;
            harmony?.UnpatchSelf();
            if (table)
            {
                if (horizontal) table.m_pieces.Remove(horizontal.gameObject);
                if (vertical) table.m_pieces.Remove(vertical.gameObject);
            }
            if (selectedDisplayPiece) Destroy(selectedDisplayPiece.gameObject);
            selectedDisplayPiece = null;
            selectedPlacementProxy = null;
            foreach (Piece piece in menuPool) if (piece) Destroy(piece.gameObject);
            if (horizontal) Destroy(horizontal.gameObject);
            if (vertical) Destroy(vertical.gameObject);
            Instance = null;
        }

    }
}
