using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    // Use Valheim or an installed mod to find a model for the chosen item.
    internal static class Attachment
    {
        private static bool providerWarningLogged;
        private static bool modelWarningLogged;

        private static readonly MethodInfo ExternalGetAttach = AccessTools.Method(
            AccessTools.TypeByName("ItemStandAllItems.Attacher"), "GetAttach", new[] { typeof(GameObject) });

        internal static GameObject GetPrefab(GameObject item)
        {
            if (!item) return null;
            GameObject prefab = ItemStand.GetAttachPrefab(item);
            if (prefab || ExternalGetAttach == null) return prefab;
            try { return ExternalGetAttach.Invoke(null, new object[] { item }) as GameObject; }
            catch (Exception ex)
            {
                if (!providerWarningLogged)
                {
                    providerWarningLogged = true;
                    Debug.LogWarning("[Valerius Item Stand Menu] Attachment provider failed: " + ex.GetBaseException().Message);
                }
                return null;
            }
        }

        internal static bool HasModel(GameObject item)
        {
            try
            {
                GameObject attach = GetPrefab(item);
                if (!attach) return false;
                GameObject model = ItemStand.GetAttachGameObject(attach);
                return model && model.GetComponentInChildren<Renderer>(true) != null;
            }
            catch (Exception ex)
            {
                if (!modelWarningLogged)
                {
                    modelWarningLogged = true;
                    Debug.LogWarning("[Valerius Item Stand Menu] Attachment model inspection failed: " + ex.GetBaseException().Message);
                }
                return false;
            }
        }
    }
}
