/*******************************************************************************
Copyright © 2015-2022 PICO Technology Co., Ltd.All rights reserved.

NOTICE：All information contained herein is, and remains the property of
PICO Technology Co., Ltd. The intellectual and technical concepts
contained herein are proprietary to PICO Technology Co., Ltd. and may be
covered by patents, patents in process, and are protected by trade secret or
copyright law. Dissemination of this information or reproduction of this
material is strictly forbidden unless prior written permission is obtained from
PICO Technology Co., Ltd.
*******************************************************************************/

// PXR_MCP_Features.cs
// Step 1 deliverable: four building blocks (VST / Controller / Locomotion / SpatialMesh)
// as plain Editor functions + MenuItems for manual validation.
// Dependency graph:
//   Common(EnsureXROrigin) -> VST           (uses Main Camera only; no Controller/Locomotion bleed-in)
//                          -> Controller    (re-shows Left/Right Controller GO + XRInputModalityManager)
//                          -> Locomotion    (re-shows Locomotion subtree + CharacterController(Driver))
//                          -> SpatialMesh   (calls VST first)
//
// Module visibility contract (see PXR_MCP_Common.InitiallyHideNonCoreModules):
//   - EnsureXROrigin first-create hides Controller + Locomotion modules so VST stays clean.
//   - Each block re-enables ONLY what it owns via SetControllerModuleVisible /
//     SetLocomotionModuleVisible. Disable / Remove flip them back off.
using System;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    // ---------------- VST ----------------
    public static class PXR_MCP_VST
    {
        public const string MarkerChild = "[PICO_MCP] VST Marker";

        [MenuItem("PICO MCP/VST/Ensure")]
        public static bool Ensure()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;

            // Idempotency marker.
            var marker = origin.transform.Find(MarkerChild);
            if (marker != null) { Debug.Log("[PICO MCP] VST already enabled."); return true; }

            var cam = PXR_MCP_Common.GetMainCamera(origin);
            if (cam == null) { Debug.LogError("[PICO MCP] No Camera under XR Origin."); return false; }
            Undo.RecordObject(cam, "PICO MCP VST configure camera");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);

            if (cam.gameObject.GetComponent<PXR_CameraEffectBlock>() == null)
                Undo.AddComponent<PXR_CameraEffectBlock>(cam.gameObject);
            var m = new GameObject(MarkerChild);
            Undo.RegisterCreatedObjectUndo(m, "PICO MCP VST marker");
            m.transform.SetParent(origin.transform, false);
            m.SetActive(false);
            Debug.Log("[PICO MCP] VST enabled.");
            return true;
        }

        [MenuItem("PICO MCP/VST/Remove")]
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { Debug.Log("[PICO MCP] No agent XR Origin."); return; }
            var m = origin.transform.Find(MarkerChild);
            if (m != null) Undo.DestroyObjectImmediate(m.gameObject);
            Debug.Log("[PICO MCP] VST removed.");
        }
    }

    // ---------------- Controller ----------------
    public static class PXR_MCP_Controller
    {
        public const string LeftPrefab  = "Packages/com.bytedance.pico.xr/Assets/Resources/Prefabs/LeftControllerModel.prefab";
        public const string RightPrefab = "Packages/com.bytedance.pico.xr/Assets/Resources/Prefabs/RightControllerModel.prefab";
        public const string MarkerLeft  = "[PICO_MCP] Left Controller Model";
        public const string MarkerRight = "[PICO_MCP] Right Controller Model";

        [MenuItem("PICO MCP/Controller/Ensure")]
        public static bool Ensure()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;

            // Re-show what InitiallyHideNonCoreModules() hid: Left/Right Controller GO
            // + XRInputModalityManager on the root. Idempotent.
            PXR_MCP_Common.SetControllerModuleVisible(origin.gameObject, true);

            var camOffset = origin.transform.Find(PXR_MCP_Common.CameraOffsetName);
            if (camOffset == null) { Debug.LogError("[PICO MCP] Camera Offset not found."); return false; }
            var left  = camOffset.Find(PXR_MCP_Common.LeftControllerName);
            var right = camOffset.Find(PXR_MCP_Common.RightControllerName);
            if (left == null || right == null) { Debug.LogError("[PICO MCP] Left/Right Controller missing."); return false; }

            Mount(left,  LeftPrefab,  "Left Controller Visual",  MarkerLeft);
            Mount(right, RightPrefab, "Right Controller Visual", MarkerRight);
            Debug.Log("[PICO MCP] Controller mounted.");
            return true;
        }

        [MenuItem("PICO MCP/Controller/Remove")]
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t == null) continue;
                if (t.name == MarkerLeft || t.name == MarkerRight) Undo.DestroyObjectImmediate(t.gameObject);
            }

            // Restore initial-hide state so subsequent block actions don't see the
            // Controller module as "enabled" just because it was once on.
            PXR_MCP_Common.SetControllerModuleVisible(origin.gameObject, false);

            Debug.Log("[PICO MCP] Controller removed.");
        }

        static void Mount(Transform parent, string prefabPath, string defaultVisualName, string markerName)
        {
            if (parent.Find(markerName) != null) return; // idempotent
            var visual = parent.Find(defaultVisualName);
            if (visual != null && visual.gameObject.activeSelf)
            {
                Undo.RecordObject(visual.gameObject, "Disable XRI default controller visual");
                visual.gameObject.SetActive(false);
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) { Debug.LogError("[PICO MCP] Controller prefab missing: " + prefabPath); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            Undo.RegisterCreatedObjectUndo(inst, "PICO MCP mount controller");
            inst.name = markerName;
        }
    }

    // ---------------- Locomotion ----------------
    [Flags]
    public enum LocomotionFlags
    {
        None = 0, Move = 1, Turn = 2, Teleportation = 4,
        GrabMove = 8, Climb = 16, Gravity = 32, Jump = 64,
        Default = Move | Turn | Teleportation,
        All     = Move | Turn | Teleportation | GrabMove | Climb | Gravity | Jump,
    }

    public static class PXR_MCP_Locomotion
    {
        static readonly (LocomotionFlags flag, string child)[] Map = {
            (LocomotionFlags.Move, "Move"),
            (LocomotionFlags.Turn, "Turn"),
            (LocomotionFlags.Teleportation, "Teleportation"),
            (LocomotionFlags.GrabMove, "Grab Move"),
            (LocomotionFlags.Climb, "Climb"),
            (LocomotionFlags.Gravity, "Gravity"),
            (LocomotionFlags.Jump, "Jump"),
        };

        // High-level enable/disable: the PICO BuildingBlock pattern is to flip the entire
        // "Locomotion" GameObject by SetActive. We additionally toggle the root-level
        // CharacterController + CharacterControllerDriver components that were disabled
        // by the initial-hide policy. Idempotent.
        [MenuItem("PICO MCP/Locomotion/Enable")]
        public static bool Enable()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;
            PXR_MCP_Common.SetLocomotionModuleVisible(origin.gameObject, true);
            Debug.Log("[PICO MCP] Locomotion module: ENABLED");
            return true;
        }

        [MenuItem("PICO MCP/Locomotion/Disable")]
        public static bool Disable()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;
            PXR_MCP_Common.SetLocomotionModuleVisible(origin.gameObject, false);
            Debug.Log("[PICO MCP] Locomotion module: DISABLED");
            return true;
        }

        // Fine-grained presets that toggle children under the Locomotion subtree.
        // These imply enabling the module so the children actually take effect.
        [MenuItem("PICO MCP/Locomotion/Configure.../Default (Move+Turn+Teleport)")]
        public static void Menu_Default() => Configure(LocomotionFlags.Default);
        [MenuItem("PICO MCP/Locomotion/Configure.../All")]
        public static void Menu_All() => Configure(LocomotionFlags.All);
        [MenuItem("PICO MCP/Locomotion/Configure.../Disable All Children")]
        public static void Menu_None() => Configure(LocomotionFlags.None);

        public static bool Configure(LocomotionFlags enabled)
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;
            // Fine-grained configure implies the module must be active; otherwise children stay off.
            PXR_MCP_Common.SetLocomotionModuleVisible(origin.gameObject, true);
            var root = origin.transform.Find(PXR_MCP_Common.LocomotionRootName);
            if (root == null) { Debug.LogError("[PICO MCP] Locomotion subtree not found (XRI Starter Assets prefab required)."); return false; }
            foreach (var (flag, child) in Map)
            {
                var t = root.Find(child);
                if (t == null) continue;
                bool want = (enabled & flag) != 0;
                if (t.gameObject.activeSelf != want)
                {
                    Undo.RecordObject(t.gameObject, "PICO MCP toggle " + child);
                    t.gameObject.SetActive(want);
                }
            }
            Debug.Log("[PICO MCP] Locomotion = " + enabled);
            return true;
        }
    }

    // ---------------- Spatial Mesh (depends on VST) ----------------
    public static class PXR_MCP_SpatialMesh
    {
        public const string ContainerName = "[PICO_MCP] Spatial Mesh";
        public const string MeshPrefabName = "MeshPrefab";

        // Locate the PICO MeshPrefab dynamically. Avoids hardcoded version / subdirectory paths
        // that break when the PICO XR SDK is restructured or installed under a different package layout.
        static GameObject LocateMeshPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets(MeshPrefabName + " t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // Prefer prefabs that live inside the PICO XR package / SDK tree.
                if (path.IndexOf("pico", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!path.EndsWith("/" + MeshPrefabName + ".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        [MenuItem("PICO MCP/Spatial Mesh/Ensure")]
        public static bool Ensure()
        {
            if (!PXR_MCP_VST.Ensure()) return false; // dependency
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return false;
            var container = origin.transform.Find(ContainerName);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Spatial Mesh container");
                go.transform.SetParent(origin.transform, false);
                container = go.transform;
            }
            var meshPrefab = LocateMeshPrefab();
            if (meshPrefab == null) { Debug.LogError("[PICO MCP] MeshPrefab not found in any pico-* package or asset folder. Make sure com.bytedance.pico.xr is installed and contains a MeshPrefab.prefab."); return false; }

            var mgr = container.gameObject.GetComponent<PXR_SpatialMeshManager>();
            if (mgr == null)
            {
                mgr = Undo.AddComponent<PXR_SpatialMeshManager>(container.gameObject);
                mgr.meshPrefab = meshPrefab;
                EditorUtility.SetDirty(mgr);
            }
            Debug.Log("[PICO MCP] Spatial Mesh configured.");
            return true;
        }

        [MenuItem("PICO MCP/Spatial Mesh/Remove")]
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return;
            var c = origin.transform.Find(ContainerName);
            if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
            Debug.Log("[PICO MCP] Spatial Mesh removed.");
        }
    }
}
