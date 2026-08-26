// PXR_MCP_Features.cs
// Step 1 deliverable: four building blocks (VST / Controller / Locomotion / SpatialMesh)
#if PICO_MCP_SHOW_MENU
// as plain Editor functions + MenuItems for manual validation.
#endif
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
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    // ---------------- Single-active-camera invariant ----------------
    // Not a user-facing block: the XR Origin ships its own Main Camera, so the
    // "only one active camera in the scene" rule is a property maintained by
#if PICO_MCP_SHOW_MENU
    // EnsureXROrigin(). These MenuItems exist only for manual smoke testing in
    // an Editor without the MCP bridge (mirrors every other block's pattern).
#endif
    public static class PXR_MCP_Camera
    {
#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Camera/Enforce Single Active Camera")]
#endif
        public static void Menu_Enforce()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) { Debug.LogError("[PICO MCP] No agent XR Origin."); return; }
            var n = PXR_MCP_Common.EnsureSingleActiveCamera(origin);
            Debug.Log($"[PICO MCP] Enforce single active camera: {n} foreign camera(s) disabled.");
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Camera/Restore Foreign Cameras")]
#endif
        public static void Menu_Restore()
        {
            var n = PXR_MCP_Common.RestoreForeignCameras();
            Debug.Log($"[PICO MCP] Restored {n} foreign camera(s).");
        }
    }

    // ---------------- VST ----------------
    public static class PXR_MCP_VST
    {
        public const string MarkerChild = "[PICO_MCP] VST Marker";

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/VST/Ensure")]
#endif
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
#if ENABLE_PICO_XR_SDK || ENABLE_PICO_OPENXR_SDK
            // PXR_CameraEffectBlock compiles on both the PICO-native and PICO OpenXR
            // paths (its own guard is `ENABLE_PICO_XR_SDK || ENABLE_PICO_OPENXR_SDK`)
            // and the SDK VST building block adds it on both. Mount it whenever
            // either PICO SDK define is set.
            if (cam.gameObject.GetComponent<PXR_CameraEffectBlock>() == null)
                Undo.AddComponent<PXR_CameraEffectBlock>(cam.gameObject);
#endif
#if ENABLE_PICO_OPENXR_SDK
            // OpenXR runtime path: passthrough is gated by the PICO PassthroughFeature
            // OpenXR feature, which must be enabled on the Android build target (the
            // native path does not need it). Mirrors the SDK VST building block's
            // `EnableOpenXRFeature<PassthroughFeature>()`. Reflection-resolved (R3);
            // a no-op if the PICO OpenXR SDK is not installed.
            PXR_MCP_Common.EnableOpenXRFeature(PXR_MCP_Common.OpenXRFeature_Passthrough);
#endif
            // Turn on the project-level Video See-Through flag so PXR_BuildProcessor
            // emits enable_vst in the Android manifest (the shared PXR_Manager
            // component itself is ensured once in EnsureXROrigin).
            PXR_MCP_Common.SetProjectCapability("videoSeeThrough", true);
            var m = new GameObject(MarkerChild);
            Undo.RegisterCreatedObjectUndo(m, "PICO MCP VST marker");
            m.transform.SetParent(origin.transform, false);
            m.SetActive(false);
            Debug.Log("[PICO MCP] VST enabled.");
            return true;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/VST/Remove")]
#endif
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { Debug.Log("[PICO MCP] No agent XR Origin."); return; }
            var m = origin.transform.Find(MarkerChild);
            if (m != null) Undo.DestroyObjectImmediate(m.gameObject);
            // Clear only this block's capability flag; never touch the shared PXR_Manager (R2).
            PXR_MCP_Common.SetProjectCapability("videoSeeThrough", false);
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

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Controller/Ensure")]
#endif
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

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Controller/Remove")]
#endif
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
#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Locomotion/Enable")]
#endif
        public static bool Enable()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;
            PXR_MCP_Common.SetLocomotionModuleVisible(origin.gameObject, true);
            Debug.Log("[PICO MCP] Locomotion module: ENABLED");
            return true;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Locomotion/Disable")]
#endif
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
#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Locomotion/Configure.../Default (Move+Turn+Teleport)")]
#endif
        public static void Menu_Default() => Configure(LocomotionFlags.Default);
#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Locomotion/Configure.../All")]
#endif
        public static void Menu_All() => Configure(LocomotionFlags.All);
#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Locomotion/Configure.../Disable All Children")]
#endif
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
    // Spatial Mesh is driven by the runtime `SpatialMeshManager` MonoBehaviour that is
    // BUNDLED with this MCP package under Editor/SpatialMeshAssets~ (a folder Unity
    // ignores because of the trailing '~', so the runtime script is never compiled into
    // this Editor-only asmdef). On enable we copy the bundled assets (driver .cs +
    // shaders + materials + prefab) into the user's project Assets/ tree, repair the
    // GUID cross-references that break because the .meta files are intentionally not
    // shipped, then mount + configure the driver via reflection.
    //
    // The old PICO-SDK `PXR_SpatialMeshManager` component is fully DEPRECATED here and
    // is no longer referenced or mounted.
    public static class PXR_MCP_SpatialMesh
    {
        public const string ContainerName = "[PICO_MCP] Spatial Mesh";

        // Destination for the bundled assets inside the user's project.
        public const string ProjectAssetsDir = "Assets/PICO_MCP/SpatialMesh";

        // Global-namespace MonoBehaviour type resolved by reflection after import + recompile.
        public const string DriverTypeName = "SpatialMeshManager";

        // Bundled folder name inside the package (Unity-ignored via trailing '~').
        const string BundledFolder = "Editor/SpatialMeshAssets~";

        // File names + the shader "names" their materials must bind to post-import.
        const string DriverScriptFile = "SpatialMeshManager.cs";
        const string MeshPrefabFile   = "MeshTriangleFadeOutPrefab.prefab";
        const string WireShaderFile   = "TriangleFadeOutFromCenter.shader";
        const string WireMatFile      = "TriangleFadeOutFromCenter.mat";
        const string MaskShaderFile   = "MR_Unlit.shader";
        const string MaskMatFile      = "TA_MR_Unlit.mat";
        const string WireShaderName   = "Custom/TriangleFadeOutFromCenter";
        const string MaskShaderName   = "TA/MR_Unlit";

        static readonly string[] BundledFiles = {
            DriverScriptFile, MeshPrefabFile,
            WireShaderFile, WireMatFile, MaskShaderFile, MaskMatFile,
        };

        public enum EnsureOutcome { Configured, ImportingRecompile, Error }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Spatial Mesh/Ensure")]
#endif
        public static void Menu_Ensure() { Ensure(out _); }

        // Two-phase enable:
        //   Phase 1 - if the SpatialMeshManager type is not yet compiled into the domain,
        //             copy the bundled assets into the project, refresh, and report
        //             ImportingRecompile. The caller must settle-loop (poll pico_xr_status)
        //             then call enable again.
        //   Phase 2 - once the type is loaded, ensure the container, repair asset links,
        //             mount the driver and configure its serialized fields.
        public static EnsureOutcome Ensure(out string detail)
        {
            detail = null;
            if (!PXR_MCP_VST.Ensure()) { detail = "VST dependency could not be ensured; see Unity Console."; return EnsureOutcome.Error; } // dependency

            // MR sense-data (Spatial Mesh) requires MultiPass stereo rendering
            // (single-pass/Multiview mis-composites passthrough + the sense-data
            // mesh on device). The stereo mode lives in a DIFFERENT place per
            // runtime, so these are two independent branches that must not touch
            // each other's setting:
            //   * OpenXR runtime : OpenXRSettings.renderMode        (Render Mode)
            //   * PICO   runtime : PXR_Settings.stereoRenderingMode (Stereo Rendering Mode)
#if ENABLE_PICO_OPENXR_SDK
            // OpenXR runtime path: force OpenXR Render Mode = MultiPass, then enable
            // the PICO PICOSpatialMesh OpenXR feature on the Android build target
            // (passthrough is already enabled by VST.Ensure above). Mirrors the SDK
            // Spatial Mesh building block's
            // `EnableOpenXRFeature<PassthroughFeature>() + EnableOpenXRFeature<PICOSpatialMesh>()`.
            // Reflection-resolved (R3); a no-op if the PICO OpenXR SDK is absent.
            PXR_MCP_Common.SetOpenXRRenderModeMultiPass();
            PXR_MCP_Common.EnableOpenXRFeature(PXR_MCP_Common.OpenXRFeature_SpatialMesh);
#else
            // PICO-native runtime path: force PICO Stereo Rendering = MultiPass.
            PXR_MCP_Common.SetPicoStereoRenderingMultiPass();
#endif

            // Idempotent copy of the bundled assets into the project.
            if (!ImportBundledAssets(out var importDetail)) { detail = importDetail; return EnsureOutcome.Error; }

            var driverType = PXR_MCP_Common.FindLoadedType(DriverTypeName);
            if (driverType == null)
            {
                // Assets just landed (or the PICO SDK scripting define is not yet applied,
                // in which case the guarded driver compiles to nothing). Either way the
                // Editor is (re)compiling and the type is not in this domain yet.
                detail = "SpatialMeshManager assets imported to " + ProjectAssetsDir +
                         ". The Editor is (re)compiling; poll pico_xr_status until the MCP bridge returns, " +
                         "then call pico_xr_spatial_mesh(action=enable) again to mount and configure the driver. " +
                         "If the type never appears, verify a PICO XR SDK is installed (the driver is guarded by ENABLE_PICO_XR_SDK || ENABLE_PICO_OPENXR_SDK).";
                return EnsureOutcome.ImportingRecompile;
            }

            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { detail = "no agent XR Origin in scene"; return EnsureOutcome.Error; }

            var container = origin.transform.Find(ContainerName);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Spatial Mesh container");
                go.transform.SetParent(origin.transform, false);
                container = go.transform;
            }

            // Idempotent: driver already mounted -> nothing more to do.
            if (container.GetComponent(driverType) != null)
            {
                detail = "SpatialMeshManager already mounted and configured.";
                return EnsureOutcome.Configured;
            }

            // Repair GUID cross-references (materials -> shaders, prefab -> material) that
            // break on a meta-less import, then load the concrete assets.
            RepairAssetLinks();
            var meshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectAssetsDir + "/" + MeshPrefabFile);
            var wireMat    = AssetDatabase.LoadAssetAtPath<Material>(ProjectAssetsDir + "/" + WireMatFile);
            var maskMat    = AssetDatabase.LoadAssetAtPath<Material>(ProjectAssetsDir + "/" + MaskMatFile);

            var mgr = Undo.AddComponent(container.gameObject, driverType);
            if (mgr == null) { detail = "failed to add SpatialMeshManager component"; return EnsureOutcome.Error; }

            // Configure serialized fields per the skill's Inspector-config contract.
            // NOTE (per project decision): meshCalcPrefab (ConvexHull) / transparentMaterial
            // (Transparent) / convexHull are intentionally LEFT UNASSIGNED -- no fallback.
            var so = new SerializedObject(mgr);
            SetInt(so, "maxRenderPerFrame", 200);
            SetInt(so, "meshAmount", 300);
            SetObj(so, "m_mask", maskMat);
            SetObj(so, "meshContainer", container);   // Transform
            SetObj(so, "meshPrefab", meshPrefab);
            SetObj(so, "wireframeMaterial", wireMat);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mgr);

            // Turn on the project-level Spatial Mesh flag so PXR_BuildProcessor emits
            // enable_mesh_anchor + the SPATIAL_DATA permission in the Android manifest.
            PXR_MCP_Common.SetProjectCapability("spatialMesh", true);

            Debug.Log("[PICO MCP] Spatial Mesh: SpatialMeshManager mounted and configured.");
            detail = "SpatialMeshManager mounted and configured.";
            return EnsureOutcome.Configured;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Spatial Mesh/Remove")]
#endif
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return;
            var c = origin.transform.Find(ContainerName);
            if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
            // Clear only this block's capability flag; never touch the shared PXR_Manager (R2).
            PXR_MCP_Common.SetProjectCapability("spatialMesh", false);
            // Imported assets under ProjectAssetsDir are left in place (non-destructive:
            // they may be shared / re-enabled later).
            Debug.Log("[PICO MCP] Spatial Mesh removed.");
        }

        // -----------------------------------------------------------------
        // Asset import + repair helpers
        // -----------------------------------------------------------------

        // Import ONLY the visual assets (shaders + materials + wireframe prefab)
        // WITHOUT the SpatialMeshManager driver .cs, repair their GUID links, and
        // return the wireframe mesh prefab (MeshTriangleFadeOutPrefab). This lets
        // other MR blocks (e.g. Plane Detection) reuse the SAME wireframe material
        // as Spatial Mesh — the single asset source is this MCP package — without
        // pulling in the driver script (which would trigger a compile / two-phase
        // enable). Idempotent (R1); returns null when the bundled assets are
        // unavailable (`detail` carries the reason).
        public static GameObject EnsureWireframeVisualAssets(out string detail)
        {
            if (!ImportBundledAssets(out detail, includeDriver: false)) return null;
            RepairAssetLinks();
            return AssetDatabase.LoadAssetAtPath<GameObject>(ProjectAssetsDir + "/" + MeshPrefabFile);
        }

        // Shared destination dir for callers that reuse these bundled assets
        // (e.g. Plane Detection imports its own driver into the same folder).
        public static string ProjectDir => ProjectAssetsDir;

        // Load the wireframe fade material (Custom/TriangleFadeOutFromCenter) so
        // sibling MR blocks (Plane) can configure their driver's wireframeMaterial
        // field with the SAME material Spatial Mesh uses. Call after
        // EnsureWireframeVisualAssets so the asset is present and its shader link
        // repaired. Returns null if not yet imported.
        public static Material LoadWireframeMaterial()
            => AssetDatabase.LoadAssetAtPath<Material>(ProjectAssetsDir + "/" + WireMatFile);

        // Copy a SINGLE bundled file (by name) from the package's
        // Editor/SpatialMeshAssets~ folder into ProjectAssetsDir. Used by the
        // Plane block to import its custom PlaneDetectionManager.cs driver from
        // the shared bundled folder (which also holds the Spatial Mesh driver +
        // visual assets). Idempotent (R1): skips if already present. Triggers a
        // synchronous asset refresh only when a new file is actually copied
        // (a .cs copy will start a recompile -> two-phase enable for the caller).
        public static bool ImportBundledFile(string fileName, out string detail)
        {
            detail = null;
            var src = LocateBundledAssetsDir();
            if (string.IsNullOrEmpty(src))
            {
                detail = "Bundled assets not found in the MCP package (" + BundledFolder + ").";
                return false;
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
            var absDest = projectRoot + "/" + ProjectAssetsDir;
            Directory.CreateDirectory(absDest);

            var s = (src + "/" + fileName).Replace("\\", "/");
            var d = (absDest + "/" + fileName).Replace("\\", "/");
            if (!File.Exists(s)) { detail = "Missing bundled asset: " + fileName; return false; }
            if (!File.Exists(d))
            {
                File.Copy(s, d);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            return true;
        }

        static bool ImportBundledAssets(out string detail, bool includeDriver = true)
        {
            detail = null;
            var src = LocateBundledAssetsDir();
            if (string.IsNullOrEmpty(src))
            {
                detail = "Bundled Spatial Mesh assets not found in the MCP package (" + BundledFolder + ").";
                return false;
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
            var absDest = projectRoot + "/" + ProjectAssetsDir;
            Directory.CreateDirectory(absDest);

            // Visual-only callers (e.g. Plane Detection reusing the wireframe) skip
            // the driver .cs so no C# recompile / domain reload is triggered.
            var files = includeDriver ? BundledFiles : BundledFiles.Where(f => f != DriverScriptFile).ToArray();

            bool copiedAny = false;
            foreach (var f in files)
            {
                var s = (src + "/" + f).Replace("\\", "/");
                var d = (absDest + "/" + f).Replace("\\", "/");
                if (!File.Exists(s)) { detail = "Missing bundled asset: " + f; return false; }
                if (!File.Exists(d)) { File.Copy(s, d); copiedAny = true; }
            }
            if (copiedAny) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        // Resolve the on-disk directory of the bundled assets. Works whether the MCP
        // package is installed as a UPM package or embedded under Assets/ during dev.
        static string LocateBundledAssetsDir()
        {
            // 1) Installed as a UPM package: resolve via the assembly's package.
            try
            {
                var pkg = PackageInfo.FindForAssembly(typeof(PXR_MCP_SpatialMesh).Assembly);
                if (pkg != null)
                {
                    var p = pkg.resolvedPath.Replace("\\", "/") + "/" + BundledFolder;
                    if (Directory.Exists(p)) return p;
                }
            }
            catch { /* fall through */ }

            // 2) Embedded under Assets/: find this script, take its Editor/ dir sibling.
            foreach (var guid in AssetDatabase.FindAssets("PXR_MCP_Features t:MonoScript"))
            {
                var rel = AssetDatabase.GUIDToAssetPath(guid);
                if (!rel.EndsWith("/PXR_MCP_Features.cs", StringComparison.OrdinalIgnoreCase)) continue;
                var editorDir = rel.Substring(0, rel.Length - "/PXR_MCP_Features.cs".Length); // ".../Editor"
                var candidateRel = editorDir + "/SpatialMeshAssets~";
                if (candidateRel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    var abs = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/") + "/" + candidateRel;
                    if (Directory.Exists(abs)) return abs;
                }
            }
            return null;
        }

        // Materials lose their shader binding and the prefab loses its material binding
        // when imported without .meta (GUIDs are regenerated). Re-wire them by name.
        static void RepairAssetLinks()
        {
            var wireMat = AssetDatabase.LoadAssetAtPath<Material>(ProjectAssetsDir + "/" + WireMatFile);
            var maskMat = AssetDatabase.LoadAssetAtPath<Material>(ProjectAssetsDir + "/" + MaskMatFile);
            var wireShader = Shader.Find(WireShaderName);
            var maskShader = Shader.Find(MaskShaderName);

            if (wireMat != null && wireShader != null && wireMat.shader != wireShader)
            {
                wireMat.shader = wireShader; EditorUtility.SetDirty(wireMat);
            }
            if (maskMat != null && maskShader != null && maskMat.shader != maskShader)
            {
                maskMat.shader = maskShader; EditorUtility.SetDirty(maskMat);
            }

            var meshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectAssetsDir + "/" + MeshPrefabFile);
            if (meshPrefab != null && wireMat != null)
            {
                var mr = meshPrefab.GetComponentInChildren<MeshRenderer>(true);
                if (mr != null && mr.sharedMaterial != wireMat)
                {
                    mr.sharedMaterial = wireMat;
                    EditorUtility.SetDirty(meshPrefab);
                }
            }
            AssetDatabase.SaveAssets();
        }

        static void SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = v;
        }

        static void SetObj(SerializedObject so, string prop, UnityEngine.Object v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = v;
        }
    }

    // ---------------- Plane Detection (depends on VST) ----------------
    // Plane Detection is the SensePack sibling of Spatial Mesh: it starts the
    // PlaneDetection sense-data provider and renders the detected planes at
    // runtime. The only difference from Spatial Mesh is the DATA SOURCE
    // (PXR_Manager.PlaneDetectionDataUpdated vs SpatialMeshDataUpdated).
    //
    // To make the plane visual TRULY match Spatial Mesh, Plane Detection uses a
    // CUSTOM driver `PlaneDetectionManager` (global namespace) that reuses
    // SpatialMeshManager's pool + fade-shader (_TargetPosition / _StartTime)
    // render pipeline VERBATIM — unlike the SDK's PXR_PlaneDetectionManager,
    // which feeds neither shader global and overwrites the material color per
    // semantic label (clobbering the wireframe fade material). That custom
    // driver is bundled alongside SpatialMeshManager under
    // Editor/SpatialMeshAssets~ (Unity-ignored via the trailing '~').
    //
    // Because the driver .cs must be copied into the project and compiled before
    // it can be reflection-mounted, Plane Detection is a TWO-PHASE enable, EXACTLY
    // like Spatial Mesh:
    //   Phase 1 - copy the PlaneDetectionManager.cs driver + shared visual assets
    //             into the project, refresh, and report ImportingRecompile. The
    //             caller must settle-loop (poll pico_xr_status) then enable again.
    //   Phase 2 - once PlaneDetectionManager is compiled into the domain, ensure
    //             the container, mount the driver and configure its serialized
    //             fields with the SAME wireframe prefab + material Spatial Mesh
    //             uses.
    public static class PXR_MCP_Plane
    {
        public const string ContainerName = "[PICO_MCP] Plane";

        // Custom global-namespace driver bundled in this MCP package, resolved by
        // reflection after import + recompile (mirrors SpatialMeshManager).
        public const string DriverTypeName = "PlaneDetectionManager";

        // Bundled driver .cs (lives in the SAME folder as the Spatial Mesh assets).
        const string DriverScriptFile = "PlaneDetectionManager.cs";

        // Shared wireframe mesh prefab imported by the Spatial Mesh block.
        const string MeshPrefabFile = "MeshTriangleFadeOutPrefab.prefab";

        public enum EnsureOutcome { Configured, ImportingRecompile, Error }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Plane/Ensure")]
#endif
        public static void Menu_Ensure() { Ensure(out _); }

        // Two-phase enable (mirrors PXR_MCP_SpatialMesh):
        //   Phase 1 - import the custom PlaneDetectionManager.cs driver + the shared
        //             wireframe visual assets, refresh, report ImportingRecompile.
        //   Phase 2 - once PlaneDetectionManager is loaded, ensure the container,
        //             mount the driver and configure its serialized fields.
        public static EnsureOutcome Ensure(out string detail)
        {
            detail = null;
#if ENABLE_PICO_OPENXR_SDK
            // PLANE DETECTION IS PICO-NATIVE ONLY. PICO ships NO plane-detection
            // OpenXR feature (unlike Spatial Mesh, which has PICOSpatialMesh): the
            // plane sense-data provider is created ONLY inside the native PXR_Loader
            // (UPxr_CreatePlaneDetectionSenseDataProvider), so under the OpenXR
            // runtime the provider never exists, PXR_Manager.PlaneDetectionDataUpdated
            // never fires and QueryPlaneAnchorAsync has nothing to query. Refuse here
            // rather than mounting a driver that would silently never receive data.
            detail = "Plane detection is not supported on the PICO OpenXR runtime — PICO ships no " +
                     "plane-detection OpenXR feature (the sense-data provider is created only by the " +
                     "PICO-native runtime). Switch Project Settings > XR Plug-in Management (Android) to " +
                     "the PICO (native) loader, re-open the scene, then enable plane detection again.";
            return EnsureOutcome.Error;
#else
            if (!PXR_MCP_VST.Ensure()) { detail = "VST dependency could not be ensured; see Unity Console."; return EnsureOutcome.Error; } // dependency

            // MR sense-data (Plane Detection) requires MultiPass stereo rendering
            // (single-pass/Multiview mis-composites passthrough + the sense-data
            // mesh on device). Plane detection runs on the PICO-native runtime only,
            // so the stereo mode lives in PXR_Settings.stereoRenderingMode.
            PXR_MCP_Common.SetPicoStereoRenderingMultiPass();

            // Import the shared wireframe visual assets (shaders + materials + mesh
            // prefab, no driver) and repair their GUID links, so the plane driver can
            // reuse the SAME material as Spatial Mesh.
            var meshPrefab = PXR_MCP_SpatialMesh.EnsureWireframeVisualAssets(out var visualDetail);
            if (meshPrefab == null) { detail = visualDetail; return EnsureOutcome.Error; }

            // Import the custom PlaneDetectionManager.cs driver from the same bundled
            // folder (this copies the .cs and triggers a recompile on first import).
            if (!PXR_MCP_SpatialMesh.ImportBundledFile(DriverScriptFile, out var importDetail)) { detail = importDetail; return EnsureOutcome.Error; }

            var driverType = PXR_MCP_Common.FindLoadedType(DriverTypeName);
            if (driverType == null)
            {
                // Driver just landed (or the PICO SDK scripting define is not yet
                // applied, in which case the guarded driver compiles to nothing).
                // Either way the Editor is (re)compiling and the type is not in this
                // domain yet.
                detail = "PlaneDetectionManager driver imported to " + PXR_MCP_SpatialMesh.ProjectDir +
                         ". The Editor is (re)compiling; poll pico_xr_status until the MCP bridge returns, " +
                         "then call pico_xr_plane(action=enable) again to mount and configure the driver. " +
                         "If the type never appears, verify a PICO XR SDK is installed (the driver is guarded by ENABLE_PICO_XR_SDK).";
                return EnsureOutcome.ImportingRecompile;
            }

            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { detail = "no agent XR Origin in scene"; return EnsureOutcome.Error; }

            var container = origin.transform.Find(ContainerName);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Plane container");
                go.transform.SetParent(origin.transform, false);
                container = go.transform;
            }

            // Idempotent: driver already mounted -> nothing more to do (R1).
            if (container.GetComponent(driverType) != null)
            {
                detail = "PlaneDetectionManager already mounted and configured.";
                return EnsureOutcome.Configured;
            }

            var wireMat = PXR_MCP_SpatialMesh.LoadWireframeMaterial();

            var mgr = Undo.AddComponent(container.gameObject, driverType);
            if (mgr == null) { detail = "failed to add PlaneDetectionManager component"; return EnsureOutcome.Error; }

            // Configure serialized fields, matching Spatial Mesh's Inspector config.
            // The plane driver bakes vertices to world space and parents its pooled
            // instances under meshContainer, so the container itself must sit at the
            // origin (it does: it is a fresh child of the XR Origin).
            var so = new SerializedObject(mgr);
            SetInt(so, "maxRenderPerFrame", 200);
            SetInt(so, "meshAmount", 300);
            SetObj(so, "meshContainer", container);   // Transform
            SetObj(so, "meshPrefab", meshPrefab);
            SetObj(so, "wireframeMaterial", wireMat);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mgr);

            // Turn on the project-level Plane Detection flag so PXR_BuildProcessor
            // emits enable_plane_detection + the SPATIAL_DATA permission in the manifest.
            PXR_MCP_Common.SetProjectCapability("planeDetection", true);

            Debug.Log("[PICO MCP] Plane: PlaneDetectionManager mounted and configured.");
            detail = "PlaneDetectionManager mounted and configured.";
            return EnsureOutcome.Configured;
#endif
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Plane/Remove")]
#endif
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return;
            var c = origin.transform.Find(ContainerName);
            if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
            // Clear only this block's capability flag; never touch the shared PXR_Manager (R2).
            PXR_MCP_Common.SetProjectCapability("planeDetection", false);
            // Imported assets are left in place (non-destructive: shared with Spatial
            // Mesh / may be re-enabled later).
            Debug.Log("[PICO MCP] Plane removed.");
        }

        static void SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = v;
        }

        static void SetObj(SerializedObject so, string prop, UnityEngine.Object v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = v;
        }
    }

    // ---------------- Hand (PICO hand tracking / virtual hands) ----------------
    // Mirrors the PICO SDK BuildingBlock "PICO Hand Tracking": instantiate the
    // HandLeft / HandRight prefabs (each carrying a PXR_Hand component) under
    // "Camera Offset" and turn on PXR_ProjectSetting.handTracking.
    //
    // Unlike Controller / Locomotion, the hand GameObjects are NOT pre-existing
    // children of the XRI Starter Assets rig, so there is nothing for
    // InitiallyHideNonCoreModules() to hide: the hands only exist AFTER enable,
    // and Remove() deletes them outright. Idempotency + status therefore follow
    // the SpatialMesh marker pattern. Enable additionally wires the mounted
    // hands into the XR Origin's XRInputModalityManager (see
    // PXR_MCP_Common.WireHandsToModalityManager) so XRI auto-hides them whenever
    // a controller becomes tracked.
    public static class PXR_MCP_Hand
    {
        // Dynamic-search prefab names (no hardcoded package version path; R3).
        // PICO-NATIVE hand models (root component = PXR_Hand, compiled ONLY under
        // ENABLE_PICO_XR_SDK). Used on the native path.
        public const string HandLeftPrefabName  = "HandLeft";
        public const string HandRightPrefabName = "HandRight";

        // OpenXR hand models. The PICO HandLeft/HandRight prefabs above are rooted
        // on the native PXR_Hand driver, which compiles ONLY under ENABLE_PICO_XR_SDK;
        // mounting them under the (mutually-exclusive) ENABLE_PICO_OPENXR_SDK path
        // leaves a missing-script on the prefab root. Mirror the PICO SDK's own
        // OpenXR hand building block (GenerateXRHands): mount the Unity XR Hands
        // "HandVisualizer" prefabs instead, driven by XRHandSkeletonDriver which is
        // always compiled with com.unity.xr.hands on the OpenXR path.
        const string XrHandsPackageName    = "com.unity.xr.hands";
        const string HandVisualizerSample  = "HandVisualizer";
        const string XrHandLeftPrefabName  = "Left Hand Tracking";
        const string XrHandRightPrefabName = "Right Hand Tracking";
        // Agent-owned instance names double as idempotency markers (R1).
        public const string MarkerLeft  = "[PICO_MCP] Hand Left";
        public const string MarkerRight = "[PICO_MCP] Hand Right";

        // Defect ② — Hand Interactor markers. The PICO hand PREFABS above are only
        // visual + PXR_Hand tracking; they carry NO XRI interactor, so a pinch can
        // never drive pico_xr_grab. XRI needs a GameObject with an Interactor
        // component + a select InputActionReference (pinch) + an Attach Transform.
        // We source that rig from the XRI "Hands Interaction Demo" sample (the same
        // sample the PICO SDK's own "XRI Hand Interaction" building block relies on)
        // and mount clones of its left/right interactor subtrees under Camera Offset.
        public const string MarkerInteractorLeft  = "[PICO_MCP] Hand Interactor L";
        public const string MarkerInteractorRight = "[PICO_MCP] Hand Interactor R";

        // XRI package + sample that ships the ready-wired hand interactor rig.
        const string XriPackageName            = "com.unity.xr.interaction.toolkit";
        const string HandsInteractionDemoSample = "Hands Interaction Demo";

        // Hand-interactor group child name inside the XRI "XR Origin Hands (XR Rig)"
        // template (under its Camera Offset). Kept as constants so the extraction
        // pipeline does not silently drift if the XRI sample ever renames them.
        const string SampleHandGroupLeft  = "Left Hand";
        const string SampleHandGroupRight = "Right Hand";
        // Sibling Transform (child of the hand group) that carries a TrackedPoseDriver
        // bound to the OpenXR "aim" pose. Everything ray-related on the NearFar
        // interactor must originate from here so the ray follows the hand instead
        // of the Camera Offset root.
        const string AimPoseChildName = "Aim Pose";

        // Interactor component types, reflection-resolved (R3). 3.x NearFarInteractor
        // first, then the 2.x ray/direct fallbacks — any of these on a sample subtree
        // marks it as a usable interactor rig.
        static readonly string[] InteractorTypeNames =
        {
            "UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor",
            "UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor",
            "UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor",
            "UnityEngine.XR.Interaction.Toolkit.XRRayInteractor",       // 2.x legacy
            "UnityEngine.XR.Interaction.Toolkit.XRDirectInteractor",    // 2.x legacy
        };

        // Root marker of the XRI "XR Origin Hands (XR Rig)" prefab we source the
        // hand-interactor subtrees from. Reflection-resolved (R3): we identify the
        // rig by "prefab root carries an XROrigin component" so we never hardcode
        // the sample's prefab filename or sub-path.
        const string TypeName_XROrigin = "Unity.XR.CoreUtils.XROrigin";

        // Ray-origin fix-up (defect ③) reflection targets. After we reparent the
        // sample's Left/Right Hand interactor subtrees, Unity discards prefab
        // overrides on inner objects and the NearFar caster/attach references
        // collapse to null — the ray then falls back to the interactor GameObject
        // (Camera Offset origin) instead of the hand. We re-point them at each
        // hand's own "Aim Pose" transform.
        //   * InteractionAttachController.transformToFollow  — attach point.
        //   * InteractionCasterBase.castOrigin               — near + far cast.
        //   * CurveVisualController.lineOriginTransform      — rendered line.
        const string TypeName_InteractionAttachController = "UnityEngine.XR.Interaction.Toolkit.Attachment.InteractionAttachController";
        const string TypeName_InteractionCasterBase       = "UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.InteractionCasterBase";
        const string TypeName_CurveVisualController       = "UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController";

        // Two-phase enable outcome (mirrors PXR_MCP_Plane.EnsureOutcome).
        public enum EnsureOutcome { Configured, ImportingRecompile, Error }

        // Reflection targets so we never hard-depend on a specific PICO SDK
        // version / assembly (R3). PXR_ProjectSetting lives in ByteDance.PICO.XR.
        const string TypeName_PXR_ProjectSetting = "Unity.XR.PXR.PXR_ProjectSetting";
        const string TypeName_PXR_ProjectSetting_Alt = "ByteDance.PICO.XR.PXR_ProjectSetting";

        // OpenXR reflection targets for the pinch->grab feature chain (R3). Present
        // only when the Unity OpenXR + XR Hands packages (and the PICO OpenXR SDK)
        // are installed; absent on the PICO-native input path, in which case the
        // feature enable is a harmless no-op. The generic OpenXRSettings/OpenXRFeature
        // reflection now lives in PXR_MCP_Common.EnableOpenXRFeature(); these two are
        // just the PICO hand feature type names passed to that shared helper.
        const string TypeName_HandTracking     = "UnityEngine.XR.Hands.OpenXR.HandTracking";
        const string TypeName_HandInteraction  = "UnityEngine.XR.OpenXR.Features.Interactions.HandInteractionProfile";

        // Defect ② (part 2) — the PICO SDK's own "XRI Hand Interaction" building
        // block. This is what ADDS the PICO hand device bindings (pinchTouched /
        // graspFirm -> the XRI "Select" action, plus Select Value / UI Press) into
        // the shared XRI Default Input Actions asset. Mounting an interactor rig is
        // only the RECEIVER half of pinch->grab; without these Select-action
        // bindings the pinch never becomes a select and the interactor never fires,
        // which is why controller grab worked but hand grab did not. We invoke the
        // SDK routine by reflection (R3) so the exact binding paths stay correct
        // across every XRI / PICO-SDK #if branch instead of us hand-rolling them.
        // Both variants live in ByteDance.PICO.XR.Editor and expose a public static
        // ExecuteBuildingBlockStatic() with NO scene side effects (verified: they
        // only edit + SaveAssets the InputActionAsset).
        static readonly string[] HandInteractionBuilderTypeNames =
        {
            "ByteDance.PICO.XR.Editor.PXR_BuildingBlocksXRIHandInteraction",        // PICO-native path
            "ByteDance.PICO.XR.Editor.PXR_BuildingBlocksOpenXRXRIHandInteraction",  // PICO OpenXR path
        };


        // Locate a PICO hand prefab dynamically. Avoids hardcoded version /
        // subdirectory paths that break when the PICO XR SDK is restructured.
        static GameObject LocateHandPrefab(string prefabName)
        {
            foreach (var guid in AssetDatabase.FindAssets(prefabName + " t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("pico", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!path.EndsWith("/" + prefabName + ".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null) return asset;
            }
            return null;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Hand/Ensure")]
#endif
        public static void Menu_Ensure() { Ensure(out _); }

        // Back-compat bool wrapper: true only when the block reached a fully
        // Configured state (models + interactors present).
        public static bool Ensure() => Ensure(out _) == EnsureOutcome.Configured;

        // Two-phase enable (mirrors PXR_MCP_Plane.Ensure):
        //   Phase A (always) - mount the PICO hand MODELS + wire tracking. These are
        //                      visual/tracking only and carry no XRI interactor.
        //   Phase B (defect ②) - ensure a HAND INTERACTOR rig exists so a pinch can
        //                      drive pico_xr_grab. The rig comes from the XRI "Hands
        //                      Interaction Demo" sample. Importing that sample copies
        //                      assets + triggers an Editor recompile, so the FIRST
        //                      call that needs the import returns ImportingRecompile;
        //                      the caller must settle-loop on pico_xr_status then call
        //                      enable AGAIN to instantiate the interactor subtrees.
        public static EnsureOutcome Ensure(out string detail)
        {
            detail = null;
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) { detail = "no agent XR Origin could be ensured"; return EnsureOutcome.Error; }

            var camOffset = origin.transform.Find(PXR_MCP_Common.CameraOffsetName);
            if (camOffset == null) { detail = "Camera Offset not found under the XR Origin"; return EnsureOutcome.Error; }

            // ---- Phase A: hand models + tracking (idempotent per-hand) ----
            if (camOffset.Find(MarkerLeft) == null || camOffset.Find(MarkerRight) == null)
            {
#if ENABLE_PICO_OPENXR_SDK
                // OpenXR runtime: mount the Unity XR Hands "HandVisualizer" models
                // (root = XRHandSkeletonDriver, always compiled with com.unity.xr.hands
                // on the OpenXR path). The PICO HandLeft/HandRight prefabs are rooted on
                // the native PXR_Hand driver, which is NOT compiled under
                // ENABLE_PICO_OPENXR_SDK — mounting them here would leave a missing
                // script on the prefab root. Mirrors the SDK's GenerateXRHands().
                var outA = EnsureOpenXRHandModels(camOffset, origin, out var openxrDetail);
                if (outA != EnsureOutcome.Configured) { detail = openxrDetail; return outA; }
#else
                var leftAsset  = LocateHandPrefab(HandLeftPrefabName);
                var rightAsset = LocateHandPrefab(HandRightPrefabName);
                if (leftAsset == null || rightAsset == null)
                {
                    detail = "HandLeft/HandRight prefab not found in any pico-* package. Make sure " +
                             "com.bytedance.pico.xr is installed and contains Assets/Resources/Prefabs/" +
                             "HandLeft.prefab and HandRight.prefab.";
                    Debug.LogError("[PICO MCP] " + detail);
                    return EnsureOutcome.Error;
                }

                var leftInst  = MountHand(camOffset, leftAsset,  MarkerLeft);
                var rightInst = MountHand(camOffset, rightAsset, MarkerRight);

                // Wire the mounted hands into the XR Origin's XRInputModalityManager
                // so XRI natively hides them whenever a controller becomes tracked.
                PXR_MCP_Common.WireHandsToModalityManager(origin, leftInst, rightInst);

                // Turn on the project-level hand-tracking flag via reflection (R3).
                // This is the ONLY runtime gate for PXR_Hand tracking, so warn loudly
                // if it could not be applied (hands would mount but never track).
                if (!EnableHandTrackingProjectSetting())
                    Debug.LogWarning("[PICO MCP] Hands mounted but PXR_ProjectSetting.handTracking could not be applied — tracking will not run until Hand Tracking is enabled in PICO XR project settings.");
#endif

                // Enable the OpenXR HandTracking + HandInteractionProfile features so
                // hand pinch is delivered as an XRI select input (the pinch->grab chain).
                // Mirrors the PICO SDK's PXR_Utils.EnableHandTrackingFeature(); non-fatal
                // and a no-op if the OpenXR SDK is not present (native path). (R3)
                EnableOpenXRHandInteractionFeature();
            }

            // Defect ② (part 2): ADD the PICO hand pinch/grasp bindings to the XRI
            // "Select" action. This is the ACTION half of pinch->grab and is what was
            // missing — controller grab worked because the controller preset already
            // binds Select, but the hand's Select action had no PICO device binding,
            // so a pinch never became a select. Idempotent (the SDK routine only adds
            // a binding if absent). Run every enable so it self-heals a project whose
            // input asset predates this fix.
            ApplyPicoHandInteractionBindings();

            // ---- Phase B: XRI hand INTERACTOR rig (defect ②) ----
            var outcome = EnsureHandInteractors(camOffset, out var interactorDetail);
            detail = interactorDetail;
            if (outcome == EnsureOutcome.Configured)
                Debug.Log("[PICO MCP] Hand tracking enabled (models + interactors).");
            return outcome;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Hand/Remove")]
#endif
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { Debug.Log("[PICO MCP] No agent XR Origin."); return; }
            // Drop the hand references from the modality manager first so it stops
            // driving the GameObjects we are about to delete (R4/R5).
            PXR_MCP_Common.ClearHandsFromModalityManager(origin.gameObject);
            foreach (var t in origin.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t == null) continue;
                if (t.name == MarkerLeft || t.name == MarkerRight
                 || t.name == MarkerInteractorLeft || t.name == MarkerInteractorRight)
                    Undo.DestroyObjectImmediate(t.gameObject);
            }
            Debug.Log("[PICO MCP] Hand tracking removed.");
        }

        static GameObject MountHand(Transform camOffset, GameObject asset, string markerName)
        {
            var existing = camOffset.Find(markerName);
            if (existing != null) return existing.gameObject; // idempotent per-hand
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, camOffset);
            Undo.RegisterCreatedObjectUndo(inst, "PICO MCP mount hand");
            inst.name = markerName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale    = Vector3.one;
            inst.SetActive(true);
            return inst;
        }

        // -----------------------------------------------------------------
        // OpenXR hand MODELS (defect ① — missing PXR_Hand script under OpenXR)
        // -----------------------------------------------------------------
        // On the OpenXR runtime the PICO HandLeft/HandRight prefabs cannot be used:
        // their root component PXR_Hand compiles ONLY under ENABLE_PICO_XR_SDK (the
        // symbol is mutually exclusive with ENABLE_PICO_OPENXR_SDK), so mounting them
        // here leaves "The associated script can not be loaded" on the prefab root.
        // Mirror the PICO SDK's own OpenXR hand building block (GenerateXRHands):
        // mount the Unity XR Hands "HandVisualizer" prefabs (Left/Right Hand Tracking),
        // whose root XRHandSkeletonDriver is always compiled with com.unity.xr.hands
        // on this path. Two-phase like the interactor rig: if the sample isn't on disk
        // we import it and ask the caller to settle-loop then re-enable.
        static EnsureOutcome EnsureOpenXRHandModels(Transform camOffset, GameObject origin, out string detail)
        {
            detail = null;

            var leftAsset  = LocateXrHandPrefab(XrHandLeftPrefabName);
            var rightAsset = LocateXrHandPrefab(XrHandRightPrefabName);
            if (leftAsset == null || rightAsset == null)
            {
                var imp = PXR_MCP_PackageOps.ImportSample(XrHandsPackageName, HandVisualizerSample);
                if (imp == null || !imp.ok || imp.skipped)
                {
                    detail = "Could not import the XR Hands '" + HandVisualizerSample + "' sample" +
                             (imp != null && !string.IsNullOrEmpty(imp.error) ? ": " + imp.error : "") +
                             (imp != null && imp.skipped ? " (" + imp.warning + ")" : "") +
                             ". Install " + XrHandsPackageName + " and retry.";
                    return EnsureOutcome.Error;
                }

                AssetDatabase.Refresh();
                leftAsset  = LocateXrHandPrefab(XrHandLeftPrefabName);
                rightAsset = LocateXrHandPrefab(XrHandRightPrefabName);
                if (leftAsset == null || rightAsset == null)
                {
                    detail = "XR Hands '" + HandVisualizerSample + "' sample imported; the Editor is " +
                             "(re)importing/compiling. Poll pico_xr_status until the MCP bridge returns, " +
                             "then call pico_xr_hand(action=enable) again to mount the hand models.";
                    return EnsureOutcome.ImportingRecompile;
                }
            }

            var leftInst  = MountHand(camOffset, leftAsset,  MarkerLeft);
            var rightInst = MountHand(camOffset, rightAsset, MarkerRight);

            // Wire the mounted hands into the XR Origin's XRInputModalityManager so
            // XRI natively hides them whenever a controller becomes tracked (same as
            // the native path).
            PXR_MCP_Common.WireHandsToModalityManager(origin, leftInst, rightInst);

            // handTracking is the same PICO project gate on both runtimes; set it via
            // reflection (R3). Non-fatal warning if it could not be applied.
            if (!EnableHandTrackingProjectSetting())
                Debug.LogWarning("[PICO MCP] XR Hands models mounted but PXR_ProjectSetting.handTracking could not be applied — tracking will not run until Hand Tracking is enabled in PICO XR project settings.");

            detail = "OpenXR XR Hands models mounted.";
            return EnsureOutcome.Configured;
        }

        // Locate a Unity XR Hands HandVisualizer prefab ("Left Hand Tracking" /
        // "Right Hand Tracking") dynamically inside the imported sample. Matches by
        // exact prefab file name under an "XR Hands" sample path so we do not bind to
        // a hardcoded package version (R3). Returns null when not yet imported.
        static GameObject LocateXrHandPrefab(string prefabName)
        {
            foreach (var guid in AssetDatabase.FindAssets(prefabName + " t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("XR Hands", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!path.EndsWith("/" + prefabName + ".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Defect ② + ③ — XRI Hand Interactor rig
        // -----------------------------------------------------------------
        // The PICO hand prefabs are visual/tracking only; without an XRI interactor
        // (Interactor component + select InputActionReference + Attach Transform) a
        // pinch never becomes a select and pico_xr_grab has nothing to grab with.
        // We source the ready-wired interactor rig from the XRI "Hands Interaction
        // Demo" sample — the SAME sample the PICO SDK's own "XRI Hand Interaction"
        // building block relies on. Concretely we instantiate the sample's
        // "XR Origin Hands (XR Rig)" prefab (identified reflection-first by an
        // XROrigin component on its root, so we never hardcode its filename),
        // unpack it, then EXTRACT its `Camera Offset/Left Hand` and
        // `Camera Offset/Right Hand` interactor subtrees, rename them into our
        // agent-owned markers and reparent them under OUR Camera Offset.
        //
        // A blind PrefabUtility.InstantiatePrefab(<sample sub-prefab>) does NOT
        // work here for two reasons the previous implementation tripped over:
        //   (1) `Left Hand` / `Right Hand` are NOT independent prefabs — they only
        //       exist as GameObjects inside the top-level rig prefab. Scanning for
        //       "any prefab whose name contains left/right and carries an
        //       interactor" finds unrelated leaves like HandInteractorAffordances
        //       or PinchPointStabilized and nothing usable ever gets mounted.
        //   (2) The interactor GameObjects rely on prefab OVERRIDES to point
        //       `NearFarInteractor` casters + `CurveVisualController` line origin
        //       at their sibling "Aim Pose" transform. As soon as you reparent an
        //       inner object out of the top-level prefab instance, Unity discards
        //       those overrides and the references collapse to null — the ray
        //       then falls back to the interactor's own Transform (Camera Offset
        //       origin) and no longer follows the hand.
        // We therefore extract from an unpacked instance (so no override is lost)
        // AND explicitly re-wire the four ray-origin references to the local Aim
        // Pose after reparent (defect ③).
        //
        // TWO-PHASE, exactly like the Plane / Spatial Mesh drivers: importing the
        // sample copies assets and triggers an Editor recompile, so the first call
        // that needs the import returns ImportingRecompile and the caller must
        // settle-loop then call enable again.
        static EnsureOutcome EnsureHandInteractors(Transform camOffset, out string detail)
        {
            detail = null;

            // Idempotent (R1): both interactor markers already mounted -> done.
            if (camOffset.Find(MarkerInteractorLeft) != null && camOffset.Find(MarkerInteractorRight) != null)
            {
                detail = "Hand models + interactors already present.";
                return EnsureOutcome.Configured;
            }

            // Locate the XRI Hands Interaction Demo rig prefab. If not on disk yet,
            // trigger the sample import (two-phase); mirror the Plane driver's
            // recompile handshake so the caller can settle-loop then re-enable.
            var rigPrefab = TryLocateHandsRigPrefab();
            if (rigPrefab == null)
            {
                var imp = PXR_MCP_PackageOps.ImportSample(XriPackageName, HandsInteractionDemoSample);
                if (imp == null || !imp.ok)
                {
                    detail = "Could not import the XRI '" + HandsInteractionDemoSample + "' sample" +
                             (imp != null && !string.IsNullOrEmpty(imp.error) ? ": " + imp.error : "") +
                             ". Install com.unity.xr.interaction.toolkit and retry.";
                    return EnsureOutcome.Error;
                }
                if (imp.skipped)
                {
                    detail = "XRI '" + HandsInteractionDemoSample + "' sample import skipped (" +
                             imp.warning + "). Install com.unity.xr.interaction.toolkit first.";
                    return EnsureOutcome.Error;
                }

                // Freshly imported: assets are landing and the Editor is recompiling.
                // Re-scan; if the rig prefab still isn't visible, tell the caller to
                // settle-loop then enable again (mirrors the Plane block).
                AssetDatabase.Refresh();
                rigPrefab = TryLocateHandsRigPrefab();
                if (rigPrefab == null)
                {
                    detail = "XRI '" + HandsInteractionDemoSample + "' sample imported; the Editor is " +
                             "(re)importing/compiling. Poll pico_xr_status until the MCP bridge returns, " +
                             "then call pico_xr_hand(action=enable) again to mount the hand interactors.";
                    return EnsureOutcome.ImportingRecompile;
                }
            }

            // Instantiate + unpack the rig into the active scene as a temporary host
            // so we can freely reparent inner objects (see comment block above).
            var scene = camOffset.gameObject.scene;
            GameObject rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            if (rigInstance == null)
            {
                detail = "Could not instantiate the XRI hand rig prefab '" + AssetDatabase.GetAssetPath(rigPrefab) + "'.";
                return EnsureOutcome.Error;
            }
            try
            {
                rigInstance.name = "__PICO_MCP_XRI_HandsRig_Temp";
                rigInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // Hide from the user during the (brief) extraction window (R2).
                rigInstance.hideFlags = HideFlags.DontSaveInEditor;
                Undo.RegisterCreatedObjectUndo(rigInstance, "PICO MCP mount hand interactor rig");
                PrefabUtility.UnpackPrefabInstance(rigInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                // Find `Camera Offset/Left Hand` and `.../Right Hand` on the unpacked
                // rig. Fall back to already-renamed marker names so the routine is
                // resilient to being retried on a partially-mounted scene.
                var rigCamOffset = rigInstance.transform.Find(PXR_MCP_Common.CameraOffsetName);
                if (rigCamOffset == null)
                {
                    detail = "XRI hand rig prefab has no '" + PXR_MCP_Common.CameraOffsetName + "' child.";
                    return EnsureOutcome.Error;
                }

                Transform srcLeft  = rigCamOffset.Find(SampleHandGroupLeft)
                                  ?? rigCamOffset.Find(MarkerInteractorLeft);
                Transform srcRight = rigCamOffset.Find(SampleHandGroupRight)
                                  ?? rigCamOffset.Find(MarkerInteractorRight);
                if (srcLeft == null && srcRight == null)
                {
                    detail = "XRI hand rig prefab has no '" + SampleHandGroupLeft + "' / '" + SampleHandGroupRight + "' children.";
                    return EnsureOutcome.Error;
                }

                if (srcLeft != null && camOffset.Find(MarkerInteractorLeft) == null)
                    ExtractHandGroup(srcLeft, camOffset, MarkerInteractorLeft);
                if (srcRight != null && camOffset.Find(MarkerInteractorRight) == null)
                    ExtractHandGroup(srcRight, camOffset, MarkerInteractorRight);
            }
            finally
            {
                // Always tear down the temporary rig, even on error, so we never
                // leave a phantom XR Origin next to the agent's XR Origin.
                if (rigInstance != null)
                    Undo.DestroyObjectImmediate(rigInstance);
            }

            if (camOffset.Find(MarkerInteractorLeft) == null && camOffset.Find(MarkerInteractorRight) == null)
            {
                detail = "Extraction from XRI hand rig completed without mounting any interactor.";
                return EnsureOutcome.Error;
            }

            detail = "Hand models + XRI hand interactors mounted.";
            return EnsureOutcome.Configured;
        }

        // Rename + reparent a `Left Hand` / `Right Hand` interactor group onto our
        // Camera Offset, then repair its NearFar ray-origin references so the far
        // ray actually originates from the hand's Aim Pose (defect ③).
        static void ExtractHandGroup(Transform sourceGroup, Transform targetCamOffset, string markerName)
        {
            sourceGroup.name = markerName;
            sourceGroup.SetParent(targetCamOffset, false);
            sourceGroup.localPosition = Vector3.zero;
            sourceGroup.localRotation = Quaternion.identity;
            sourceGroup.localScale    = Vector3.one;
            sourceGroup.gameObject.SetActive(true);
            RepairRayOriginReferences(sourceGroup);
        }

        // Locate the top-level "XR Origin Hands (XR Rig)" prefab in the imported
        // Hands Interaction Demo sample. We identify it structurally (XROrigin
        // component on the root, `Camera Offset/Left Hand|Right Hand` under it),
        // not by filename, so a future XRI rename does not silently break us.
        static GameObject TryLocateHandsRigPrefab()
        {
            var xrOriginType = PXR_MCP_Common.FindLoadedType(TypeName_XROrigin);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf(HandsInteractionDemoSample, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                // Root must carry an XROrigin (when XRI's dependency is loaded).
                if (xrOriginType != null && go.GetComponent(xrOriginType) == null)
                    continue;

                var camOffset = go.transform.Find(PXR_MCP_Common.CameraOffsetName);
                if (camOffset == null) continue;
                if (camOffset.Find(SampleHandGroupLeft) == null &&
                    camOffset.Find(SampleHandGroupRight) == null)
                    continue;

                return go;
            }
            return null;
        }

        // Re-target NearFarInteractor + line-visual references at the hand's own
        // "Aim Pose" child so the ray follows the hand instead of Camera Offset
        // origin. All access is reflection so we do not force-link to a specific
        // XRI major version at build time (R3). No-op when the components or the
        // Aim Pose child are absent (e.g. hand path that swapped to a different
        // interactor prefab structure).
        static void RepairRayOriginReferences(Transform handGroup)
        {
            var aim = handGroup.Find(AimPoseChildName);
            if (aim == null) return;

            var attachType = PXR_MCP_Common.FindLoadedType(TypeName_InteractionAttachController);
            var casterType = PXR_MCP_Common.FindLoadedType(TypeName_InteractionCasterBase);
            var curveVisType = PXR_MCP_Common.FindLoadedType(TypeName_CurveVisualController);

            // InteractionAttachController.transformToFollow — attach point that the
            // NearFarInteractor snaps grabbed objects toward, and the far-ray
            // pivot the sample uses. Property setter is public in XRI 3.x.
            if (attachType != null)
            {
                foreach (var comp in handGroup.GetComponentsInChildren(attachType, true))
                    SetPropertyOrField(comp, "transformToFollow", "m_TransformToFollow", aim);
            }

            // Both SphereInteractionCaster + CurveInteractionCaster inherit
            // `castOrigin` from InteractionCasterBase — one property write covers
            // near-cast AND far-cast origins.
            if (casterType != null)
            {
                foreach (var comp in handGroup.GetComponentsInChildren(casterType, true))
                    SetPropertyOrField(comp, "castOrigin", "m_CastOrigin", aim);
            }

            // The rendered curve (LineRenderer) is driven by CurveVisualController.
            // Without a lineOriginTransform it snaps to its own GameObject (the
            // LineVisual child of the interactor) which is again pinned at Camera
            // Offset — same visual defect as the caster origins.
            if (curveVisType != null)
            {
                foreach (var comp in handGroup.GetComponentsInChildren(curveVisType, true))
                    SetPropertyOrField(comp, "lineOriginTransform", "m_LineOriginTransform", aim);
            }
        }

        // Small reflection helper: set a public property when it exists, otherwise
        // fall back to a serialized private field. Both are used across XRI
        // versions (public setters in 3.x, direct m_* fields in 2.x prefab data).
        static void SetPropertyOrField(UnityEngine.Object target, string propertyName, string fieldName, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            var prop = t.GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsInstanceOfType(value))
            {
                Undo.RecordObject(target, "PICO MCP repair hand ray origin");
                prop.SetValue(target, value);
                EditorUtility.SetDirty(target);
                return;
            }
            var field = t.GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                Undo.RecordObject(target, "PICO MCP repair hand ray origin");
                field.SetValue(target, value);
                EditorUtility.SetDirty(target);
            }
        }

        // Resolve any known XRI interactor component type (3.x first, 2.x fallback).
        static Type ResolveInteractorType()
        {
            foreach (var n in InteractorTypeNames)
            {
                var t = PXR_MCP_Common.FindLoadedType(n);
                if (t != null) return t;
            }
            return null;
        }

        // Scan the imported XRI "Hands Interaction Demo" sample folder for the
        // left/right hand-interactor prefabs. Kept only as a legacy helper —
        // superseded by TryLocateHandsRigPrefab(), which extracts the interactor
        // groups directly out of the top-level rig prefab so prefab overrides
        // (see defect ③) survive the reparent. Reserved here for callers that
        // still want a plain "does the sample expose any left/right leaf
        // interactor prefab" boolean.
        static bool AnyInteractorPrefabInSample(out GameObject leftPrefab, out GameObject rightPrefab)
        {
            leftPrefab = null;
            rightPrefab = null;
            var interactorType = ResolveInteractorType();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf(HandsInteractionDemoSample, StringComparison.OrdinalIgnoreCase) < 0
                 && path.IndexOf("Hands Interaction", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                // Must contain an interactor somewhere in the subtree.
                bool hasInteractor = false;
                if (interactorType != null)
                {
                    hasInteractor = go.GetComponentInChildren(interactorType, true) != null;
                }
                else
                {
                    // XRI type not loaded yet (sample still importing): fall back to a
                    // name heuristic so the two-phase re-scan can still make progress.
                    hasInteractor = go.name.IndexOf("interactor", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (!hasInteractor) continue;

                var lower = go.name.ToLowerInvariant();
                bool isLeft  = lower.Contains("left")  || lower.Contains(" l ") || lower.EndsWith(" l");
                bool isRight = lower.Contains("right") || lower.Contains(" r ") || lower.EndsWith(" r");
                if (isLeft && leftPrefab == null)   leftPrefab = go;
                else if (isRight && rightPrefab == null) rightPrefab = go;
            }

            return leftPrefab != null || rightPrefab != null;
        }


        // Apply the project-level hand-tracking configuration via reflection (R3):
        //   * handTracking = true                     (the ONLY runtime gate for PXR_Hand)
        //   * handTrackingSupportType = ControllersAndHands (so controllers keep working)
        // Returns true when handTracking was successfully set (the caller warns
        // otherwise). Silently no-ops / returns false if the PICO SDK type is
        // absent (older/absent SDK).
        static bool EnableHandTrackingProjectSetting()
        {
            var t = FindType(TypeName_PXR_ProjectSetting) ?? FindType(TypeName_PXR_ProjectSetting_Alt);
            if (t == null) return false;
            try
            {
                var getCfg = t.GetMethod("GetProjectConfig", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getCfg == null) return false;
                var cfg = getCfg.Invoke(null, null);
                if (cfg == null) return false;

                bool handTrackingSet = false;
                var field = cfg.GetType().GetField("handTracking");
                if (field != null) { field.SetValue(cfg, true); handTrackingSet = true; }

                // Best-effort: also widen the support type so both controllers and
                // hands are delivered. The field is an enum; resolve the
                // "ControllersAndHands" member by name so we never hardcode its
                // numeric value (R3). Missing field/enum member is non-fatal.
                var supportField = cfg.GetType().GetField("handTrackingSupportType");
                if (supportField != null && supportField.FieldType.IsEnum)
                {
                    foreach (var name in Enum.GetNames(supportField.FieldType))
                    {
                        if (string.Equals(name, "ControllersAndHands", StringComparison.OrdinalIgnoreCase))
                        {
                            supportField.SetValue(cfg, Enum.Parse(supportField.FieldType, name));
                            break;
                        }
                    }
                }

                var save = t.GetMethod("SaveAssets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (save != null) save.Invoke(null, null);
                return handTrackingSet;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PICO MCP] Could not set handTracking project setting: " + e.Message);
                return false;
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        // Enable the OpenXR HandTracking + HandInteractionProfile features on the
        // Android build target so hand pinch surfaces as an XRI select input
        // (the OpenXR pinch->grab chain). Delegates to the shared
        // PXR_MCP_Common.EnableOpenXRFeature() (which mirrors the PICO SDK's
        // PXR_Utils.EnableOpenXRFeature<T>(): target BuildTargetGroup.Android,
        // iterate GetFeatures<OpenXRFeature>(), flip enabled=true, SetDirty +
        // SaveAssets + NotifySettingsProviderChanged). Reflection-guarded (R3) so a
        // missing OpenXR / XR Hands assembly is a silent no-op (native path).
        static void EnableOpenXRHandInteractionFeature()
        {
            PXR_MCP_Common.EnableOpenXRFeature(TypeName_HandTracking);
            PXR_MCP_Common.EnableOpenXRFeature(TypeName_HandInteraction);
        }

        // Defect ② (part 2) — ADD the PICO hand pinch/grasp device bindings to the
        // shared XRI Default Input Actions asset's "Select" action (plus Select
        // Value / UI Press). This is the missing ACTION half of pinch->grab: the
        // interactor rig can RECEIVE a select, but on the hand path nothing was
        // DELIVERING one, because the hand's Select action carried no PICO device
        // binding (the controller path worked only because its Starter-Assets preset
        // already binds Select).
        //
        // Rather than re-implement the ~500 lines of version-forked binding code, we
        // invoke the PICO SDK's canonical routine
        // PXR_BuildingBlocks(OpenXR)XRIHandInteraction.ExecuteBuildingBlockStatic()
        // by reflection (R3). That routine:
        //   * imports the "Hands Interaction Demo" sample if needed (we already did),
        //   * adds "<PicoHandInteraction>{LeftHand}/pinchTouched" + "/graspFirm" (and
        //     the OpenXR "<HandInteraction>{...}/pinchTouched") to Select,
        //   * is idempotent (only adds a binding when absent),
        //   * has NO scene side effects (verified — it only edits + SaveAssets the
        //     InputActionAsset).
        // If the SDK type is absent (SDK too old / not installed) this is a warned
        // no-op; the interactor rig is still mounted, but the user must run the PICO
        // "XRI Hand Interaction" building block manually for pinch to select.
        static void ApplyPicoHandInteractionBindings()
        {
            try
            {
                Type builder = null;
                foreach (var n in HandInteractionBuilderTypeNames)
                {
                    builder = FindType(n);
                    if (builder != null) break;
                }
                if (builder == null)
                {
                    Debug.LogWarning("[PICO MCP] PICO 'XRI Hand Interaction' building block not found; hand pinch " +
                                     "may not fire a select. Update the PICO SDK, or run PICO > Building Blocks > " +
                                     "XRI Hand Interaction once so the hand Select bindings are added.");
                    return;
                }

                var exec = builder.GetMethod("ExecuteBuildingBlockStatic",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (exec == null)
                {
                    Debug.LogWarning("[PICO MCP] " + builder.Name + ".ExecuteBuildingBlockStatic() not found; " +
                                     "cannot add hand Select bindings automatically.");
                    return;
                }

                exec.Invoke(null, null);
                Debug.Log("[PICO MCP] Applied PICO hand-interaction Select bindings via " + builder.Name + ".");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PICO MCP] Could not apply PICO hand-interaction Select bindings " +
                                 "(hand pinch may not grab): " + e.Message);
            }
        }
    }

    // ---------------- Grab & Drag (pick-up + drag) ----------------
    // Object pickup / drag needs BOTH halves of the XRI interaction pair wired up,
    // because either half alone is inert:
    //   * Interactor side  — the hand/controller must carry an interactor that can
    //     initiate a grab, and the scene must contain an XRInteractionManager that
    //     brokers interactor<->interactable handshakes.
    //   * Interactable side — the target object must carry a Collider + Rigidbody +
    //     XRGrabInteractable so it can be selected and follow the interactor.
    // Enabling just one side "works" in the sense of not erroring, but produces
    // nothing grabbable, so this block owns both halves in a single tool.
    //
    // Ensure() sets up the interactor side (idempotently) and MakeGrabbable()
    // upgrades a target object into a grabbable. The XRI Starter Assets XR Origin
    // already ships a NearFarInteractor under each Left/Right Controller, so on the
    // controller path Ensure() mostly re-shows the controller module and guarantees
    // an XRInteractionManager; it only adds an interactor when a rig somehow lacks
    // one. Every XRI type is reflection-resolved (R3: XRI namespaces drift across
    // major versions — the Interactors./Interactables. sub-namespaces are 3.x-only,
    // with 2.x legacy fallbacks).
    public static class PXR_MCP_Grab
    {
        // Idempotency sentinel (R1) parented under the agent XR Origin.
        public const string MarkerChild = "[PICO_MCP] Grab Marker";
        // Agent-owned interaction-manager host (created only if the scene has none).
        public const string ManagerHostName = "[PICO_MCP] XR Interaction Manager";
        // Agent-owned sample grabbable spawned when MakeGrabbable is called w/o target.
        public const string SampleGrabbableName = "[PICO_MCP] Grabbable Sample";

        // Reflection targets (R3). 3.x sub-namespace first, then 2.x legacy fallback.
        const string TypeName_InteractionManager   = "UnityEngine.XR.Interaction.Toolkit.XRInteractionManager";
        const string TypeName_GrabInteractable       = "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable";
        const string TypeName_GrabInteractable_Legacy= "UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable";

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Grab/Ensure")]
#endif
        public static bool Ensure()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;

            // DECOUPLING (R2): Grab is NOT bound to hand or controller. It must not
            // force the controller module visible, nor decide which input source
            // (hand vs controller) is active — that is the job of the dedicated
            // pico_xr_controller / pico_xr_hand blocks. In particular grab must NOT
            // re-show the generic Starter Assets controllers, because whether a
            // controller appears — and that its model be the PICO prefab — is owned
            // solely by pico_xr_controller enable. Grab only wires up the shared
            // interaction broker and makes objects grabbable; the interactor side is
            // provided by whichever input block the user enabled.

            // Guarantee an XRInteractionManager exists somewhere in the scene; XRI
            // interactors/interactables are inert without one. Create an agent-owned
            // host only when the scene has none (R2: never touch a foreign manager).
            // If the XRI type cannot be resolved (XRI not installed), enabling grab
            // is meaningless — bail out WITHOUT dropping the marker so `enable`
            // reports Error and `status` does not later report a phantom enable.
            if (!EnsureInteractionManager(origin.gameObject))
            {
                Debug.LogWarning("[PICO MCP] Grab enable aborted: could not resolve/create XRInteractionManager (XRI not installed?); grab will not function until XRI is present.");
                return false;
            }

            // Final re-check (defence in depth): only commit the idempotency marker
            // and report success once a scene XRInteractionManager is actually
            // present, so `enable` success == a real, usable grab state and a later
            // `status` never sees a marker without a working interaction manager.
            if (!HasInteractionManager())
            {
                Debug.LogWarning("[PICO MCP] Grab enable aborted: no XRInteractionManager in scene after ensure; not committing marker.");
                return false;
            }

            // Idempotency sentinel (R1).
            if (origin.transform.Find(MarkerChild) == null)
            {
                var m = new GameObject(MarkerChild);
                Undo.RegisterCreatedObjectUndo(m, "PICO MCP grab marker"); // R5
                m.transform.SetParent(origin.transform, false);
                m.SetActive(false);
            }

            Debug.Log("[PICO MCP] Grab interaction manager ensured (input source owned by controller/hand blocks).");
            return true;
        }

        // Upgrade a scene object into an XRI grabbable: Collider + Rigidbody +
        // XRGrabInteractable. When targetName is null/empty, spawn an agent-owned
        // sample cube in front of the Main Camera so the user has something to test.
        // Returns the affected GameObject name, or null on failure.
        public static string MakeGrabbable(string targetName)
        {
            var grabType = PXR_MCP_Common.FindLoadedType(TypeName_GrabInteractable)
                        ?? PXR_MCP_Common.FindLoadedType(TypeName_GrabInteractable_Legacy);
            if (grabType == null)
            {
                Debug.LogError("[PICO MCP] XRGrabInteractable type not found (XRI not installed?). Cannot make object grabbable.");
                return null;
            }

            GameObject target;
            bool spawnedSample = false;
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                target = ResolveSceneObject(targetName);
                if (target == null)
                {
                    Debug.LogError("[PICO MCP] Grab target '" + targetName + "' not found in the active scene.");
                    return null;
                }
            }
            else
            {
                target = SpawnSampleGrabbable();
                spawnedSample = true;
                if (target == null) return null;
            }

            // Collider (required for selection). Add a BoxCollider if none present.
            if (target.GetComponentInChildren<Collider>(true) == null)
            {
                var col = Undo.AddComponent<BoxCollider>(target); // R5
                if (col != null) EditorUtility.SetDirty(col);
            }

            // Rigidbody (XRGrabInteractable drives it). Add if missing; leave the
            // user's existing physics settings alone when it already has one (R1).
            // When WE add it, make it a NON-falling kinematic body: without this the
            // object free-falls the instant it becomes grabbable (before the user
            // ever selects it). XRGrabInteractable flips it to kinematic-with-motion
            // on select and restores per forceGravityOnDetach on deselect, so
            // useGravity=false + isKinematic=true is the correct resting state.
            // (PICO SDK's own Grab building block uses useGravity=false; mass=0;
            //  drag/linearDamping=2f on a fresh sample cube — we prefer kinematic
            //  here because make_grabbable targets arbitrary pre-existing objects.)
            var rb = target.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(target); // R5
                if (rb != null)
                {
                    rb.useGravity  = false;
                    rb.isKinematic = true;
                    EditorUtility.SetDirty(rb);
                }
            }

            // XRGrabInteractable (reflection-added, R3). Idempotent: skip if present.
            if (target.GetComponent(grabType) == null)
            {
                var comp = Undo.AddComponent(target, grabType); // R5
                if (comp == null)
                {
                    Debug.LogError("[PICO MCP] Failed to add XRGrabInteractable to '" + target.name + "'.");
                    return null;
                }
                EditorUtility.SetDirty(comp);
            }

            Debug.Log("[PICO MCP] '" + target.name + "' is now grabbable" + (spawnedSample ? " (spawned sample)." : "."));
            return target.name;
        }

#if PICO_MCP_SHOW_MENU
        [MenuItem("PICO MCP/Grab/Make Sample Grabbable")]
        static void Menu_MakeSampleGrabbable() => MakeGrabbable(null);
        [MenuItem("PICO MCP/Grab/Remove")]
#endif
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) { Debug.Log("[PICO MCP] No agent XR Origin."); return; }

            // Remove agent-owned objects only (R2). We never strip XRGrabInteractable
            // from user target objects because we do not track which ones we upgraded.
            foreach (var t in origin.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t == null) continue;
                if (t.name == MarkerChild) Undo.DestroyObjectImmediate(t.gameObject);
            }

            // Remove the agent-owned interaction-manager host + sample grabbable if
            // WE created them (identified by name).
            var host = GameObject.Find(ManagerHostName);
            if (host != null) Undo.DestroyObjectImmediate(host);
            var sample = GameObject.Find(SampleGrabbableName);
            if (sample != null) Undo.DestroyObjectImmediate(sample);

            Debug.Log("[PICO MCP] Grab interactor side removed (user-authored grabbables left intact).");
        }

        // True when a scene XRInteractionManager exists (grab is otherwise inert).
        public static bool HasInteractionManager()
        {
            var t = PXR_MCP_Common.FindLoadedType(TypeName_InteractionManager);
            if (t == null) return false;
#if UNITY_2023_1_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = UnityEngine.Object.FindObjectsOfType(t, true);
#endif
            return found != null && found.Length > 0;
        }

        // Ensure a scene XRInteractionManager exists; create an agent-owned host when
        // the scene has none. Returns false only when the XRI type cannot be resolved.
        static bool EnsureInteractionManager(GameObject originGo)
        {
            var t = PXR_MCP_Common.FindLoadedType(TypeName_InteractionManager);
            if (t == null) return false;
            if (HasInteractionManager()) return true;

            var host = new GameObject(ManagerHostName);
            Undo.RegisterCreatedObjectUndo(host, "PICO MCP create XR Interaction Manager"); // R5
            if (originGo != null) host.transform.SetParent(originGo.transform, false);
            var comp = Undo.AddComponent(host, t);
            if (comp != null) EditorUtility.SetDirty(comp);
            Debug.Log("[PICO MCP] XR Interaction Manager created.");
            return true;
        }

        // Resolve a scene GameObject by hierarchy path ("Parent/Child") or by name.
        static GameObject ResolveSceneObject(string nameOrPath)
        {
            var direct = GameObject.Find(nameOrPath);
            if (direct != null) return direct;
            // Fall back to a name match across the active scene (including inactive).
            var leaf = nameOrPath;
            var slash = nameOrPath.LastIndexOf('/');
            if (slash >= 0 && slash < nameOrPath.Length - 1) leaf = nameOrPath.Substring(slash + 1);
#if UNITY_2023_1_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<Transform>(true);
#endif
            var hit = all.FirstOrDefault(tr => tr != null && tr.name == leaf);
            return hit != null ? hit.gameObject : null;
        }

        // Spawn an agent-owned sample cube ~1.5m in front of the Main Camera so the
        // user has something concrete to grab. Idempotent by name (R1).
        static GameObject SpawnSampleGrabbable()
        {
            var existing = GameObject.Find(SampleGrabbableName);
            if (existing != null) return existing;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "PICO MCP spawn sample grabbable"); // R5
            cube.name = SampleGrabbableName;
            cube.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

            var origin = PXR_MCP_Common.FindAgentOrigin();
            var cam = origin != null ? PXR_MCP_Common.GetMainCamera(origin.gameObject) : null;
            if (cam != null)
                cube.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
            else
                cube.transform.position = new Vector3(0f, 1.0f, 1.5f);
            EditorUtility.SetDirty(cube);
            return cube;
        }
    }
}
