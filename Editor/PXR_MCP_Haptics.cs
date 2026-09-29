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

using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.MCPExtensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    [Serializable]
    public class PXR_MCP_HapticsHandConfiguration
    {
        public float impulseAmplitude = PXR_MCP_Haptics.DefaultImpulseAmplitude;
        public int impulseDurationMs = PXR_MCP_Haptics.DefaultImpulseDurationMs;
        public int impulseFrequencyHz = PXR_MCP_Haptics.DefaultImpulseFrequencyHz;
        public string audioClipPath;
        public string phfTextPath;
        public PXR_MCP_HapticsChannelFlip channelFlip = PXR_MCP_HapticsChannelFlip.No;
        public PXR_MCP_HapticsCacheType cacheType = PXR_MCP_HapticsCacheType.DontCache;
        public float amplitudeScale = PXR_MCP_Haptics.DefaultAmplitudeScale;
    }

    [Serializable]
    public class PXR_MCP_HapticsEffectConfiguration
    {
        public string name = PXR_MCP_Haptics.DefaultEffectName;
        public PXR_MCP_HapticsEffectType effectType = PXR_MCP_HapticsEffectType.Impulse;
        public PXR_MCP_HapticsTarget target = PXR_MCP_HapticsTarget.Both;
        public bool setDefault = true;
        public PXR_MCP_HapticsHandConfiguration left = new PXR_MCP_HapticsHandConfiguration();
        public PXR_MCP_HapticsHandConfiguration right = new PXR_MCP_HapticsHandConfiguration();
    }

    [Serializable]
    public class PXR_MCP_HapticsHandState
    {
        public float impulseAmplitude;
        public int impulseDurationMs;
        public int impulseFrequencyHz;
        public string audioClipPath;
        public string phfTextPath;
        public string channelFlip;
        public string cacheType;
        public float amplitudeScale;
    }

    [Serializable]
    public class PXR_MCP_HapticsEffectState
    {
        public string name;
        public string effectType;
        public string target;
        public PXR_MCP_HapticsHandState left;
        public PXR_MCP_HapticsHandState right;
    }

    [Serializable]
    public class PXR_MCP_HapticsResult
    {
        public bool ok;
        public bool changed;
        public bool supported;
        public bool attached;
        public bool managedByMcp;
        public string managerPath;
        public string defaultEffect;
        public string reason;
        public string error;
        public List<PXR_MCP_HapticsEffectState> effects = new List<PXR_MCP_HapticsEffectState>();
    }

    /// <summary>
    /// Editor lifecycle for the one runtime manager on the agent XR Origin.
    /// It configures reusable named effects but never chooses a gameplay trigger.
    /// </summary>
    public static class PXR_MCP_Haptics
    {
        public const string DefaultEffectName = "Default";
        public const float DefaultImpulseAmplitude = 0.5f;
        public const int DefaultImpulseDurationMs = 100;
        public const int DefaultImpulseFrequencyHz = 150;
        public const float DefaultAmplitudeScale = 1f;

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

        public static PXR_MCP_HapticsResult Attach(PXR_MCP_HapticsEffectConfiguration configuration = null)
        {
            return UpsertInternal(configuration ?? new PXR_MCP_HapticsEffectConfiguration(), true);
        }

        public static PXR_MCP_HapticsResult UpsertEffect(PXR_MCP_HapticsEffectConfiguration configuration)
        {
            return UpsertInternal(configuration, false);
        }

        public static PXR_MCP_HapticsResult RemoveEffect(string effectName)
        {
            var result = NewResult();
            if (!TryFindManager(out var manager, out result.error)) return result;
            if (string.IsNullOrWhiteSpace(effectName))
            {
                result.error = "effectName is required.";
                return result;
            }

            Undo.RecordObject(manager, "Remove PICO haptics effect");
            result.changed = manager.RemoveEffect(effectName);
            if (!result.changed)
            {
                result.error = "Named haptics effect '" + effectName + "' was not found.";
                return result;
            }

            EditorUtility.SetDirty(manager);
            MarkActiveSceneDirty();
            result.ok = true;
            PopulateState(result, manager);
            return result;
        }

        public static PXR_MCP_HapticsResult Remove()
        {
            var result = NewResult();
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null)
            {
                result.ok = true;
                result.reason = "No agent XR Origin exists.";
                return result;
            }

            var managers = origin.GetComponents<PXR_MCP_HapticsManager>();
            foreach (var manager in managers.Where(candidate => candidate.ManagedByMcp).ToArray())
            {
                Undo.DestroyObjectImmediate(manager);
                result.changed = true;
            }
            if (result.changed) MarkActiveSceneDirty();
            result.ok = true;
            PopulateState(result, origin.GetComponent<PXR_MCP_HapticsManager>());
            return result;
        }

        public static PXR_MCP_HapticsResult Status()
        {
            var result = NewResult();
            var origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null)
            {
                result.ok = true;
                result.reason = "No agent XR Origin exists.";
                return result;
            }

            var managers = origin.GetComponents<PXR_MCP_HapticsManager>();
            result.ok = true;
            if (managers.Length > 1) result.reason = "Multiple haptics managers found on the agent XR Origin.";
            PopulateState(result, managers.FirstOrDefault(candidate => candidate.ManagedByMcp)
                                  ?? managers.FirstOrDefault());
            return result;
        }

        static PXR_MCP_HapticsResult UpsertInternal(
            PXR_MCP_HapticsEffectConfiguration configuration, bool createManager)
        {
            var result = NewResult();
            if (!IsRuntimeSupported)
            {
                result.error = "Controller haptics require the PICO native runtime (ENABLE_PICO_XR_SDK).";
                return result;
            }
            if (configuration == null)
            {
                result.error = "effect configuration is required.";
                return result;
            }
            if (!TryResolveOrigin(out var origin, out result.error)) return result;

            var managers = origin.GetComponents<PXR_MCP_HapticsManager>();
            if (managers.Length > 1 || managers.Any(manager => !manager.ManagedByMcp))
            {
                result.error = "The agent XR Origin contains a user-managed or duplicate haptics manager; no changes made.";
                return result;
            }

            var manager = managers.FirstOrDefault();
            if (manager == null && !createManager)
            {
                result.error = "No MCP-managed haptics manager is attached to the agent XR Origin. Run action=attach first.";
                return result;
            }

            if (!TryBuildEffect(configuration, out var effect, out result.error)) return result;
            if (manager == null)
            {
                manager = Undo.AddComponent<PXR_MCP_HapticsManager>(origin.gameObject);
                manager.ConfigureManagedByMcp(true);
                result.changed = true;
            }

            Undo.RecordObject(manager, "Configure PICO haptics manager");
            result.changed |= manager.UpsertEffect(effect, configuration.setDefault);
            if (!manager.ManagedByMcp) manager.ConfigureManagedByMcp(true);
            EditorUtility.SetDirty(manager);
            if (result.changed) MarkActiveSceneDirty();
            result.ok = true;
            PopulateState(result, manager);
            return result;
        }

        static bool TryBuildEffect(
            PXR_MCP_HapticsEffectConfiguration configuration,
            out PXR_MCP_HapticsEffect effect,
            out string error)
        {
            effect = null;
            if (string.IsNullOrWhiteSpace(configuration.name))
            {
                error = "effectName is required.";
                return false;
            }

            if (!TryBuildHand(configuration.effectType, PXR_MCP_HapticsTarget.Left,
                    configuration.target != PXR_MCP_HapticsTarget.Right, configuration.left,
                    out var left, out error) ||
                !TryBuildHand(configuration.effectType, PXR_MCP_HapticsTarget.Right,
                    configuration.target != PXR_MCP_HapticsTarget.Left, configuration.right,
                    out var right, out error))
                return false;

            effect = PXR_MCP_HapticsManager.CreateEffect(
                configuration.name.Trim(), configuration.effectType, configuration.target, left, right);
            return PXR_MCP_HapticsManager.TryValidate(effect, out error);
        }

        static bool TryBuildHand(
            PXR_MCP_HapticsEffectType type,
            PXR_MCP_HapticsTarget hand,
            bool active,
            PXR_MCP_HapticsHandConfiguration configuration,
            out PXR_MCP_HapticsHandSettings settings,
            out string error)
        {
            settings = null;
            configuration = configuration ?? new PXR_MCP_HapticsHandConfiguration();
            AudioClip audioClip = null;
            TextAsset phfText = null;
            if (active && type == PXR_MCP_HapticsEffectType.AudioClipBuffer &&
                !TryLoadAsset(configuration.audioClipPath, hand + " audioClipPath", out audioClip, out error))
                return false;
            if (active && type == PXR_MCP_HapticsEffectType.PhfBuffer &&
                !TryLoadAsset(configuration.phfTextPath, hand + " phfTextPath", out phfText, out error))
                return false;

            settings = PXR_MCP_HapticsManager.CreateHandSettings(
                configuration.impulseAmplitude, configuration.impulseDurationMs,
                configuration.impulseFrequencyHz, audioClip, phfText, configuration.channelFlip,
                configuration.cacheType, configuration.amplitudeScale);
            error = null;
            return true;
        }

        static bool TryLoadAsset<T>(string assetPath, string label, out T asset, out string error)
            where T : UnityEngine.Object
        {
            asset = null;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = label + " is required for this effect type.";
                return false;
            }
            asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                error = label + " does not resolve to a " + typeof(T).Name + " asset: " + assetPath;
                return false;
            }
            error = null;
            return true;
        }

        static bool TryResolveOrigin(out Unity.XR.CoreUtils.XROrigin origin, out string error)
        {
            origin = PXR_MCP_Common.FindAgentOrigin();
            if (origin == null)
            {
                error = "No agent XR Origin exists. Enable the Controller or XR Origin block first; haptics did not create one.";
                return false;
            }
            error = null;
            return true;
        }

        static bool TryFindManager(out PXR_MCP_HapticsManager manager, out string error)
        {
            manager = null;
            if (!TryResolveOrigin(out var origin, out error)) return false;
            var managers = origin.GetComponents<PXR_MCP_HapticsManager>();
            if (managers.Length != 1 || !managers[0].ManagedByMcp)
            {
                error = managers.Length == 0
                    ? "No MCP-managed haptics manager is attached to the agent XR Origin."
                    : "A user-managed or duplicate haptics manager prevents this operation.";
                return false;
            }
            manager = managers[0];
            return true;
        }

        static PXR_MCP_HapticsResult NewResult()
            => new PXR_MCP_HapticsResult { supported = IsRuntimeSupported };

        static void PopulateState(PXR_MCP_HapticsResult result, PXR_MCP_HapticsManager manager)
        {
            result.attached = manager != null;
            result.managedByMcp = manager != null && manager.ManagedByMcp;
            result.managerPath = manager != null ? HierarchyPath(manager.transform) : null;
            result.defaultEffect = manager != null ? manager.DefaultEffect : null;
            result.effects = manager == null
                ? new List<PXR_MCP_HapticsEffectState>()
                : manager.Effects.Where(effect => effect != null).Select(ToState).ToList();
        }

        static PXR_MCP_HapticsEffectState ToState(PXR_MCP_HapticsEffect effect)
        {
            return new PXR_MCP_HapticsEffectState
            {
                name = effect.Name,
                effectType = effect.EffectType.ToString(),
                target = effect.Target.ToString(),
                left = ToState(effect.Left),
                right = ToState(effect.Right),
            };
        }

        static PXR_MCP_HapticsHandState ToState(PXR_MCP_HapticsHandSettings settings)
        {
            return new PXR_MCP_HapticsHandState
            {
                impulseAmplitude = settings.ImpulseAmplitude,
                impulseDurationMs = settings.ImpulseDurationMs,
                impulseFrequencyHz = settings.ImpulseFrequencyHz,
                audioClipPath = settings.AudioClip != null ? AssetDatabase.GetAssetPath(settings.AudioClip) : null,
                phfTextPath = settings.PhfText != null ? AssetDatabase.GetAssetPath(settings.PhfText) : null,
                channelFlip = settings.ChannelFlip.ToString(),
                cacheType = settings.CacheType.ToString(),
                amplitudeScale = settings.AmplitudeScale,
            };
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
