using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    // Sailing expects parts that our temporary stands do not have.
    // Skip that check for previews without affecting real structures or ships.
    internal static class SmoothBrainSailing
    {
        [ThreadStatic]
        internal static bool CreatingProxy;

        internal static void TryInstall(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName("Sailing.Sailing+IncreaseHealth");
            if (patchType == null) return;
            MethodInfo target = AccessTools.Method(patchType, "Prefix", new[] { typeof(WearNTear) });
            MethodInfo guard = AccessTools.Method(typeof(SmoothBrainSailing), nameof(BeforeSailingPrefix));
            if (target == null || guard == null)
            {
                Debug.LogWarning("[Valerius Item Stand Menu] Sailing compatibility unavailable: IncreaseHealth signature changed.");
                return;
            }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(guard) { priority = Priority.First });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Valerius Item Stand Menu] Sailing compatibility patch failed: " + ex);
            }
        }

        private static bool BeforeSailingPrefix(WearNTear __0)
        {
            if (CreatingProxy) return false;
            return !__0 || !__0.GetComponent<ShortcutPiece>();
        }
    }
}
