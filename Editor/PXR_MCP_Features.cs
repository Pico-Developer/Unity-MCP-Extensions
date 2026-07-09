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
    // EnsureXROrigin(). These MenuItems exist only for manual smoke testing in
    // an Editor without the MCP bridge (mirrors every other block's pattern).
    public static class PXR_MCP_Camera
    {
        [MenuItem("PICO MCP/Camera/Enforce Single Active Camera")]
        public static void Menu_Enforce()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) { Debug.LogError("[PICO MCP] No agent XR Origin."); return; }
            var n = PXR_MCP_Common.EnsureSingleActiveCamera(origin);
            Debug.Log($"[PICO MCP] Enforce single active camera: {n} foreign camera(s) disabled.");
        }

        [MenuItem("PICO MCP/Camera/Restore Foreign Cameras")]
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

        [MenuItem("PICO MCP/Spatial Mesh/Ensure")]
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
                         "If the type never appears, verify the PICO XR SDK is installed (the driver is guarded by ENABLE_PICO_XR_SDK).";
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

            Debug.Log("[PICO MCP] Spatial Mesh: SpatialMeshManager mounted and configured.");
            detail = "SpatialMeshManager mounted and configured.";
            return EnsureOutcome.Configured;
        }

        [MenuItem("PICO MCP/Spatial Mesh/Remove")]
        public static void Remove()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return;
            var c = origin.transform.Find(ContainerName);
            if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
            // Imported assets under ProjectAssetsDir are left in place (non-destructive:
            // they may be shared / re-enabled later).
            Debug.Log("[PICO MCP] Spatial Mesh removed.");
        }

        // -----------------------------------------------------------------
        // Asset import + repair helpers
        // -----------------------------------------------------------------

        static bool ImportBundledAssets(out string detail)
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

            bool copiedAny = false;
            foreach (var f in BundledFiles)
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
        public const string HandLeftPrefabName  = "HandLeft";
        public const string HandRightPrefabName = "HandRight";
        // Agent-owned instance names double as idempotency markers (R1).
        public const string MarkerLeft  = "[PICO_MCP] Hand Left";
        public const string MarkerRight = "[PICO_MCP] Hand Right";

        // Reflection targets so we never hard-depend on a specific PICO SDK
        // version / assembly (R3). PXR_ProjectSetting lives in ByteDance.PICO.XR.
        const string TypeName_PXR_ProjectSetting = "Unity.XR.PXR.PXR_ProjectSetting";
        const string TypeName_PXR_ProjectSetting_Alt = "ByteDance.PICO.XR.PXR_ProjectSetting";

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

        [MenuItem("PICO MCP/Hand/Ensure")]
        public static bool Ensure()
        {
            var origin = PXR_MCP_Common.EnsureXROrigin();
            if (origin == null) return false;

            var camOffset = origin.transform.Find(PXR_MCP_Common.CameraOffsetName);
            if (camOffset == null) { Debug.LogError("[PICO MCP] Camera Offset not found."); return false; }

            // Idempotent: if both hands already mounted, no-op.
            if (camOffset.Find(MarkerLeft) != null && camOffset.Find(MarkerRight) != null)
            {
                Debug.Log("[PICO MCP] Hand tracking already enabled.");
                return true;
            }

            var leftAsset  = LocateHandPrefab(HandLeftPrefabName);
            var rightAsset = LocateHandPrefab(HandRightPrefabName);
            if (leftAsset == null || rightAsset == null)
            {
                Debug.LogError("[PICO MCP] HandLeft/HandRight prefab not found in any pico-* package. Make sure com.bytedance.pico.xr is installed and contains Assets/Resources/Prefabs/HandLeft.prefab and HandRight.prefab.");
                return false;
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

            Debug.Log("[PICO MCP] Hand tracking enabled.");
            return true;
        }

        [MenuItem("PICO MCP/Hand/Remove")]
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
                if (t.name == MarkerLeft || t.name == MarkerRight) Undo.DestroyObjectImmediate(t.gameObject);
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
    }
}
