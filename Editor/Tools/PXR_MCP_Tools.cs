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

// PXR_MCP_Tools.cs
// Step 2: PICO XR feature surface exposed as Unity MCP tools.
//
// Architecture:
//   * One [McpTool] static method per PICO feature block.
//   * Each tool takes a typed `<Tool>Params` object whose Action property is
//     constrained by an enum (so the generated JSON schema lists the verbs).
//   * Each tool calls into the Step 1 layer (ByteDance.PICO.MCPExtensions.Editor)
//     and wraps the raw POCO result in a PXR_MCP_Result envelope.
//   * Any thrown exception is caught at the tool boundary and converted to
//     PXR_MCP_Result.FromException — the MCP bridge never sees a crash.
//
// Compilation:
//   The asmdef references Unity.AI.MCP.Editor directly, so compiling at all
//   implicitly requires AI Assistant 2.x to be installed in the project. If
//   the reference cannot be resolved, the asmdef won't compile and you'll
//   see a clear error in the Console — better than silently dropping the
//   tools the way `defineConstraints` would.

using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.MCPExtensions.Editor;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Tools
{
    public static class PXR_MCP_Tools
    {
        // =============================================================
        // pico_xr_vst
        // =============================================================
        public enum VstAction { Enable, Disable, Status }

        public class VstParams
        {
            [McpDescription("Operation to perform on the PICO Video See-Through block.",
                Required = true, EnumType = typeof(VstAction))]
            public string Action { get; set; }
        }

        [McpTool("pico_xr_vst",
            "Enable, disable, or query PICO Video See-Through (passthrough) on the agent XR Origin.")]
        public static object PicoXrVst(VstParams p)
        {
            try
            {
                switch (ParseEnum<VstAction>(p?.Action))
                {
                    case VstAction.Enable:
                    {
                        var ok = PXR_MCP_VST.Ensure();
                        return ok
                            ? PXR_MCP_Result.Ok("VST enabled on the agent XR Origin.")
                            : PXR_MCP_Result.Error("Failed to enable VST.", "PXR_MCP_VST.Ensure returned false; see Unity Console for details.");
                    }
                    case VstAction.Disable:
                        PXR_MCP_VST.Remove();
                        return PXR_MCP_Result.Ok("VST removed from the agent XR Origin.");
                    case VstAction.Status:
                    {
                        var info = ProbeVstStatus();
                        return PXR_MCP_Result.Ok(
                            info.installed ? "VST is currently enabled." : "VST is not enabled.", info);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.", "action must be one of: enable, disable, status");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_vst", e); }
        }

        // =============================================================
        // pico_xr_controller
        // =============================================================
        public enum ControllerAction { Enable, Disable, Status }

        public class ControllerParams
        {
            [McpDescription("Operation to perform on the PICO Controller visuals block.",
                Required = true, EnumType = typeof(ControllerAction))]
            public string Action { get; set; }
        }

        [McpTool("pico_xr_controller",
            "Enable, disable, or query the PICO controller visual models on the agent XR Origin.")]
        public static object PicoXrController(ControllerParams p)
        {
            try
            {
                switch (ParseEnum<ControllerAction>(p?.Action))
                {
                    case ControllerAction.Enable:
                    {
                        var ok = PXR_MCP_Controller.Ensure();
                        return ok
                            ? PXR_MCP_Result.Ok("PICO controller models mounted on Left/Right hand anchors.")
                            : PXR_MCP_Result.Error("Failed to mount controllers.", "PXR_MCP_Controller.Ensure returned false; see Unity Console.");
                    }
                    case ControllerAction.Disable:
                        PXR_MCP_Controller.Remove();
                        return PXR_MCP_Result.Ok("PICO controller models removed.");
                    case ControllerAction.Status:
                    {
                        var info = ProbeControllerStatus();
                        return PXR_MCP_Result.Ok(
                            info.installed ? "PICO controllers are mounted." : "PICO controllers are not mounted.", info);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.", "action must be one of: enable, disable, status");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_controller", e); }
        }

        // =============================================================
        // pico_xr_locomotion
        // =============================================================
        public enum LocomotionAction { Enable, Disable, Configure, Status }

        public class LocomotionParams
        {
            [McpDescription("Operation to perform on the Locomotion root.",
                Required = true, EnumType = typeof(LocomotionAction))]
            public string Action { get; set; }

            [McpDescription("For action=configure: comma-separated subset of {Move, Turn, Teleportation, GrabMove, Climb, Gravity, Jump, Default, All, None}. " +
                            "Default = Move,Turn,Teleportation,Gravity. Case-insensitive.")]
            public string Presets { get; set; }
        }

        [McpTool("pico_xr_locomotion",
            "Enable, disable, or configure the Locomotion root on the agent XR Origin. " +
            "Locomotion is toggled by SetActive on the Locomotion root; configure further toggles individual child blocks.")]
        public static object PicoXrLocomotion(LocomotionParams p)
        {
            try
            {
                switch (ParseEnum<LocomotionAction>(p?.Action))
                {
                    case LocomotionAction.Enable:
                    {
                        var ok = PXR_MCP_Locomotion.Enable();
                        return ok
                            ? PXR_MCP_Result.Ok("Locomotion root activated.")
                            : PXR_MCP_Result.Error("Failed to enable Locomotion.", "PXR_MCP_Locomotion.Enable returned false; see Unity Console.");
                    }
                    case LocomotionAction.Disable:
                    {
                        var ok = PXR_MCP_Locomotion.Disable();
                        return ok
                            ? PXR_MCP_Result.Ok("Locomotion root deactivated.")
                            : PXR_MCP_Result.Error("Failed to disable Locomotion.", "PXR_MCP_Locomotion.Disable returned false; see Unity Console.");
                    }
                    case LocomotionAction.Configure:
                    {
                        var flags = ParseLocomotionFlags(p.Presets);
                        var ok = PXR_MCP_Locomotion.Configure(flags);
                        return ok
                            ? PXR_MCP_Result.Ok("Locomotion configured: " + flags + ".", new { flags = flags.ToString() })
                            : PXR_MCP_Result.Error("Failed to configure Locomotion.", "PXR_MCP_Locomotion.Configure returned false; see Unity Console.");
                    }
                    case LocomotionAction.Status:
                    {
                        var info = ProbeLocomotionStatus();
                        return PXR_MCP_Result.Ok(
                            info.active ? "Locomotion root is active." : "Locomotion root is inactive.", info);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.", "action must be one of: enable, disable, configure, status");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_locomotion", e); }
        }

        // =============================================================
        // pico_xr_spatial_mesh
        // =============================================================
        public enum SpatialMeshAction { Enable, Disable, Status }

        public class SpatialMeshParams
        {
            [McpDescription("Operation to perform on the PICO Spatial Mesh block.",
                Required = true, EnumType = typeof(SpatialMeshAction))]
            public string Action { get; set; }
        }

        [McpTool("pico_xr_spatial_mesh",
            "Enable, disable, or query the PICO Spatial Mesh on the agent XR Origin. " +
            "Spatial Mesh depends on VST: enable VST first if it is not on. " +
            "Enable is TWO-PHASE: the first call copies the bundled SpatialMeshManager driver + " +
            "shaders/materials/prefab into the project (Assets/PICO_MCP/SpatialMesh), which triggers " +
            "an Editor recompile. When the response reports an import/recompile in progress, settle-loop " +
            "on pico_xr_status until the MCP bridge returns, then call enable again to mount and configure the driver.")]
        public static object PicoXrSpatialMesh(SpatialMeshParams p)
        {
            try
            {
                switch (ParseEnum<SpatialMeshAction>(p?.Action))
                {
                    case SpatialMeshAction.Enable:
                    {
                        var outcome = PXR_MCP_SpatialMesh.Ensure(out var detail);
                        switch (outcome)
                        {
                            case PXR_MCP_SpatialMesh.EnsureOutcome.Configured:
                                return PXR_MCP_Result.Ok(
                                    "Spatial Mesh enabled: SpatialMeshManager mounted and configured.",
                                    new { detail });
                            case PXR_MCP_SpatialMesh.EnsureOutcome.ImportingRecompile:
                                // Assets landed; the Editor is (re)compiling the driver. This is NOT an
                                // error -- the caller must settle-loop on pico_xr_status and re-enable.
                                return PXR_MCP_Result.Skipped(
                                    "Spatial Mesh assets imported; Editor is recompiling. " +
                                    "Poll pico_xr_status until the bridge returns, then call enable again.",
                                    detail, new { recompiling = true });
                            default:
                                return PXR_MCP_Result.Error("Failed to enable Spatial Mesh.", detail);
                        }
                    }
                    case SpatialMeshAction.Disable:
                        PXR_MCP_SpatialMesh.Remove();
                        return PXR_MCP_Result.Ok("Spatial Mesh container removed.");
                    case SpatialMeshAction.Status:
                    {
                        var info = ProbeSpatialMeshStatus();
                        return PXR_MCP_Result.Ok(
                            info.installed ? "Spatial Mesh is enabled." : "Spatial Mesh is not enabled.", info);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.", "action must be one of: enable, disable, status");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_spatial_mesh", e); }
        }

        // =============================================================
        // pico_xr_hand
        // =============================================================
        public enum HandAction { Enable, Disable, Status }

        public class HandParams
        {
            [McpDescription("Operation to perform on the PICO hand-tracking (virtual hands) block.",
                Required = true, EnumType = typeof(HandAction))]
            public string Action { get; set; }
        }

        [McpTool("pico_xr_hand",
            "Enable, disable, or query PICO hand tracking (virtual hands) on the agent XR Origin. " +
            "Mounts the PICO HandLeft/HandRight models under Camera Offset and enables the hand-tracking project setting.")]
        public static object PicoXrHand(HandParams p)
        {
            try
            {
                switch (ParseEnum<HandAction>(p?.Action))
                {
                    case HandAction.Enable:
                    {
                        var ok = PXR_MCP_Hand.Ensure();
                        return ok
                            ? PXR_MCP_Result.Ok("PICO hand models mounted on the agent XR Origin; hand tracking enabled.")
                            : PXR_MCP_Result.Error("Failed to enable hand tracking.", "PXR_MCP_Hand.Ensure returned false (PICO hand prefabs missing?); see Unity Console.");
                    }
                    case HandAction.Disable:
                        PXR_MCP_Hand.Remove();
                        return PXR_MCP_Result.Ok("PICO hand models removed.");
                    case HandAction.Status:
                    {
                        var info = ProbeHandStatus();
                        return PXR_MCP_Result.Ok(
                            info.installed ? "PICO hand tracking is enabled." : "PICO hand tracking is not enabled.", info);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.", "action must be one of: enable, disable, status");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_hand", e); }
        }

        // =============================================================
        // pico_xr_package
        // =============================================================
        public enum PackageAction { List, Info, Add, Remove, Update, ListSamples, ImportSample }

        public class PackageParams
        {
            [McpDescription("Operation to perform on Unity Package Manager.",
                Required = true, EnumType = typeof(PackageAction))]
            public string Action { get; set; }

            [McpDescription("For action=add: package identifier accepted by Client.Add (e.g. 'com.unity.xr.hands' or 'com.unity.xr.hands@1.5.0' or a git URL).")]
            public string Identifier { get; set; }

            [McpDescription("For action=info/remove/update/list_samples/import_sample: the package name (e.g. 'com.unity.xr.interaction.toolkit').")]
            public string PackageName { get; set; }

            [McpDescription("For action=update: target version (empty = latest stable). For list_samples/import_sample: optional version override (default = installed version).")]
            public string Version { get; set; }

            [McpDescription("For action=import_sample: the displayName of the sample, e.g. 'Starter Assets'.")]
            public string SampleName { get; set; }

            [McpDescription("For action=import_sample: when true, re-imports even if the target folder already exists.", Default = false)]
            public bool Overwrite { get; set; }
        }

        [McpTool("pico_xr_package",
            "Manage Unity packages and their samples (install / remove / update / query / list samples / import sample). " +
            "Designed for XRI, XR Hands, and any other Unity package needed by PICO workflows. Idempotent: re-running with the same arguments is a safe no-op.")]
        public static object PicoXrPackage(PackageParams p)
        {
            try
            {
                switch (ParseEnum<PackageAction>(p?.Action))
                {
                    case PackageAction.List:
                    {
                        var all = PXR_MCP_PackageOps.ListInstalled();
                        return PXR_MCP_Result.Ok(all.Count + " package(s) installed.", new { packages = all });
                    }
                    case PackageAction.Info:
                    {
                        if (string.IsNullOrWhiteSpace(p.PackageName))
                            return PXR_MCP_Result.Error("Missing packageName.", "packageName is required for action=info");
                        var info = PXR_MCP_PackageOps.GetInstalled(p.PackageName);
                        return info != null
                            ? PXR_MCP_Result.Ok(p.PackageName + " is installed at version " + info.version + ".", info)
                            : PXR_MCP_Result.Skipped(p.PackageName + " is not installed.",
                                "package '" + p.PackageName + "' is not present in this project", null);
                    }
                    case PackageAction.Add:
                    {
                        var id = string.IsNullOrWhiteSpace(p.Identifier) ? p.PackageName : p.Identifier;
                        if (string.IsNullOrWhiteSpace(id))
                            return PXR_MCP_Result.Error("Missing identifier.", "identifier or packageName is required for action=add");
                        var r = PXR_MCP_PackageOps.Add(id);
                        return PackageResultToEnvelope(r, "add", id);
                    }
                    case PackageAction.Remove:
                    {
                        if (string.IsNullOrWhiteSpace(p.PackageName))
                            return PXR_MCP_Result.Error("Missing packageName.", "packageName is required for action=remove");
                        var r = PXR_MCP_PackageOps.Remove(p.PackageName);
                        return PackageResultToEnvelope(r, "remove", p.PackageName);
                    }
                    case PackageAction.Update:
                    {
                        if (string.IsNullOrWhiteSpace(p.PackageName))
                            return PXR_MCP_Result.Error("Missing packageName.", "packageName is required for action=update");
                        var r = PXR_MCP_PackageOps.Update(p.PackageName, p.Version);
                        return PackageResultToEnvelope(r, "update", p.PackageName + (string.IsNullOrEmpty(p.Version) ? "" : "@" + p.Version));
                    }
                    case PackageAction.ListSamples:
                    {
                        if (string.IsNullOrWhiteSpace(p.PackageName))
                            return PXR_MCP_Result.Error("Missing packageName.", "packageName is required for action=list_samples");
                        var samples = PXR_MCP_PackageOps.ListSamples(p.PackageName, p.Version);
                        return PXR_MCP_Result.Ok(
                            samples.Count + " sample(s) declared by " + p.PackageName + ".",
                            new { samples });
                    }
                    case PackageAction.ImportSample:
                    {
                        if (string.IsNullOrWhiteSpace(p.PackageName) || string.IsNullOrWhiteSpace(p.SampleName))
                            return PXR_MCP_Result.Error("Missing packageName or sampleName.",
                                "both packageName and sampleName are required for action=import_sample");
                        var r = PXR_MCP_PackageOps.ImportSample(p.PackageName, p.SampleName, p.Overwrite, p.Version);
                        return SampleImportResultToEnvelope(r);
                    }
                }
                return PXR_MCP_Result.Error("Unknown action.",
                    "action must be one of: list, info, add, remove, update, list_samples, import_sample");
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_package", e); }
        }

        // =============================================================
        // pico_xr_status (aggregate)
        // =============================================================
        public class StatusParams { /* no parameters */ }

        [McpTool("pico_xr_status",
            "Return a snapshot of all PICO XR blocks (VST, Controller, Locomotion, Spatial Mesh, Hand) plus the camera invariant on the agent XR Origin.")]
        public static object PicoXrStatus(StatusParams _)
        {
            try
            {
                var details = new
                {
                    vst          = ProbeVstStatus(),
                    controller   = ProbeControllerStatus(),
                    locomotion   = ProbeLocomotionStatus(),
                    spatial_mesh = ProbeSpatialMeshStatus(),
                    hand         = ProbeHandStatus(),
                    camera       = ProbeCameraStatus(),
                };
                return PXR_MCP_Result.Ok("PICO XR status snapshot collected.", details);
            }
            catch (Exception e) { return PXR_MCP_Result.FromException("pico_xr_status", e); }
        }

        // =============================================================
        // Status probes (no Step 1 modification; rely on marker objects)
        // =============================================================
        class BlockStatus { public bool installed; public string reason; }
        class LocomotionStatus { public bool active; public List<string> activeChildren; public string reason; }
        class CameraStatus
        {
            public int activeCameras;        // cameras currently active-and-enabled in the scene
            public int managedDisabled;      // foreign cameras WE disabled to keep the invariant
            public bool single;              // true when exactly one active camera remains
            public string reason;
        }

        static BlockStatus ProbeVstStatus()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return new BlockStatus { installed = false, reason = "no agent XR Origin in scene" };
            return new BlockStatus
            {
                installed = origin.transform.Find(PXR_MCP_VST.MarkerChild) != null,
            };
        }

        static BlockStatus ProbeControllerStatus()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return new BlockStatus { installed = false, reason = "no agent XR Origin in scene" };

            // Controller module is "installed" when PICO controller-model markers are
            // present under Left/Right Controller. Additionally surface the module's
            // raw visibility (Left/Right Controller GO activeSelf) so the agent can
            // distinguish "PICO mounted" vs "module re-enabled by hand".
            bool markersFound = false;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == PXR_MCP_Controller.MarkerLeft || t.name == PXR_MCP_Controller.MarkerRight) { markersFound = true; break; }
            }
            var moduleActive = PXR_MCP_Common.IsControllerModuleActive(origin.gameObject);
            string reason = null;
            if (!markersFound && moduleActive) reason = "Controller GameObjects active but no PICO markers mounted yet";
            if (!markersFound && !moduleActive) reason = "Controller module hidden by initial-hide policy";
            return new BlockStatus { installed = markersFound, reason = reason };
        }

        static LocomotionStatus ProbeLocomotionStatus()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return new LocomotionStatus { active = false, reason = "no agent XR Origin in scene" };
            var root = origin.transform.Find(PXR_MCP_Common.LocomotionRootName);
            if (root == null) return new LocomotionStatus { active = false, reason = "Locomotion root child not found" };
            var children = new List<string>();
            foreach (Transform c in root)
                if (c.gameObject.activeSelf) children.Add(c.name);
            return new LocomotionStatus { active = root.gameObject.activeSelf, activeChildren = children };
        }

        static BlockStatus ProbeSpatialMeshStatus()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return new BlockStatus { installed = false, reason = "no agent XR Origin in scene" };

            var container = origin.transform.Find(PXR_MCP_SpatialMesh.ContainerName);
            if (container == null)
                return new BlockStatus { installed = false, reason = "Spatial Mesh container not present" };

            // "installed" == the SpatialMeshManager driver is actually mounted on the container.
            // A bare container without the driver means enable is mid-flight (assets imported,
            // driver not yet compiled/mounted) -> report not-installed with a settle hint.
            var driverType = PXR_MCP_Common.FindLoadedType(PXR_MCP_SpatialMesh.DriverTypeName);
            if (driverType == null)
                return new BlockStatus
                {
                    installed = false,
                    reason = "container present but SpatialMeshManager type not loaded yet " +
                             "(assets importing / Editor recompiling, or PICO XR SDK not installed)",
                };

            var mounted = container.GetComponent(driverType) != null;
            return new BlockStatus
            {
                installed = mounted,
                reason = mounted ? null : "container present but SpatialMeshManager not mounted; call enable again",
            };
        }

        static BlockStatus ProbeHandStatus()
        {
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null) return new BlockStatus { installed = false, reason = "no agent XR Origin in scene" };

            // Hand block is "installed" when BOTH hand markers are mounted under the origin.
            bool left = false, right = false;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == PXR_MCP_Hand.MarkerLeft)  left  = true;
                if (t.name == PXR_MCP_Hand.MarkerRight) right = true;
            }
            bool installed = left && right;
            string reason = null;
            if (!installed && (left || right)) reason = "only one hand mounted; expected both Left and Right";
            return new BlockStatus { installed = installed, reason = reason };
        }

        // Camera invariant probe: how many cameras actually render right now, and
        // how many foreign cameras we are holding disabled to keep it at one.
        static CameraStatus ProbeCameraStatus()
        {
            var active = PXR_MCP_Common.CountActiveSceneCameras();
            var managed = PXR_MCP_Common.CountManagedDisabledCameras();
            string reason = null;
            if (active == 0) reason = "no active camera in scene (agent XR Origin not created yet?)";
            else if (active > 1) reason = active + " active cameras — multi-camera render conflict; enable a block or run Enforce Single Active Camera";
            return new CameraStatus
            {
                activeCameras = active,
                managedDisabled = managed,
                single = active == 1,
                reason = reason,
            };
        }

        // =============================================================
        // Conversion helpers
        // =============================================================
        static PXR_MCP_Result PackageResultToEnvelope(PXR_MCP_PackageOps.PackageResult r, string verb, string idForSummary)
        {
            if (r == null) return PXR_MCP_Result.Error(verb + " " + idForSummary + " returned null.", "null result");
            if (!r.ok) return PXR_MCP_Result.Error("Package " + verb + " failed for " + idForSummary + ".", r.error, r);
            if (r.alreadyPresent)
                return PXR_MCP_Result.AlreadyPresent(
                    r.packageName + " was already at " + r.version + "; no-op.", r);
            if (verb == "remove")
                return PXR_MCP_Result.Ok(
                    r.packageName + " removed" + (r.previousVersion != null ? " (was " + r.previousVersion + ")" : "") + ".", r);
            return PXR_MCP_Result.Ok(
                r.packageName + " " + verb + "d at " + r.version +
                (r.previousVersion != null ? " (was " + r.previousVersion + ")" : "") + ".", r);
        }

        static PXR_MCP_Result SampleImportResultToEnvelope(PXR_MCP_PackageOps.SampleImportResult r)
        {
            if (r == null) return PXR_MCP_Result.Error("Sample import returned null.", "null result");
            if (r.skipped) return PXR_MCP_Result.Skipped(
                "Sample import skipped: " + r.packageName + " :: " + r.sampleName + ".", r.warning, r);
            if (!r.ok) return PXR_MCP_Result.Error(
                "Sample import failed: " + r.packageName + " :: " + r.sampleName + ".", r.error, r);
            if (r.alreadyPresent) return PXR_MCP_Result.AlreadyPresent(
                "Sample already imported at " + r.importPath + ".", r);
            return PXR_MCP_Result.Ok(
                "Sample imported to " + r.importPath + ".", r);
        }

        // =============================================================
        // Enum parsing
        // =============================================================
        static T ParseEnum<T>(string raw) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("action is required");
            // Accept both snake_case ("import_sample") and PascalCase ("ImportSample").
            var normalized = raw.Replace("_", "").Replace("-", "").Replace(" ", "");
            foreach (var name in Enum.GetNames(typeof(T)))
            {
                if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
                    return (T)Enum.Parse(typeof(T), name);
            }
            throw new ArgumentException("invalid action '" + raw + "' for " + typeof(T).Name +
                                        "; allowed: " + string.Join(", ", Enum.GetNames(typeof(T))));
        }

        static LocomotionFlags ParseLocomotionFlags(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return LocomotionFlags.Default;
            LocomotionFlags acc = LocomotionFlags.None;
            foreach (var tok in raw.Split(new[] { ',', '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (Enum.TryParse<LocomotionFlags>(t, ignoreCase: true, out var f)) acc |= f;
                else throw new ArgumentException("unknown locomotion preset '" + t + "'");
            }
            return acc;
        }
    }
}
