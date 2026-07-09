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
using System.Collections.Generic;
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
                // Re-assert the single-active-camera invariant on every ensure so
                // cameras added after the origin was created are also collapsed.
                EnsureSingleActiveCamera(existing.gameObject);
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

            // Enforce the single-active-camera invariant: the agent origin ships
            // its own Main Camera, so any other enabled scene camera must be
            // switched off (reversibly) to avoid a multi-camera render conflict.
            EnsureSingleActiveCamera(instance);

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

        // Hand module: wire the mounted hand GameObjects into the XR Origin's
        // XRInputModalityManager so XRI natively switches between hands and
        // controllers by tracking state — when a controller becomes tracked the
        // manager deactivates the hand GameObjects (and vice versa). This gives
        // us "auto-hide hands when a controller connects" with no custom runtime
        // script and no domain reload. The manager type + property names are
        // resolved by reflection (R3: XRI namespaces drift across versions).
        public static void WireHandsToModalityManager(GameObject originGo, GameObject leftHand, GameObject rightHand)
        {
            if (originGo == null) return;
            var t = FindTypeInLoadedAssemblies(TypeName_XRInputModalityManager);
            if (t == null) return; // XRI version doesn't expose the manager; hands just stay visible.
            var mgr = originGo.GetComponent(t) as Behaviour;
            if (mgr == null) return;

            Undo.RecordObject(mgr, "PICO MCP wire hands to XRInputModalityManager");
            if (leftHand  != null) SetGameObjectMember(mgr, "leftHand",  leftHand);
            if (rightHand != null) SetGameObjectMember(mgr, "rightHand", rightHand);
            // The manager must be enabled for the auto-switch loop to run.
            if (!mgr.enabled) mgr.enabled = true;
            EditorUtility.SetDirty(mgr);
        }

        // Undo the hand<->manager wiring on Remove(): null the hand references so
        // the manager no longer drives the (now-deleted) hand GameObjects. Put
        // the manager back to sleep unless the Controller module is still active
        // (R4: don't leave a module lit that the user didn't ask for).
        public static void ClearHandsFromModalityManager(GameObject originGo)
        {
            if (originGo == null) return;
            var t = FindTypeInLoadedAssemblies(TypeName_XRInputModalityManager);
            if (t == null) return;
            var mgr = originGo.GetComponent(t) as Behaviour;
            if (mgr == null) return;

            Undo.RecordObject(mgr, "PICO MCP unwire hands from XRInputModalityManager");
            SetGameObjectMember(mgr, "leftHand",  null);
            SetGameObjectMember(mgr, "rightHand", null);
            if (mgr.enabled && !IsControllerModuleActive(originGo)) mgr.enabled = false;
            EditorUtility.SetDirty(mgr);
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
        // Single-active-camera invariant
        // -----------------------------------------------------------------
        // The agent XR Origin ships its own Main Camera. Any *other* enabled
        // Camera in the scene produces a multi-camera render conflict (most
        // visibly it breaks VST passthrough). So whenever we ensure the agent
        // XR Origin we collapse the scene to a single active camera: the
        // agent's own.
        //
        // Rules honoured (see file header):
        //   R2 Non-destructive: we NEVER SetActive(false) or destroy a foreign
        //      camera's GameObject. We only flip Camera.enabled (and the paired
        //      AudioListener.enabled), which stops rendering/listening without
        //      mutating the object graph. Fully reversible.
        //   R5 Undo + SetDirty: every flip is recorded so Ctrl+Z restores it.
        //   R1 Idempotent: a camera we already disabled is skipped and not
        //      double-recorded.
        //
        // The set of cameras WE disabled is recorded in Editor SessionState
        // (keyed by scene path, using GlobalObjectId so the reference survives
        // domain reloads) so RestoreForeignCameras() can re-enable exactly the
        // cameras we touched — never a camera the user disabled themselves.
        // SessionState is Editor-session-scoped; Ctrl+Z remains the primary
        // user-facing restore path across sessions.

        const string DisabledCamerasSessionKeyPrefix = "PICO_MCP.DisabledForeignCameras.";

        static string DisabledCamerasSessionKey()
        {
            var scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            return DisabledCamerasSessionKeyPrefix + (string.IsNullOrEmpty(scenePath) ? "<untitled>" : scenePath);
        }

        // Collapse the scene to a single active camera: the agent XR Origin's own.
        // Returns the number of foreign cameras it disabled on this call (0 when
        // the invariant already held). Safe to call on every EnsureXROrigin().
        public static int EnsureSingleActiveCamera(GameObject agentOrigin)
        {
            if (agentOrigin == null) return 0;

#if UNITY_2023_1_OR_NEWER
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
#endif
            var recorded = new List<string>(LoadDisabledCameraIds());
            int disabledNow = 0;

            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                // Never touch cameras that belong to the agent XR Origin subtree.
                if (cam.transform.IsChildOf(agentOrigin.transform)) continue;
                // Already off — nothing to do (R1). Do not record; we didn't disable it.
                if (!cam.enabled) continue;

                Undo.RecordObject(cam, "PICO MCP: enforce single active camera");
                cam.enabled = false;
                EditorUtility.SetDirty(cam);

                // Silence the paired AudioListener too (a scene with >1 enabled
                // AudioListener spams the console); recorded/restored together.
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null && listener.enabled)
                {
                    Undo.RecordObject(listener, "PICO MCP: disable foreign AudioListener");
                    listener.enabled = false;
                    EditorUtility.SetDirty(listener);
                }

                var id = GlobalObjectId.GetGlobalObjectIdSlow(cam).ToString();
                if (!recorded.Contains(id)) recorded.Add(id);
                disabledNow++;
            }

            if (disabledNow > 0)
            {
                SaveDisabledCameraIds(recorded);
                Debug.Log($"[PICO MCP] Enforced single active camera: disabled {disabledNow} foreign camera(s).");
            }
            return disabledNow;
        }

        // Re-enable exactly the foreign cameras that EnsureSingleActiveCamera
        // previously disabled (and their AudioListeners). Cameras the user
        // disabled on their own are never touched. Returns the count restored.
        public static int RestoreForeignCameras()
        {
            var ids = LoadDisabledCameraIds();
            if (ids.Count == 0) return 0;

            int restored = 0;
            foreach (var idStr in ids)
            {
                if (!GlobalObjectId.TryParse(idStr, out var gid)) continue;
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                var cam = obj as Camera;
                if (cam == null) continue;
                if (!cam.enabled)
                {
                    Undo.RecordObject(cam, "PICO MCP: restore foreign camera");
                    cam.enabled = true;
                    EditorUtility.SetDirty(cam);
                }
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null && !listener.enabled)
                {
                    Undo.RecordObject(listener, "PICO MCP: restore foreign AudioListener");
                    listener.enabled = true;
                    EditorUtility.SetDirty(listener);
                }
                restored++;
            }

            ClearDisabledCameraIds();
            if (restored > 0) Debug.Log($"[PICO MCP] Restored {restored} foreign camera(s).");
            return restored;
        }

        // Count of active-and-enabled cameras currently in the scene. A camera
        // counts only when its GameObject is active in hierarchy AND the Camera
        // component is enabled (i.e. it actually renders).
        public static int CountActiveSceneCameras()
        {
#if UNITY_2023_1_OR_NEWER
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
#endif
            return cameras.Count(c => c != null && c.isActiveAndEnabled);
        }

        // Number of foreign cameras (outside the agent XR Origin) we are
        // currently holding disabled. 0 when none / no record.
        public static int CountManagedDisabledCameras()
        {
            return LoadDisabledCameraIds().Count;
        }

        static List<string> LoadDisabledCameraIds()
        {
            var raw = SessionState.GetString(DisabledCamerasSessionKey(), "");
            if (string.IsNullOrEmpty(raw)) return new List<string>();
            return raw.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        static void SaveDisabledCameraIds(List<string> ids)
        {
            SessionState.SetString(DisabledCamerasSessionKey(), string.Join("\n", ids.Distinct()));
        }

        static void ClearDisabledCameraIds()
        {
            SessionState.EraseString(DisabledCamerasSessionKey());
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

        // Assign a GameObject-typed member (property first, then field) by name on
        // a component instance via reflection. XRInputModalityManager exposes
        // leftHand/rightHand/leftController/rightController as public GameObject
        // members, but whether they are properties or fields — and their exact
        // declaring type — can drift across XRI versions, so we probe both (R3).
        static void SetGameObjectMember(object target, string memberName, GameObject value)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return;
            var type = target.GetType();
            var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(GameObject))
            {
                prop.SetValue(target, value);
                return;
            }
            var field = type.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(GameObject))
            {
                field.SetValue(target, value);
            }
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

        // Public reflection helper (R3: no hard type reference across versions / assemblies).
        // Resolves a type by its full name first, then falls back to a simple (namespace-less)
        // name match across every loaded assembly. Used to locate runtime MonoBehaviours that
        // are copied into the project at enable time and thus cannot be referenced by asmdef
        // (e.g. the global-namespace `SpatialMeshManager` driver). Returns null if absent.
        public static Type FindLoadedType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 1) Exact full-name match.
            var byFull = FindTypeInLoadedAssemblies(name);
            if (byFull != null) return byFull;

            // 2) Simple-name match (the driver ships with no namespace).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (var t in types)
                {
                    if (t != null && t.Name == name) return t;
                }
            }
            return null;
        }
    }
}
