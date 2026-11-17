using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    // Keep temporary menu stands out of the multiplayer world.
    [HarmonyPatch(typeof(WearNTear), "Awake")]
    internal static class ShortcutGhostWear
    {
        private static bool Prefix(WearNTear __instance)
        {
            return !__instance.GetComponent<ShortcutPiece>();
        }
    }

    [HarmonyPatch(typeof(ItemStand), "Awake")]
    internal static class ShortcutCaptureStand
    {
        private static bool Prefix(ItemStand __instance)
        {
            return !__instance.GetComponent<ShortcutPiece>();
        }

        private static void Postfix(ItemStand __instance)
        {
            var pending = ShortcutPlacement.Current;
            if (pending == null || pending.Spawned || !__instance) return;
            string name = __instance.gameObject.name;
            if (name == pending.StandName + "(Clone)")
                pending.Spawned = __instance;
        }
    }
}
