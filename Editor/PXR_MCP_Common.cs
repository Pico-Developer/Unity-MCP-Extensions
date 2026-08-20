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
                // PXR_Manager is the shared PICO system event dispatcher every MR
                // feature (VST / SpatialMesh / Plane / HandTracking) relies on.
                // Ensure it on every EnsureXROrigin so a rig created before this
                // code shipped is upgraded in place. Idempotent.
                EnsurePxrManager(existing.gameObject);
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

            // Attach the shared PICO system manager to the XR Origin ROOT (this is
            // where the PICO Building Blocks flow puts it, and where PXR_Manager
            // expects to sit — above the Main Camera). Without it the SensePack
            // providers never deliver data to VST / SpatialMesh / Plane.
            EnsurePxrManager(instance);

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
            GameObject left = null, right = null;
            if (camOffset != null)
            {
                var l = camOffset.Find(LeftControllerName);
                var r = camOffset.Find(RightControllerName);
                left  = l != null ? l.gameObject : null;
                right = r != null ? r.gameObject : null;
                if (left  != null) SetGameObjectActive(left,  visible, "PICO MCP toggle Left Controller");
                if (right != null) SetGameObjectActive(right, visible, "PICO MCP toggle Right Controller");
            }
            SetComponentEnabledByTypeName(originGo, TypeName_XRInputModalityManager, visible);

            // Defect ① counterpart: keep the XRInputModalityManager's controller
            // members in sync with controller ownership. On enable, (re)bind them to
            // the Left/Right Controller GameObjects so XRI's hand<->controller
            // auto-switch works; on disable, null them so a still-enabled manager
            // (kept alive by the hand module) can never resurface a controller the
            // user did not ask for. Reflection-guarded (R3); no-op if XRI absent.
            var t = FindTypeInLoadedAssemblies(TypeName_XRInputModalityManager);
            if (t != null)
            {
                var mgr = originGo.GetComponent(t) as Behaviour;
                if (mgr != null)
                {
                    Undo.RecordObject(mgr, "PICO MCP sync controller refs on modality manager");
                    SetGameObjectMember(mgr, "leftController",  visible ? left  : null);
                    SetGameObjectMember(mgr, "rightController", visible ? right : null);
                    EditorUtility.SetDirty(mgr);
                }
            }
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

            // Defect ① fix: the XRI Starter Assets rig bakes generic (NON-PICO)
            // controller references into the manager's leftController/rightController
            // members. Once the manager is enabled, it re-activates those generic
            // controller GameObjects whenever a controller device is (or defaults to)
            // tracked — so a HAND-ONLY enable would wrongly surface a non-PICO
            // controller alongside the hands. Whether a controller appears — and that
            // its model is the PICO prefab — is owned SOLELY by pico_xr_controller.
            // So when the Controller module is NOT active, null the controller members
            // so the manager can only drive the hands. pico_xr_controller.enable
            // (SetControllerModuleVisible true) re-binds them.
            if (!IsControllerModuleActive(originGo))
            {
                SetGameObjectMember(mgr, "leftController",  null);
                SetGameObjectMember(mgr, "rightController", null);
            }
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

        // -----------------------------------------------------------------
        // PICO system manager + project capability flags
        // -----------------------------------------------------------------
        // PXR_Manager is the PICO runtime event dispatcher: it polls the OS for
        // sense-data events and fires the static events (SpatialMeshDataUpdated /
        // PlaneDetectionDataUpdated / ...) that the per-feature runtime drivers
        // subscribe to. The PICO Building Blocks flow attaches it to the XR Origin
        // ROOT (cameraOrigin.AddComponent<PXR_Manager>()), and PXR_Manager.Awake()
        // discovers cameras via GetComponentsInChildren<Camera>(), so it must sit
        // ABOVE the Main Camera. We resolve the type by reflection (R3: the PICO
        // SDK assembly / namespace may drift and this asmdef must not hard-bind a
        // version). No-op when the SDK is absent.
        const string TypeName_PXR_Manager     = "ByteDance.PICO.XR.PXR_Manager";
        const string TypeName_PXR_Manager_Alt = "Unity.XR.PXR.PXR_Manager";

        // Ensure a PXR_Manager component exists on the agent XR Origin root.
        // Idempotent (R1), reflection-resolved (R3), Undo-tracked (R5). Returns the
        // component or null when the PICO SDK type is unavailable.
        public static Component EnsurePxrManager(GameObject originGo)
        {
            if (originGo == null) return null;
            var t = FindLoadedType(TypeName_PXR_Manager) ?? FindLoadedType(TypeName_PXR_Manager_Alt);
            if (t == null)
            {
                // PICO SDK not installed / define not set. Silent: features that
                // need it (VST/SpatialMesh/Plane) already surface their own
                // SDK-missing errors, and non-PICO rigs should not be spammed.
                return null;
            }
            var existing = originGo.GetComponent(t);
            if (existing != null) return existing;
            var comp = Undo.AddComponent(originGo, t);
            if (comp != null) EditorUtility.SetDirty(comp);
            Debug.Log("[PICO MCP] PXR_Manager ensured on XR Origin root.");
            return comp;
        }

        // PICO project-level capability flags (videoSeeThrough / spatialMesh /
        // planeDetection / handTracking / ...) live on the PXR_ProjectSetting
        // ScriptableObject, are drawn into the PXR_Manager Inspector by the SDK's
        // custom editor, and are consumed at BUILD time to write the Android
        // manifest system-features + permissions (enable_vst / enable_mesh_anchor /
        // enable_plane_detection / SPATIAL_DATA). Mounting the runtime driver alone
        // is NOT enough — the matching flag must be on or the OS delivers no data.
        // Each block sets ONLY its own flag on enable (so opting into VST does not
        // silently request Spatial Mesh / Plane permissions).
        const string TypeName_PXR_ProjectSetting     = "ByteDance.PICO.XR.PXR_ProjectSetting";
        const string TypeName_PXR_ProjectSetting_Alt = "Unity.XR.PXR.PXR_ProjectSetting";

        // Set a boolean capability flag on PXR_ProjectSetting by field name via
        // reflection (R3). Returns true when the flag was found and set. No-op /
        // false when the PICO SDK type or the field is absent. Persists via the
        // SDK's static SaveAssets().
        public static bool SetProjectCapability(string flagField, bool value)
        {
            if (string.IsNullOrEmpty(flagField)) return false;
            var t = FindLoadedType(TypeName_PXR_ProjectSetting) ?? FindLoadedType(TypeName_PXR_ProjectSetting_Alt);
            if (t == null) return false;
            try
            {
                var getCfg = t.GetMethod("GetProjectConfig", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getCfg == null) return false;
                var cfg = getCfg.Invoke(null, null);
                if (cfg == null) return false;

                var field = cfg.GetType().GetField(flagField);
                if (field == null || field.FieldType != typeof(bool)) return false;
                var cur = (bool)field.GetValue(cfg);
                if (cur != value)
                {
                    field.SetValue(cfg, value);
                    var cfgObj = cfg as UnityEngine.Object;
                    if (cfgObj != null) EditorUtility.SetDirty(cfgObj);
                    var save = t.GetMethod("SaveAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (save != null) save.Invoke(null, null);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PICO MCP] Could not set PXR_ProjectSetting." + flagField + " = " + value + ": " + e.Message);
                return false;
            }
        }

        // -----------------------------------------------------------------
        // PICO Stereo Rendering Mode (MR sense-data requires MultiPass)
        // -----------------------------------------------------------------
        // The PICO Stereo Rendering Mode lives on the PXR_Settings ScriptableObject
        // (namespace ByteDance.PICO.XR), stored via Unity's generic XR Management
        // under the config-object key "ByteDance.PICO.XR.Settings" and surfaced in
        // Project Settings > XR Plug-in Management > PICO > Stereo Rendering Mode.
        // The MR sense-data features (Spatial Mesh / Plane Detection) require
        // MultiPass: Multiview (single-pass-instanced) mis-composites passthrough +
        // the sense-data mesh on device. So enabling either forces the mode to
        // MultiPass. Reflection-resolved (R3: no hard PICO SDK version bind); the
        // "MultiPass" enum member is matched by name (no numeric hardcode). No-op /
        // false when the PICO SDK is absent or the field/member cannot be resolved.
        const string TypeName_PXR_Settings     = "ByteDance.PICO.XR.PXR_Settings";
        const string TypeName_PXR_Settings_Alt = "Unity.XR.PXR.PXR_Settings";
        const string PXR_SettingsConfigKey     = "ByteDance.PICO.XR.Settings";

        public static bool SetPicoStereoRenderingMultiPass()
        {
            var t = FindLoadedType(TypeName_PXR_Settings) ?? FindLoadedType(TypeName_PXR_Settings_Alt);
            if (t == null) return false;
            try
            {
                // Obtain the settings instance: prefer the SDK's own static
                // accessor (GetSettings), else fall back to the generic XR
                // Management config-object store keyed by PXR_SettingsConfigKey.
                object settings = null;
                var getSettings = t.GetMethod("GetSettings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getSettings != null) settings = getSettings.Invoke(null, null);
                if (settings == null)
                {
                    if (EditorBuildSettings.TryGetConfigObject(PXR_SettingsConfigKey, out UnityEngine.Object cfg)) settings = cfg;
                }
                if (settings == null) return false;

                var field = settings.GetType().GetField("stereoRenderingModeAndroid");
                if (field == null || !field.FieldType.IsEnum) return false;

                object multiPass = null;
                foreach (var name in Enum.GetNames(field.FieldType))
                {
                    if (string.Equals(name, "MultiPass", StringComparison.OrdinalIgnoreCase))
                    {
                        multiPass = Enum.Parse(field.FieldType, name);
                        break;
                    }
                }
                if (multiPass == null) return false;

                var cur = field.GetValue(settings);
                if (!Equals(cur, multiPass))
                {
                    field.SetValue(settings, multiPass);
                    var so = settings as UnityEngine.Object;
                    if (so != null) EditorUtility.SetDirty(so);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[PICO MCP] PICO Stereo Rendering Mode set to MultiPass.");
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PICO MCP] Could not set PICO Stereo Rendering Mode = MultiPass: " + e.Message);
                return false;
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
