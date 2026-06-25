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

// PXR_MCP_Common.cs
// Step 1 deliverable: minimal common helpers for the four building blocks.
// Principles:
//   1. Non-destructive: never SetActive(false) or destroy a foreign XR Origin.
//   2. Idempotent: re-running an Ensure() does not duplicate anything.
//   3. No hardcoded XRI version: resolved via PackageInfo.FindForAssembly.
//   4. Agent-owned XR Origin is identified by name (Tag may be added later).
//   5. Initial-create policy: when we instantiate the XRI Starter Assets
//      "XR Origin (XR Rig)" prefab, we IMMEDIATELY hide modules that are
//      not part of any block's *core* dependency. Specifically:
//        * Camera Offset/Left Controller            GameObject SetActive(false)
//        * Camera Offset/Right Controller           GameObject SetActive(false)
//        * Locomotion subtree                       GameObject SetActive(false)
//        * Root XRInputModalityManager component    enabled = false
//        * Root CharacterController component       enabled = false
//        * Root CharacterControllerDriver component enabled = false
//      Only Main Camera is visible by default.
//      Each block re-enables only what it owns:
//        * pico_xr_vst            : nothing (uses Main Camera only)
//        * pico_xr_controller     : Left/Right Controller GO + XRInputModalityManager
//        * pico_xr_locomotion     : Locomotion subtree + CharacterController + CharacterControllerDriver
//      This avoids the "Enable VST also brings in Controllers / Locomotion" surprise.
//      "Initial hide" runs ONLY on first creation; re-finding an existing agent
//      origin does NOT re-hide modules the user already turned on.
using System;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    public static class PXR_MCP_Common
    {
        public const string AgentOriginName = "[PICO_MCP] XR Origin (XR Rig)";

        // Children-of-XR-Origin paths we manage:
        public const string CameraOffsetName      = "Camera Offset";
        public const string MainCameraName        = "Main Camera";
        public const string LeftControllerName    = "Left Controller";
        public const string RightControllerName   = "Right Controller";
        public const string LocomotionRootName    = "Locomotion";

        // Reflection-resolved type names (XRI namespace may shift across versions).
        const string TypeName_XRInputModalityManager  = "UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager";
        const string TypeName_CharacterControllerDriver = "UnityEngine.XR.Interaction.Toolkit.Locomotion.CharacterControllerDriver";
        // Legacy XRI 2.x namespace fallback for CharacterControllerDriver:
        const string TypeName_CharacterControllerDriver_Legacy = "UnityEngine.XR.Interaction.Toolkit.CharacterControllerDriver";

        // -----------------------------------------------------------------
        // Find / create
        // -----------------------------------------------------------------

        // Find the agent-managed XR Origin in the scene; null if not present.
        public static XROrigin FindAgentOrigin()
        {
#if UNITY_2023_1_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<XROrigin>(true);
#endif
            return all.FirstOrDefault(o => o.gameObject.name == AgentOriginName);
        }

        // Ensure an agent-owned XR Origin exists. Reuse if already there; otherwise create from XRI prefab.
        // Never touches foreign XR Origins.
        public static GameObject EnsureXROrigin()
        {
            var existing = FindAgentOrigin();
            if (existing != null)
            {
                ApplyFloorTrackingOrigin(existing.gameObject);
                return existing.gameObject;
            }

            var prefabPath = LocateXriOriginPrefab();
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("[PICO MCP] XRI Starter Assets sample not imported. Import it via Package Manager -> XR Interaction Toolkit -> Samples.");
                return null;
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) { Debug.LogError("[PICO MCP] Prefab missing at " + prefabPath); return null; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(instance, "Create PICO MCP XR Origin");
            instance.name = AgentOriginName;

            ApplyFloorTrackingOrigin(instance);

            // Initial hide: keep only Main Camera + core XR Origin visible/active.
            // Any module a block depends on must be re-enabled by that block.
            InitiallyHideNonCoreModules(instance);

            return instance;
        }

        // -----------------------------------------------------------------
        // Initial-hide policy (only on freshly created agent origin)
        // -----------------------------------------------------------------

        static void InitiallyHideNonCoreModules(GameObject originGo)
        {
            if (originGo == null) return;

            // 1) Hide Camera Offset children that are not Main Camera.
            //    Default visible: Main Camera only.
            //    Hidden:          Left Controller, Right Controller, and any other non-camera child.
            var camOffset = originGo.transform.Find(CameraOffsetName);
            if (camOffset != null)
            {
                foreach (Transform child in camOffset)
                {
                    if (child == null) continue;
                    if (child.name == MainCameraName) continue;
                    SetGameObjectActive(child.gameObject, false, "Hide non-camera child under Camera Offset");
                }
            }

            // 2) Hide Locomotion subtree (PICO default: Locomotion OFF until user opts in).
            SetLocomotionRootActive(originGo, false);

            // 3) Disable root-level scripts that belong to Controller / Locomotion modules.
            SetComponentEnabledByTypeName(originGo, TypeName_XRInputModalityManager, false);
            SetComponentEnabled<CharacterController>(originGo, false);
            SetComponentEnabledByTypeName(originGo, TypeName_CharacterControllerDriver,        false);
            SetComponentEnabledByTypeName(originGo, TypeName_CharacterControllerDriver_Legacy, false);
        }

        // -----------------------------------------------------------------
        // Per-module show / hide (called from each block)
        // -----------------------------------------------------------------

        // Controller module: show Left/Right Controller GameObjects + enable
        // XRInputModalityManager on the XR Origin root.
        public static void SetControllerModuleVisible(GameObject originGo, bool visible)
        {
            if (originGo == null) return;
            var camOffset = originGo.transform.Find(CameraOffsetName);
            if (camOffset != null)
            {
                var left  = camOffset.Find(LeftControllerName);
                var right = camOffset.Find(RightControllerName);
                if (left  != null) SetGameObjectActive(left.gameObject,  visible, "PICO MCP toggle Left Controller");
                if (right != null) SetGameObjectActive(right.gameObject, visible, "PICO MCP toggle Right Controller");
            }
            SetComponentEnabledByTypeName(originGo, TypeName_XRInputModalityManager, visible);
        }

        // Locomotion module: show Locomotion subtree + enable CharacterController +
        // CharacterControllerDriver on the XR Origin root.
        public static void SetLocomotionModuleVisible(GameObject originGo, bool visible)
        {
            if (originGo == null) return;
            SetLocomotionRootActive(originGo, visible);
            SetComponentEnabled<CharacterController>(originGo, visible);
            SetComponentEnabledByTypeName(originGo, TypeName_CharacterControllerDriver,        visible);
            SetComponentEnabledByTypeName(originGo, TypeName_CharacterControllerDriver_Legacy, visible);
        }

        // Toggle the XRI Starter Assets "Locomotion" subtree by SetActive. This is the documented
        // PICO BuildingBlock pattern for enabling/disabling Locomotion. Idempotent.
        public static void SetLocomotionRootActive(GameObject originGo, bool active)
        {
            if (originGo == null) return;
            var root = originGo.transform.Find(LocomotionRootName);
            if (root == null) return;
            SetGameObjectActive(root.gameObject, active, "PICO MCP toggle Locomotion root");
        }

        // -----------------------------------------------------------------
        // Status probes used by pico_xr_status / pico_xr_controller status
        // -----------------------------------------------------------------

        // Controller module considered "active" when either Left or Right Controller GO is active.
        public static bool IsControllerModuleActive(GameObject originGo)
        {
            if (originGo == null) return false;
            var camOffset = originGo.transform.Find(CameraOffsetName);
            if (camOffset == null) return false;
            var left  = camOffset.Find(LeftControllerName);
            var right = camOffset.Find(RightControllerName);
            return (left  != null && left.gameObject.activeSelf)
                || (right != null && right.gameObject.activeSelf);
        }

        // -----------------------------------------------------------------
        // Misc
        // -----------------------------------------------------------------

        // Force tracking origin to Floor on the agent-owned XR Origin. Idempotent: re-applying
        // the same value is a no-op for the XRI runtime.
        public static void ApplyFloorTrackingOrigin(GameObject originGo)
        {
            if (originGo == null) return;
            var origin = originGo.GetComponent<XROrigin>();
            if (origin == null) return;
            if (origin.RequestedTrackingOriginMode != XROrigin.TrackingOriginMode.Floor)
            {
                Undo.RecordObject(origin, "PICO MCP: Set XR Origin Tracking Mode = Floor");
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                EditorUtility.SetDirty(origin);
            }
        }

        // Resolve XRI Starter Assets path WITHOUT hardcoded version.
        public static string LocateXriOriginPrefab()
        {
            // 1) Use the actually installed XRI version.
            try
            {
                var pkg = PackageInfo.FindForAssembly(typeof(XRInteractionManager).Assembly);
                if (pkg != null)
                {
                    foreach (var s in Sample.FindByPackage(pkg.name, pkg.version))
                    {
                        if (string.Equals(s.displayName, "Starter Assets", StringComparison.OrdinalIgnoreCase))
                        {
                            var abs = s.importPath + "/Prefabs/XR Origin (XR Rig).prefab";
                            if (File.Exists(abs)) return ToAssetPath(abs);
                        }
                    }
                }
            }
            catch { /* fall through to asset search */ }

            // 2) Fallback: AssetDatabase search.
            foreach (var guid in AssetDatabase.FindAssets("XR Origin (XR Rig) t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Starter Assets", StringComparison.OrdinalIgnoreCase) >= 0) return path;
            }
            return null;
        }

        // Convert an absolute filesystem path to a Unity project-relative path ("Assets/...").
        // AssetDatabase APIs only accept project-relative paths.
        public static string ToAssetPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return absolute;
            var norm = absolute.Replace("\\", "/");
            var projectRoot = Application.dataPath.Replace("\\", "/");
            // Application.dataPath ends with "/Assets"
            var root = projectRoot.Substring(0, projectRoot.Length - "Assets".Length);
            if (norm.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return norm.Substring(root.Length);
            return norm;
        }

        public static Camera GetMainCamera(GameObject origin)
        {
            if (origin == null) return null;
            var cams = origin.GetComponentsInChildren<Camera>(true);
            return cams.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cams.FirstOrDefault();
        }

        // -----------------------------------------------------------------
        // Low-level helpers
        // -----------------------------------------------------------------

        static void SetGameObjectActive(GameObject go, bool active, string undoLabel)
        {
            if (go == null) return;
            if (go.activeSelf == active) return;
            Undo.RecordObject(go, undoLabel);
            go.SetActive(active);
        }

        static void SetComponentEnabled<T>(GameObject host, bool enabled) where T : Behaviour
        {
            if (host == null) return;
            var c = host.GetComponent<T>();
            if (c == null) return;
            if (c.enabled == enabled) return;
            Undo.RecordObject(c, "PICO MCP toggle " + typeof(T).Name);
            c.enabled = enabled;
            EditorUtility.SetDirty(c);
        }

        // CharacterController is not a Behaviour but has an `enabled` property.
        static void SetComponentEnabled<T>(GameObject host, bool enabled, bool collider = false) where T : Component
        {
            if (host == null) return;
            var c = host.GetComponent<T>();
            if (c == null) return;
            // Use reflection to avoid needing a separate overload for CharacterController/Collider.
            var prop = c.GetType().GetProperty("enabled");
            if (prop == null) return;
            var cur = (bool)prop.GetValue(c);
            if (cur == enabled) return;
            Undo.RecordObject(c, "PICO MCP toggle " + typeof(T).Name);
            prop.SetValue(c, enabled);
            EditorUtility.SetDirty(c);
        }

        // Resolve a Behaviour-derived component by full type name (reflection),
        // because XRI namespaces shift between major versions and we don't want
        // to hard-depend on every variant. Silently no-op when the type or the
        // component is absent.
        static void SetComponentEnabledByTypeName(GameObject host, string fullTypeName, bool enabled)
        {
            if (host == null || string.IsNullOrEmpty(fullTypeName)) return;
            var t = FindTypeInLoadedAssemblies(fullTypeName);
            if (t == null) return; // SDK version doesn't expose this component; OK to ignore.
            var comp = host.GetComponent(t) as Behaviour;
            if (comp == null) return;
            if (comp.enabled == enabled) return;
            Undo.RecordObject(comp, "PICO MCP toggle " + t.Name);
            comp.enabled = enabled;
            EditorUtility.SetDirty(comp);
        }

        static Type FindTypeInLoadedAssemblies(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }
    }
}
