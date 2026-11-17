using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemStandMenu
{
    // Only change what the player sees before placing a stand.
    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class PlacementGhost
    {
        private static readonly FieldInfo GhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly HashSet<string> Warnings = new HashSet<string>();
        private static Material directionMaterial;
        private const float ArrowThickness = 0.02f;
        private static readonly Vector3[] ArrowFootprint =
        {
            new Vector3(-0.014f, 0f, -0.09f),
            new Vector3( 0.014f, 0f, -0.09f),
            new Vector3(-0.014f, 0f,  0.03f),
            new Vector3( 0.014f, 0f,  0.03f),
            new Vector3(-0.058f, 0f,  0.03f),
            new Vector3( 0.058f, 0f,  0.03f),
            new Vector3( 0f,      0f,  0.09f)
        };
        private static readonly int[] ArrowTopFaces = { 0, 1, 2, 1, 3, 2, 4, 2, 6, 2, 3, 6, 3, 5, 6 };
        private static readonly int[] ArrowPerimeter = { 0, 1, 3, 5, 6, 4, 2 };

        // Temporarily enable the stand so Valheim can create its placement preview.
        private static void Prefix(Player __instance, out GameObject __state)
        {
            __state = null;
            Piece selected = __instance.GetSelectedPiece();
            if (!selected || !selected.GetComponent<ShortcutPiece>()) return;
            GameObject proxy = selected.gameObject;
            if (!proxy.activeSelf)
            {
                __state = proxy;
                proxy.SetActive(true);
            }
        }

        private static void Postfix(Player __instance, GameObject __state)
        {

            if (__state) __state.SetActive(false);
            if (GhostField == null) return;
            GameObject ghost = GhostField.GetValue(__instance) as GameObject;
            if (!ghost) return;

            // Look up the chosen item again because the preview may not remember it.
            Piece selected = __instance.GetSelectedPiece();
            ShortcutPiece marker = selected ? selected.GetComponent<ShortcutPiece>() : null;
            if (!marker || string.IsNullOrEmpty(marker.ItemPrefabName)) return;
            // The arrow is already included. Avoid showing the same item twice.
            if (ghost.transform.Find("ItemStand_ItemPreview") ||
                ghost.GetComponentInChildren<PlacementPreviewMarker>(true)) return;

            ItemStand stand = ghost.GetComponent<ItemStand>();
            if (!stand) return;
            GameObject item = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(marker.ItemPrefabName) : null;
            ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
            if (!drop || drop.m_itemData == null)
            {
                WarnOnce(marker.ItemPrefabName, "model", "Could not find 3D model for " + marker.ItemPrefabName + ".");
                return;
            }

            try
            {
                // Check other installed mods if Valheim cannot display this item.
                GameObject attachPrefab = Attachment.GetPrefab(item);
                if (!attachPrefab)
                {
                    WarnOnce(marker.ItemPrefabName, "model", "Could not find 3D model for " + marker.ItemPrefabName + ".");
                    return;
                }

                GameObject modelPrefab = ItemStand.GetAttachGameObject(attachPrefab);
                Transform attachPoint = stand.GetAttach(drop.m_itemData);
                if (!modelPrefab || !attachPoint)
                {
                    WarnOnce(marker.ItemPrefabName, "preview", "Could not preview " + marker.ItemPrefabName + " on the item stand.");
                    return;
                }

                GameObject preview = UnityEngine.Object.Instantiate(modelPrefab, attachPoint, false);
                preview.name = "ItemStand_ItemPreview";
                preview.transform.localPosition = attachPrefab.transform.localPosition;
                preview.transform.localRotation = attachPrefab.transform.localRotation;
                preview.transform.localScale = Vector3.Scale(
                    attachPrefab.transform.localScale, drop.m_itemData.GetScale(1));

                var settings = new List<ItemStand.OrientationSettings>();
                stand.GetOrientationSettings(drop.m_itemData, ref settings);
                if (settings.Count > 0)
                {
                    var s = settings[0];
                    preview.transform.localPosition += s.m_position;
                    preview.transform.localRotation *= Quaternion.Euler(s.m_rotation);
                    preview.transform.localScale = Vector3.Scale(preview.transform.localScale, s.m_scale);
                }

                foreach (MonoBehaviour component in preview.GetComponentsInChildren<MonoBehaviour>(true))
                    component.enabled = false;
                foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
                    body.isKinematic = true;

                preview.SetActive(true);
                bool hasVisibleRenderer = false;
                foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>(true))
                    if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        hasVisibleRenderer = true;
                        break;
                    }
                if (!hasVisibleRenderer)
                {
                    UnityEngine.Object.Destroy(preview);
                    WarnOnce(marker.ItemPrefabName, "preview", "Could not preview " + marker.ItemPrefabName + " on the item stand.");
                    return;
                }
                preview.AddComponent<PlacementPreviewMarker>();

            }
            catch (Exception ex)
            {
                WarnOnce(marker.ItemPrefabName, "preview", "Could not preview " + marker.ItemPrefabName + " on the item stand.", ex);
            }
        }

        // Add the arrow before creating the preview, so placed stands never keep it.
        internal static void AddDirectionArrow(GameObject proxy)
        {
            if (!proxy || proxy.transform.Find("ItemStand_DirectionPreview")) return;

            var arrow = new GameObject("ItemStand_DirectionPreview");
            arrow.transform.SetParent(proxy.transform, false);
            // Keep the arrow close to the top of the stand.
            arrow.transform.localPosition = new Vector3(0f, 0.035f, 0f);

            // Give the small arrow enough thickness to see it from the side.
            var vertices = new Vector3[ArrowFootprint.Length * 2];
            for (int i = 0; i < ArrowFootprint.Length; i++)
            {
                vertices[i] = ArrowFootprint[i] + Vector3.up * ArrowThickness;
                vertices[i + ArrowFootprint.Length] = ArrowFootprint[i];
            }
            var triangles = new List<int>(ArrowTopFaces.Length * 2 + ArrowPerimeter.Length * 6);
            triangles.AddRange(ArrowTopFaces);
            for (int i = 0; i < ArrowTopFaces.Length; i += 3)
            {
                triangles.Add(ArrowTopFaces[i] + ArrowFootprint.Length);
                triangles.Add(ArrowTopFaces[i + 2] + ArrowFootprint.Length);
                triangles.Add(ArrowTopFaces[i + 1] + ArrowFootprint.Length);
            }
            for (int i = 0; i < ArrowPerimeter.Length; i++)
            {
                int current = ArrowPerimeter[i];
                int next = ArrowPerimeter[(i + 1) % ArrowPerimeter.Length];
                triangles.Add(current);
                triangles.Add(current + ArrowFootprint.Length);
                triangles.Add(next);
                triangles.Add(next);
                triangles.Add(current + ArrowFootprint.Length);
                triangles.Add(next + ArrowFootprint.Length);
            }
            var mesh = new Mesh { name = "ItemStand_DirectionArrow" };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            arrow.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = arrow.AddComponent<MeshRenderer>();
            if (!directionMaterial)
            {
                // Use a material that supports transparency.
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
                if (!shader)
                {
                    UnityEngine.Object.Destroy(arrow);
                    Debug.LogWarning("[Valerius Item Stand Menu] Transparent arrow shader unavailable.");
                    return;
                }
                directionMaterial = new Material(shader)
                {
                    name = "ItemStand_ArrowMaterial",
                    color = new Color(0.16f, 0.65f, 1f, 0.26f)
                };
            }
            renderer.sharedMaterial = directionMaterial;
        }

        private static void WarnOnce(string item, string category, string message, Exception exception = null)
        {
            if (!Warnings.Add(item + ":" + category)) return;
            Debug.LogWarning("[Valerius Item Stand Menu] " + message);
            if (exception != null) Debug.LogException(exception);
        }
    }

    internal sealed class PlacementPreviewMarker : MonoBehaviour { }
}
