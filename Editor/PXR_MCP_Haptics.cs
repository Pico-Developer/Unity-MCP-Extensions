using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.MCPExtensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    public enum PXR_MCP_HapticsTarget
    {
        Left,
        Right,
        Both,
    }

    [Serializable]
    public class PXR_MCP_HapticsState
    {
        public string controller;
        public string path;
        public bool attached;
        public bool managedByMcp;
        public float amplitude;
        public int durationMs;
        public int frequencyHz;
        public string reason;
    }

    [Serializable]
    public class PXR_MCP_HapticsResult
    {
        public bool ok;
        public bool changed;
        public bool supported;
        public string reason;
        public string error;
        public List<PXR_MCP_HapticsState> controllers = new List<PXR_MCP_HapticsState>();
    }

    /// <summary>
    /// Editor-side lifecycle for the runtime haptics bridge. This class only
    /// attaches and configures components; it never chooses or subscribes to a
    /// gameplay event.
    /// </summary>
    public static class PXR_MCP_Haptics
    {
        public const float DefaultAmplitude = 0.5f;
        public const int DefaultDurationMs = 100;
        public const int DefaultFrequencyHz = 150;

        internal static bool? RuntimeSupportOverride;

        public static bool IsRuntimeSupported
        {
            get
            {
                if (RuntimeSupportOverride.HasValue) return RuntimeSupportOverride.Value;
#if ENABLE_PICO_XR_SDK
                return true;
#else
                return false;
#endif
            }
        }

        public static PXR_MCP_HapticsResult Attach(
            PXR_MCP_HapticsTarget target,
            float amplitude = DefaultAmplitude,
            int durationMs = DefaultDurationMs,
            int frequencyHz = DefaultFrequencyHz)
        {
            return ConfigureInternal(target, amplitude, durationMs, frequencyHz, createIfMissing: true);
        }

        public static PXR_MCP_HapticsResult Configure(
            PXR_MCP_HapticsTarget target,
            float? amplitude = null,
            int? durationMs = null,
            int? frequencyHz = null)
        {
            return ConfigureInternal(target, amplitude, durationMs, frequencyHz, createIfMissing: false);
        }

        public static PXR_MCP_HapticsResult Remove(PXR_MCP_HapticsTarget target)
        {
            var result = NewResult();
            if (!TryResolveTargets(target, out var targets, out result.error)) return result;

            foreach (var entry in targets)
            {
                var components = entry.gameObject.GetComponents<PXR_MCP_ControllerHaptics>();
                foreach (var component in components.Where(component => component.ManagedByMcp).ToArray())
                {
                    Undo.DestroyObjectImmediate(component);
                    result.changed = true;
                }
            }

            if (result.changed) MarkActiveSceneDirty();
            result.ok = true;
            result.controllers = ReadStates(targets);
            return result;
        }

        public static PXR_MCP_HapticsResult Status(PXR_MCP_HapticsTarget target)
        {
            var result = NewResult();
            if (!TryResolveTargets(target, out var targets, out var reason))
            {
                result.ok = true;
                result.reason = reason;
                return result;
            }
            result.controllers = ReadStates(targets);
            result.ok = true;
            return result;
        }

        static PXR_MCP_HapticsResult ConfigureInternal(
            PXR_MCP_HapticsTarget target,
            float? amplitude,
            int? durationMs,
            int? frequencyHz,
            bool createIfMissing)
        {
            var result = NewResult();
            if (!IsRuntimeSupported)
            {
                result.error = "Controller haptics require the PICO native runtime (ENABLE_PICO_XR_SDK).";
                return result;
            }

            if (!TryResolveTargets(target, out var targets, out result.error)) return result;

            // Validate the whole operation first so a `both` request cannot leave
            // only one side modified. A manually-authored component is never taken
            // over by MCP.
            foreach (var entry in targets)
            {
                var components = entry.gameObject.GetComponents<PXR_MCP_ControllerHaptics>();
                if (components.Length > 1 || components.Any(component => !component.ManagedByMcp))
                {
                    result.error = "Controller '" + entry.gameObject.name +
                                   "' contains a user-managed or duplicate haptics component; no changes made.";
                    return result;
                }

                if (!createIfMissing && components.Length == 0)
                {
                    result.error = "No MCP-managed haptics component is attached to '" +
                                   entry.gameObject.name + "'. Run action=attach first.";
                    return result;
                }

                var current = components.FirstOrDefault();
                var effectiveAmplitude = amplitude ?? (current != null ? current.Amplitude : DefaultAmplitude);
                var effectiveDuration = durationMs ?? (current != null ? current.DurationMs : DefaultDurationMs);
                var effectiveFrequency = frequencyHz ?? (current != null ? current.FrequencyHz : DefaultFrequencyHz);
                if (!PXR_MCP_ControllerHaptics.TryValidate(
                        effectiveAmplitude, effectiveDuration, effectiveFrequency, out result.error))
                    return result;
            }

            foreach (var entry in targets)
            {
                var component = entry.gameObject.GetComponent<PXR_MCP_ControllerHaptics>();
                if (component == null)
                {
                    component = Undo.AddComponent<PXR_MCP_ControllerHaptics>(entry.gameObject);
                    result.changed = true;
                }

                var effectiveAmplitude = amplitude ?? component.Amplitude;
                var effectiveDuration = durationMs ?? component.DurationMs;
                var effectiveFrequency = frequencyHz ?? component.FrequencyHz;
                if (component.Controller != entry.side ||
                    !Mathf.Approximately(component.Amplitude, effectiveAmplitude) ||
                    component.DurationMs != effectiveDuration ||
                    component.FrequencyHz != effectiveFrequency ||
                    !component.ManagedByMcp)
                {
                    Undo.RecordObject(component, "Configure PICO controller haptics");
                    component.Configure(entry.side, effectiveAmplitude, effectiveDuration, effectiveFrequency, true);
                    EditorUtility.SetDirty(component);
                    result.changed = true;
                }
            }

            if (result.changed) MarkActiveSceneDirty();
            result.ok = true;
            result.controllers = ReadStates(targets);
            return result;
        }

        static PXR_MCP_HapticsResult NewResult()
            => new PXR_MCP_HapticsResult { supported = IsRuntimeSupported };

        static bool TryResolveTargets(
            PXR_MCP_HapticsTarget target,
            out List<(GameObject gameObject, PXR_MCP_ControllerSide side)> targets,
            out string error)
        {
            targets = new List<(GameObject, PXR_MCP_ControllerSide)>();
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null)
            {
                error = "No agent XR Origin exists. Enable the Controller block first.";
                return false;
            }

            var cameraOffset = origin.transform.Find(PXR_MCP_Common.CameraOffsetName);
            if (cameraOffset == null)
            {
                error = "The agent XR Origin has no Camera Offset child.";
                return false;
            }

            if (target == PXR_MCP_HapticsTarget.Left || target == PXR_MCP_HapticsTarget.Both)
            {
                var left = cameraOffset.Find(PXR_MCP_Common.LeftControllerName);
                if (left == null)
                {
                    error = "The agent XR Origin has no Left Controller child.";
                    return false;
                }
                targets.Add((left.gameObject, PXR_MCP_ControllerSide.Left));
            }

            if (target == PXR_MCP_HapticsTarget.Right || target == PXR_MCP_HapticsTarget.Both)
            {
                var right = cameraOffset.Find(PXR_MCP_Common.RightControllerName);
                if (right == null)
                {
                    error = "The agent XR Origin has no Right Controller child.";
                    return false;
                }
                targets.Add((right.gameObject, PXR_MCP_ControllerSide.Right));
            }

            error = null;
            return true;
        }

        static List<PXR_MCP_HapticsState> ReadStates(
            IEnumerable<(GameObject gameObject, PXR_MCP_ControllerSide side)> targets)
        {
            return targets.Select(entry =>
            {
                var components = entry.gameObject.GetComponents<PXR_MCP_ControllerHaptics>();
                var component = components.FirstOrDefault(candidate => candidate.ManagedByMcp)
                                ?? components.FirstOrDefault();
                return new PXR_MCP_HapticsState
                {
                    controller = entry.side.ToString().ToLowerInvariant(),
                    path = HierarchyPath(entry.gameObject.transform),
                    attached = component != null,
                    managedByMcp = component != null && component.ManagedByMcp,
                    amplitude = component != null ? component.Amplitude : 0f,
                    durationMs = component != null ? component.DurationMs : 0,
                    frequencyHz = component != null ? component.FrequencyHz : 0,
                    reason = components.Length > 1 ? "multiple haptics components found" : null,
                };
            }).ToList();
        }

        static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        static void MarkActiveSceneDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
