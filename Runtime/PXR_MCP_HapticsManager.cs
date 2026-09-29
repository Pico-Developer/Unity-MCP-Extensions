using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace ByteDance.PICO.MCPExtensions
{
    public enum PXR_MCP_HapticsTarget
    {
        Left,
        Right,
        Both,
    }

    public enum PXR_MCP_HapticsEffectType
    {
        Impulse,
        AudioClipBuffer,
        PhfBuffer,
    }

    public enum PXR_MCP_HapticsChannelFlip
    {
        No,
        Yes,
    }

    public enum PXR_MCP_HapticsCacheType
    {
        DontCache,
        CacheAndVibrate,
        CacheNoVibrate,
    }

    [Serializable]
    public sealed class PXR_MCP_HapticsEvent : UnityEvent<string, string> { }

    [Serializable]
    public sealed class PXR_MCP_HapticsHandSettings
    {
        [SerializeField, Range(0f, 1f), Tooltip("Impulse amplitude. Valid range: 0 to 1.")]
        float m_ImpulseAmplitude = 0.5f;

        [SerializeField, Tooltip("Impulse duration in milliseconds. Valid range: 0 to 65535.")]
        int m_ImpulseDurationMs = 100;

        [SerializeField, Tooltip("Impulse frequency in hertz. Valid range: 50 to 500.")]
        int m_ImpulseFrequencyHz = 150;

        [SerializeField, Tooltip("AudioClip used when Effect Type is Audio Clip Buffer.")]
        AudioClip m_AudioClip;

        [SerializeField, Tooltip("PHF JSON TextAsset used when Effect Type is PHF Buffer.")]
        TextAsset m_PhfText;

        [SerializeField, Tooltip("Whether the left and right audio channels are swapped.")]
        PXR_MCP_HapticsChannelFlip m_ChannelFlip = PXR_MCP_HapticsChannelFlip.No;

        [SerializeField, Tooltip("Audio buffer cache and initial playback behavior. PHF does not use this setting.")]
        PXR_MCP_HapticsCacheType m_CacheType = PXR_MCP_HapticsCacheType.DontCache;

        [SerializeField, Range(0f, 2f), Tooltip("Buffered-haptics amplitude scale. Valid range: 0 to 2.")]
        float m_AmplitudeScale = 1f;

        public float ImpulseAmplitude => m_ImpulseAmplitude;
        public int ImpulseDurationMs => m_ImpulseDurationMs;
        public int ImpulseFrequencyHz => m_ImpulseFrequencyHz;
        public AudioClip AudioClip => m_AudioClip;
        public TextAsset PhfText => m_PhfText;
        public PXR_MCP_HapticsChannelFlip ChannelFlip => m_ChannelFlip;
        public PXR_MCP_HapticsCacheType CacheType => m_CacheType;
        public float AmplitudeScale => m_AmplitudeScale;

        internal PXR_MCP_HapticsHandSettings Clone()
        {
            var clone = new PXR_MCP_HapticsHandSettings();
            clone.Configure(
                m_ImpulseAmplitude, m_ImpulseDurationMs, m_ImpulseFrequencyHz,
                m_AudioClip, m_PhfText, m_ChannelFlip, m_CacheType, m_AmplitudeScale);
            return clone;
        }

        internal void Configure(
            float impulseAmplitude,
            int impulseDurationMs,
            int impulseFrequencyHz,
            AudioClip audioClip,
            TextAsset phfText,
            PXR_MCP_HapticsChannelFlip channelFlip,
            PXR_MCP_HapticsCacheType cacheType,
            float amplitudeScale)
        {
            m_ImpulseAmplitude = impulseAmplitude;
            m_ImpulseDurationMs = impulseDurationMs;
            m_ImpulseFrequencyHz = impulseFrequencyHz;
            m_AudioClip = audioClip;
            m_PhfText = phfText;
            m_ChannelFlip = channelFlip;
            m_CacheType = cacheType;
            m_AmplitudeScale = amplitudeScale;
        }

        internal bool ConfigurationEquals(PXR_MCP_HapticsHandSettings other)
        {
            return other != null &&
                   Mathf.Approximately(m_ImpulseAmplitude, other.m_ImpulseAmplitude) &&
                   m_ImpulseDurationMs == other.m_ImpulseDurationMs &&
                   m_ImpulseFrequencyHz == other.m_ImpulseFrequencyHz &&
                   m_AudioClip == other.m_AudioClip &&
                   m_PhfText == other.m_PhfText &&
                   m_ChannelFlip == other.m_ChannelFlip &&
                   m_CacheType == other.m_CacheType &&
                   Mathf.Approximately(m_AmplitudeScale, other.m_AmplitudeScale);
        }
    }

    [Serializable]
    public sealed class PXR_MCP_HapticsEffect
    {
        [SerializeField, Tooltip("Unique name used by scripts and UnityEvents.")]
        string m_Name = "Default";

        [SerializeField]
        PXR_MCP_HapticsEffectType m_EffectType = PXR_MCP_HapticsEffectType.Impulse;

        [SerializeField]
        PXR_MCP_HapticsTarget m_Target = PXR_MCP_HapticsTarget.Both;

        [SerializeField, Tooltip("Parameters and source asset used by the left controller.")]
        PXR_MCP_HapticsHandSettings m_Left = new PXR_MCP_HapticsHandSettings();

        [SerializeField, Tooltip("Parameters and source asset used by the right controller.")]
        PXR_MCP_HapticsHandSettings m_Right = new PXR_MCP_HapticsHandSettings();

        public string Name => m_Name;
        public PXR_MCP_HapticsEffectType EffectType => m_EffectType;
        public PXR_MCP_HapticsTarget Target => m_Target;
        public PXR_MCP_HapticsHandSettings Left => m_Left;
        public PXR_MCP_HapticsHandSettings Right => m_Right;

        internal PXR_MCP_HapticsEffect Clone()
        {
            var clone = new PXR_MCP_HapticsEffect();
            clone.Configure(m_Name, m_EffectType, m_Target, m_Left, m_Right);
            return clone;
        }

        internal void Configure(
            string name,
            PXR_MCP_HapticsEffectType effectType,
            PXR_MCP_HapticsTarget target,
            PXR_MCP_HapticsHandSettings left,
            PXR_MCP_HapticsHandSettings right)
        {
            m_Name = name;
            m_EffectType = effectType;
            m_Target = target;
            m_Left = left != null ? left.Clone() : new PXR_MCP_HapticsHandSettings();
            m_Right = right != null ? right.Clone() : new PXR_MCP_HapticsHandSettings();
        }

        internal bool ConfigurationEquals(PXR_MCP_HapticsEffect other)
        {
            return other != null &&
                   string.Equals(m_Name, other.m_Name, StringComparison.Ordinal) &&
                   m_EffectType == other.m_EffectType &&
                   m_Target == other.m_Target &&
                   m_Left.ConfigurationEquals(other.m_Left) &&
                   m_Right.ConfigurationEquals(other.m_Right);
        }
    }

    internal interface IPXR_MCP_HapticsApi
    {
        void SendImpulse(PXR_MCP_HapticsTarget target, float amplitude, int durationMs, int frequencyHz);
        int SendAudio(
            PXR_MCP_HapticsTarget target, AudioClip audioClip, PXR_MCP_HapticsChannelFlip channelFlip,
            ref int sourceId, PXR_MCP_HapticsCacheType cacheType);
        int SendPcm(
            PXR_MCP_HapticsTarget target, float[] pcmData, int bufferSize, int sampleRateHz, int channelCount,
            PXR_MCP_HapticsChannelFlip channelFlip, ref int sourceId, PXR_MCP_HapticsCacheType cacheType);
        int SendPhf(
            PXR_MCP_HapticsTarget target, TextAsset phfText, PXR_MCP_HapticsChannelFlip channelFlip,
            float amplitudeScale, ref int sourceId);
        int Start(int sourceId);
        int Pause(int sourceId);
        int Resume(int sourceId);
        int Update(
            int sourceId, PXR_MCP_HapticsTarget target, PXR_MCP_HapticsChannelFlip channelFlip,
            float amplitudeScale);
        int Stop(int sourceId, bool clearCache);
    }

    /// <summary>
    /// Runtime controller-haptics manager for the agent XR Origin. It stores named
    /// effects but deliberately owns no gameplay trigger: scripts and UnityEvents
    /// decide when to call Send, Start, Pause, Resume, Update, or Stop.
    /// </summary>
    [AddComponentMenu("PICO MCP/Haptics Manager")]
    [DisallowMultipleComponent]
    public sealed class PXR_MCP_HapticsManager : MonoBehaviour
    {
        public const float MinImpulseAmplitude = 0f;
        public const float MaxImpulseAmplitude = 1f;
        public const int MinImpulseDurationMs = 0;
        public const int MaxImpulseDurationMs = 65535;
        public const int MinImpulseFrequencyHz = 50;
        public const int MaxImpulseFrequencyHz = 500;
        public const float MinBufferAmplitudeScale = 0f;
        public const float MaxBufferAmplitudeScale = 2f;

        [SerializeField, Tooltip("Effect used by parameterless lifecycle methods.")]
        string m_DefaultEffect = "Default";

        [SerializeField]
        List<PXR_MCP_HapticsEffect> m_Effects = new List<PXR_MCP_HapticsEffect>();

        [SerializeField, HideInInspector]
        bool m_ManagedByMcp;

        [Header("Lifecycle callbacks (effect name, target/detail)")]
        [SerializeField] PXR_MCP_HapticsEvent m_OnRequested = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnSent = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnSourceCreated = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnStarted = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnPaused = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnResumed = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnUpdated = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnStopped = new PXR_MCP_HapticsEvent();
        [SerializeField] PXR_MCP_HapticsEvent m_OnFailed = new PXR_MCP_HapticsEvent();

        sealed class SourceState
        {
            public PXR_MCP_HapticsTarget target;
            public int leftSourceId;
            public int rightSourceId;
            public PXR_MCP_HapticsChannelFlip leftChannelFlip;
            public PXR_MCP_HapticsChannelFlip rightChannelFlip;
            public float leftAmplitudeScale = 1f;
            public float rightAmplitudeScale = 1f;
        }

        static PXR_MCP_HapticsManager s_Instance;
        readonly Dictionary<string, SourceState> m_Sources =
            new Dictionary<string, SourceState>(StringComparer.Ordinal);

        internal static IPXR_MCP_HapticsApi ApiOverride;
#if UNITY_INCLUDE_TESTS
        internal static bool? RuntimeSupportOverride;
#endif

        public static PXR_MCP_HapticsManager Instance => s_Instance;
        public static bool IsRuntimeSupported
        {
            get
            {
#if UNITY_INCLUDE_TESTS
                if (RuntimeSupportOverride.HasValue) return RuntimeSupportOverride.Value;
#endif
#if ENABLE_PICO_XR_SDK
                return true;
#else
                return false;
#endif
            }
        }

        public string DefaultEffect => m_DefaultEffect;
        public IReadOnlyList<PXR_MCP_HapticsEffect> Effects => m_Effects;
        public bool ManagedByMcp => m_ManagedByMcp;
        public PXR_MCP_HapticsEvent OnRequested => m_OnRequested;
        public PXR_MCP_HapticsEvent OnSent => m_OnSent;
        public PXR_MCP_HapticsEvent OnSourceCreated => m_OnSourceCreated;
        public PXR_MCP_HapticsEvent OnStarted => m_OnStarted;
        public PXR_MCP_HapticsEvent OnPaused => m_OnPaused;
        public PXR_MCP_HapticsEvent OnResumed => m_OnResumed;
        public PXR_MCP_HapticsEvent OnUpdated => m_OnUpdated;
        public PXR_MCP_HapticsEvent OnStopped => m_OnStopped;
        public PXR_MCP_HapticsEvent OnFailed => m_OnFailed;

        void OnEnable()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Debug.LogError("[PICO MCP] Only one enabled PXR_MCP_HapticsManager is allowed.", this);
                enabled = false;
                return;
            }
            s_Instance = this;
        }

        void OnDisable()
        {
            if (s_Instance == this) s_Instance = null;
        }

        /// <summary>UnityEvent-friendly entry point for the default named effect.</summary>
        public void Send() => TrySend(m_DefaultEffect);

        /// <summary>UnityEvent-friendly entry point for a named effect.</summary>
        public void Send(string effectName) => TrySend(effectName);

        /// <summary>Sends a named effect and reports whether the request succeeded.</summary>
        public bool TrySend(string effectName)
        {
            if (!TryGetEffect(effectName, out var effect))
                return Fail(effectName, "Named haptics effect was not found.");

            m_OnRequested.Invoke(effect.Name, effect.Target.ToString());
            if (!EnsureRuntime(effect.Name)) return false;
            if (!TryValidate(effect, out var error)) return Fail(effect.Name, error);

            if (effect.EffectType == PXR_MCP_HapticsEffectType.Impulse)
            {
                ForEachHand(effect.Target, (target, settings) =>
                    Api.SendImpulse(
                        target, settings.ImpulseAmplitude, settings.ImpulseDurationMs,
                        settings.ImpulseFrequencyHz), effect);
                m_OnSent.Invoke(effect.Name, effect.Target.ToString());
                m_OnStarted.Invoke(effect.Name, effect.Target.ToString());
                return true;
            }

            StopExistingSources(effect.Name);
            var state = NewSourceState(effect.Target, effect.Left, effect.Right);
            if (!SendBufferHand(effect, PXR_MCP_HapticsTarget.Left, state) ||
                !SendBufferHand(effect, PXR_MCP_HapticsTarget.Right, state))
            {
                StopCreatedSources(state);
                return false;
            }

            m_Sources[effect.Name] = state;
            m_OnSent.Invoke(effect.Name, effect.Target.ToString());
            if (StartsImmediately(effect))
                m_OnStarted.Invoke(effect.Name, effect.Target.ToString());
            return true;
        }

        /// <summary>
        /// UnityEvent&lt;float&gt;-friendly impulse call. It keeps the default
        /// effect's target, duration, and frequency while overriding amplitude.
        /// </summary>
        public void SendImpulse(float amplitude) => TrySendImpulse(amplitude);

        public bool TrySendImpulse(float amplitude)
        {
            if (!TryGetEffect(m_DefaultEffect, out var effect))
                return Fail(m_DefaultEffect, "Default named haptics effect was not found.");
            if (effect.EffectType != PXR_MCP_HapticsEffectType.Impulse)
                return Fail(effect.Name, "SendImpulse(float) requires the default effect to be Impulse.");
            if (!EnsureRuntime(effect.Name)) return false;
            foreach (var hand in Hands(effect.Target))
            {
                var settings = hand == PXR_MCP_HapticsTarget.Left ? effect.Left : effect.Right;
                if (!TryValidateImpulse(
                        amplitude, settings.ImpulseDurationMs, settings.ImpulseFrequencyHz, out var error))
                    return Fail(effect.Name, error);
            }

            m_OnRequested.Invoke(effect.Name, effect.Target.ToString());
            ForEachHand(effect.Target, (target, settings) =>
                Api.SendImpulse(target, amplitude, settings.ImpulseDurationMs,
                    settings.ImpulseFrequencyHz), effect);
            m_OnSent.Invoke(effect.Name, effect.Target.ToString());
            m_OnStarted.Invoke(effect.Name, effect.Target.ToString());
            return true;
        }

        /// <summary>Code-level direct impulse call independent of serialized effects.</summary>
        public bool TrySendImpulse(
            PXR_MCP_HapticsTarget target, float amplitude, int durationMs, int frequencyHz)
        {
            const string DirectEffectName = "DirectImpulse";
            m_OnRequested.Invoke(DirectEffectName, target.ToString());
            if (!EnsureRuntime(DirectEffectName)) return false;
            if (!TryValidateImpulse(amplitude, durationMs, frequencyHz, out var error))
                return Fail(DirectEffectName, error);
            Api.SendImpulse(target, amplitude, durationMs, frequencyHz);
            m_OnSent.Invoke(DirectEffectName, target.ToString());
            m_OnStarted.Invoke(DirectEffectName, target.ToString());
            return true;
        }

        /// <summary>
        /// Sends raw PCM through the current PICO buffer API. PCM is intentionally
        /// code-only because Unity cannot usefully serialize an arbitrary float array.
        /// The name owns the returned source IDs for later lifecycle calls.
        /// </summary>
        public bool SendPcm(
            string effectName,
            PXR_MCP_HapticsTarget target,
            float[] pcmData,
            int bufferSize,
            int sampleRateHz,
            int channelCount,
            PXR_MCP_HapticsChannelFlip channelFlip = PXR_MCP_HapticsChannelFlip.No,
            PXR_MCP_HapticsCacheType cacheType = PXR_MCP_HapticsCacheType.DontCache,
            float amplitudeScale = 1f)
        {
            m_OnRequested.Invoke(effectName ?? string.Empty, target.ToString());
            if (!EnsureRuntime(effectName)) return false;
            if (string.IsNullOrWhiteSpace(effectName)) return Fail(effectName, "effectName is required.");
            if (FindEffect(effectName) != null)
                return Fail(effectName, "PCM effectName must not duplicate a serialized named effect.");
            if (pcmData == null || bufferSize <= 0 || bufferSize > pcmData.Length)
                return Fail(effectName, "pcmData is required and bufferSize must be between 1 and pcmData.Length.");
            if (sampleRateHz <= 0) return Fail(effectName, "sampleRateHz must be greater than zero.");
            if (channelCount <= 0) return Fail(effectName, "channelCount must be greater than zero.");
            if (!TryValidateAmplitudeScale(amplitudeScale, out var error)) return Fail(effectName, error);

            StopExistingSources(effectName);
            var state = new SourceState
            {
                target = target,
                leftChannelFlip = channelFlip,
                rightChannelFlip = channelFlip,
                leftAmplitudeScale = amplitudeScale,
                rightAmplitudeScale = amplitudeScale,
            };

            foreach (var hand in Hands(target))
            {
                int sourceId = 0;
                var result = Api.SendPcm(
                    hand, pcmData, bufferSize, sampleRateHz, channelCount, channelFlip, ref sourceId, cacheType);
                if (result != 0)
                {
                    StopCreatedSources(state);
                    return Fail(effectName, "PXR_Input.SendHapticBuffer(PCM) failed for " + hand +
                                            " with result " + result + ".");
                }
                if (sourceId <= 0)
                {
                    StopCreatedSources(state);
                    return Fail(effectName, "PXR_Input.SendHapticBuffer(PCM) returned no source ID for " + hand + ".");
                }
                SetSourceId(state, hand, sourceId);
                m_OnSourceCreated.Invoke(effectName, hand + ":" + sourceId);
                if (sourceId > 0 && !Mathf.Approximately(amplitudeScale, 1f))
                {
                    result = Api.Update(sourceId, hand, channelFlip, amplitudeScale);
                    if (result != 0)
                    {
                        StopCreatedSources(state);
                        return Fail(effectName, "PXR_Input.UpdateHapticBuffer failed for " + hand +
                                                " with result " + result + ".");
                    }
                }
            }

            m_Sources[effectName] = state;
            m_OnSent.Invoke(effectName, target.ToString());
            if (cacheType != PXR_MCP_HapticsCacheType.CacheNoVibrate)
                m_OnStarted.Invoke(effectName, target.ToString());
            return true;
        }

        // Do not add a parameterless method named Start or Update: Unity invokes
        // those MonoBehaviour messages automatically. The *Effect names are the
        // safe parameterless UnityEvent entry points.
        public void StartEffect() => TryStart(m_DefaultEffect);
        public void StartEffect(string effectName) => TryStart(effectName);
        public bool TryStart(string effectName) => ApplyLifecycle(
            effectName, "StartHapticBuffer", Api.Start, m_OnStarted);

        public void Pause() => TryPause(m_DefaultEffect);
        public void Pause(string effectName) => TryPause(effectName);
        public bool TryPause(string effectName) => ApplyLifecycle(
            effectName, "PauseHapticBuffer", Api.Pause, m_OnPaused);

        public void Resume() => TryResume(m_DefaultEffect);
        public void Resume(string effectName) => TryResume(effectName);
        public bool TryResume(string effectName) => ApplyLifecycle(
            effectName, "ResumeHapticBuffer", Api.Resume, m_OnResumed);

        public void UpdateEffect() => TryUpdate(m_DefaultEffect);
        public void UpdateEffect(string effectName) => TryUpdate(effectName);
        public bool TryUpdate(string effectName)
        {
            if (!EnsureRuntime(effectName)) return false;
            if (!m_Sources.TryGetValue(effectName, out var state))
                return Fail(effectName, "No buffered source exists. Call Send first.");
            var effect = FindEffect(effectName);
            if (effect != null)
            {
                state.leftChannelFlip = effect.Left.ChannelFlip;
                state.rightChannelFlip = effect.Right.ChannelFlip;
                state.leftAmplitudeScale = effect.Left.AmplitudeScale;
                state.rightAmplitudeScale = effect.Right.AmplitudeScale;
            }
            return ApplyUpdate(effectName, state);
        }

        bool ApplyUpdate(string effectName, SourceState state)
        {
            if (!ApplyToSources(state, (sourceId, target) =>
            {
                var flip = target == PXR_MCP_HapticsTarget.Left
                    ? state.leftChannelFlip : state.rightChannelFlip;
                var scale = target == PXR_MCP_HapticsTarget.Left
                    ? state.leftAmplitudeScale : state.rightAmplitudeScale;
                return Api.Update(sourceId, target, flip, scale);
            }, out var result))
                return Fail(effectName, "PXR_Input.UpdateHapticBuffer failed with result " + result + ".");

            m_OnUpdated.Invoke(effectName, state.target.ToString());
            return true;
        }

        public void UpdateAmplitude(float amplitudeScale) => TryUpdateAmplitude(m_DefaultEffect, amplitudeScale);
        public bool TryUpdateAmplitude(string effectName, float amplitudeScale)
        {
            if (!TryValidateAmplitudeScale(amplitudeScale, out var error)) return Fail(effectName, error);
            if (!m_Sources.TryGetValue(effectName, out var state))
                return Fail(effectName, "No buffered source exists. Call Send first.");
            state.leftAmplitudeScale = amplitudeScale;
            state.rightAmplitudeScale = amplitudeScale;
            return ApplyUpdate(effectName, state);
        }

        public void Stop() => TryStop(m_DefaultEffect, false);
        public void Stop(string effectName) => TryStop(effectName, false);
        public void StopAndClear() => TryStop(m_DefaultEffect, true);
        public void StopAndClear(string effectName) => TryStop(effectName, true);

        public bool TryStop(string effectName, bool clearCache = false)
        {
            if (!EnsureRuntime(effectName)) return false;
            var effect = FindEffect(effectName);
            if (effect != null && effect.EffectType == PXR_MCP_HapticsEffectType.Impulse)
            {
                ForEachHand(effect.Target, (target, settings) =>
                    Api.SendImpulse(target, 0f, 0, settings.ImpulseFrequencyHz), effect);
                m_OnStopped.Invoke(effectName, effect.Target.ToString());
                return true;
            }

            if (!m_Sources.TryGetValue(effectName, out var state))
                return Fail(effectName, "No buffered source exists. Call Send first.");
            if (!ApplyToSources(state, (sourceId, _) => Api.Stop(sourceId, clearCache), out var result))
                return Fail(effectName, "PXR_Input.StopHapticBuffer failed with result " + result + ".");
            if (clearCache) m_Sources.Remove(effectName);
            m_OnStopped.Invoke(effectName, state.target.ToString());
            return true;
        }

        public void StopAll() => TryStopAll(false);
        public void StopAllAndClear() => TryStopAll(true);

        public bool TryStopAll(bool clearCache = false)
        {
            if (!EnsureRuntime("*")) return false;
            Api.SendImpulse(PXR_MCP_HapticsTarget.Both, 0f, 0, 150);
            var result = Api.Stop(0, clearCache);
            if (result != 0) return Fail("*", "PXR_Input.StopHapticBuffer failed with result " + result + ".");
            if (clearCache) m_Sources.Clear();
            m_OnStopped.Invoke("*", PXR_MCP_HapticsTarget.Both.ToString());
            return true;
        }

        public bool TryGetEffect(string effectName, out PXR_MCP_HapticsEffect effect)
        {
            effect = FindEffect(effectName);
            return effect != null;
        }

        public static bool TryValidate(PXR_MCP_HapticsEffect effect, out string error)
        {
            if (effect == null)
            {
                error = "effect is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(effect.Name))
            {
                error = "effect name is required.";
                return false;
            }

            foreach (var hand in Hands(effect.Target))
            {
                var settings = hand == PXR_MCP_HapticsTarget.Left ? effect.Left : effect.Right;
                if (settings == null)
                {
                    error = hand + " settings are required.";
                    return false;
                }
                if (effect.EffectType == PXR_MCP_HapticsEffectType.Impulse &&
                    !TryValidateImpulse(
                        settings.ImpulseAmplitude, settings.ImpulseDurationMs,
                        settings.ImpulseFrequencyHz, out error))
                    return false;
                if (effect.EffectType == PXR_MCP_HapticsEffectType.AudioClipBuffer && settings.AudioClip == null)
                {
                    error = hand + " AudioClip is required for AudioClipBuffer.";
                    return false;
                }
                if (effect.EffectType == PXR_MCP_HapticsEffectType.PhfBuffer && settings.PhfText == null)
                {
                    error = hand + " PHF TextAsset is required for PhfBuffer.";
                    return false;
                }
                if (effect.EffectType != PXR_MCP_HapticsEffectType.Impulse &&
                    !TryValidateAmplitudeScale(settings.AmplitudeScale, out error))
                    return false;
            }

            error = null;
            return true;
        }

        public static bool TryValidateImpulse(float amplitude, int durationMs, int frequencyHz, out string error)
        {
            if (!IsFinite(amplitude) || amplitude < MinImpulseAmplitude || amplitude > MaxImpulseAmplitude)
            {
                error = "impulse amplitude must be between 0 and 1.";
                return false;
            }
            if (durationMs < MinImpulseDurationMs || durationMs > MaxImpulseDurationMs)
            {
                error = "impulse durationMs must be between 0 and 65535.";
                return false;
            }
            if (frequencyHz < MinImpulseFrequencyHz || frequencyHz > MaxImpulseFrequencyHz)
            {
                error = "impulse frequencyHz must be between 50 and 500.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool TryValidateAmplitudeScale(float amplitudeScale, out string error)
        {
            if (!IsFinite(amplitudeScale) ||
                amplitudeScale < MinBufferAmplitudeScale || amplitudeScale > MaxBufferAmplitudeScale)
            {
                error = "buffer amplitudeScale must be between 0 and 2.";
                return false;
            }
            error = null;
            return true;
        }

        internal void ConfigureManagedByMcp(bool managedByMcp) => m_ManagedByMcp = managedByMcp;

        internal bool UpsertEffect(PXR_MCP_HapticsEffect effect, bool setDefault)
        {
            if (!TryValidate(effect, out var error)) throw new ArgumentException(error, nameof(effect));
            var current = FindEffect(effect.Name);
            var changed = current == null || !current.ConfigurationEquals(effect);
            if (current == null) m_Effects.Add(effect.Clone());
            else if (changed) current.Configure(
                effect.Name, effect.EffectType, effect.Target, effect.Left, effect.Right);
            if (setDefault && !string.Equals(m_DefaultEffect, effect.Name, StringComparison.Ordinal))
            {
                m_DefaultEffect = effect.Name;
                changed = true;
            }
            return changed;
        }

        internal bool RemoveEffect(string effectName)
        {
            var removed = m_Effects.RemoveAll(effect =>
                effect != null && string.Equals(effect.Name, effectName, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                m_Sources.Remove(effectName);
                if (string.Equals(m_DefaultEffect, effectName, StringComparison.Ordinal))
                    m_DefaultEffect = m_Effects.FirstOrDefault(effect => effect != null)?.Name ?? string.Empty;
            }
            return removed;
        }

        internal static PXR_MCP_HapticsEffect CreateEffect(
            string name, PXR_MCP_HapticsEffectType type, PXR_MCP_HapticsTarget target,
            PXR_MCP_HapticsHandSettings left, PXR_MCP_HapticsHandSettings right)
        {
            var effect = new PXR_MCP_HapticsEffect();
            effect.Configure(name, type, target, left, right);
            return effect;
        }

        internal static PXR_MCP_HapticsHandSettings CreateHandSettings(
            float impulseAmplitude, int impulseDurationMs, int impulseFrequencyHz,
            AudioClip audioClip, TextAsset phfText, PXR_MCP_HapticsChannelFlip channelFlip,
            PXR_MCP_HapticsCacheType cacheType, float amplitudeScale)
        {
            var settings = new PXR_MCP_HapticsHandSettings();
            settings.Configure(
                impulseAmplitude, impulseDurationMs, impulseFrequencyHz, audioClip, phfText,
                channelFlip, cacheType, amplitudeScale);
            return settings;
        }

        PXR_MCP_HapticsEffect FindEffect(string effectName)
        {
            if (string.IsNullOrWhiteSpace(effectName)) return null;
            return m_Effects.Find(effect => effect != null &&
                string.Equals(effect.Name, effectName, StringComparison.Ordinal));
        }

        bool SendBufferHand(
            PXR_MCP_HapticsEffect effect, PXR_MCP_HapticsTarget hand, SourceState state)
        {
            if (!Includes(effect.Target, hand)) return true;
            var settings = hand == PXR_MCP_HapticsTarget.Left ? effect.Left : effect.Right;
            int sourceId = 0;
            int result;
            switch (effect.EffectType)
            {
                case PXR_MCP_HapticsEffectType.AudioClipBuffer:
                    result = Api.SendAudio(
                        hand, settings.AudioClip, settings.ChannelFlip, ref sourceId, settings.CacheType);
                    break;
                case PXR_MCP_HapticsEffectType.PhfBuffer:
                    result = Api.SendPhf(
                        hand, settings.PhfText, settings.ChannelFlip, settings.AmplitudeScale, ref sourceId);
                    break;
                default:
                    return Fail(effect.Name, "Unsupported buffered effect type " + effect.EffectType + ".");
            }
            if (result != 0)
                return Fail(effect.Name, "PXR_Input.SendHapticBuffer failed for " + hand +
                                         " with result " + result + ".");
            if (sourceId <= 0)
                return Fail(effect.Name, "PXR_Input.SendHapticBuffer returned no source ID for " + hand + ".");

            SetSourceId(state, hand, sourceId);
            m_OnSourceCreated.Invoke(effect.Name, hand + ":" + sourceId);
            if (effect.EffectType == PXR_MCP_HapticsEffectType.AudioClipBuffer &&
                sourceId > 0 && !Mathf.Approximately(settings.AmplitudeScale, 1f))
            {
                result = Api.Update(sourceId, hand, settings.ChannelFlip, settings.AmplitudeScale);
                if (result != 0)
                    return Fail(effect.Name, "PXR_Input.UpdateHapticBuffer failed for " + hand +
                                             " with result " + result + ".");
            }
            return true;
        }

        bool ApplyLifecycle(string effectName, string apiName, Func<int, int> call, PXR_MCP_HapticsEvent callback)
        {
            if (!EnsureRuntime(effectName)) return false;
            if (!m_Sources.TryGetValue(effectName, out var state))
                return Fail(effectName, "No buffered source exists. Call Send first.");
            if (!ApplyToSources(state, (sourceId, _) => call(sourceId), out var result))
                return Fail(effectName, "PXR_Input." + apiName + " failed with result " + result + ".");
            callback.Invoke(effectName, state.target.ToString());
            return true;
        }

        static bool ApplyToSources(
            SourceState state, Func<int, PXR_MCP_HapticsTarget, int> call, out int failedResult)
        {
            failedResult = 0;
            foreach (var hand in Hands(state.target))
            {
                var sourceId = hand == PXR_MCP_HapticsTarget.Left
                    ? state.leftSourceId : state.rightSourceId;
                if (sourceId <= 0)
                {
                    failedResult = -1;
                    return false;
                }
                var result = call(sourceId, hand);
                if (result != 0)
                {
                    failedResult = result;
                    return false;
                }
            }
            return true;
        }

        void StopExistingSources(string effectName)
        {
            if (!m_Sources.TryGetValue(effectName, out var state)) return;
            StopCreatedSources(state);
            m_Sources.Remove(effectName);
        }

        void StopCreatedSources(SourceState state)
        {
            if (state.leftSourceId > 0) Api.Stop(state.leftSourceId, true);
            if (state.rightSourceId > 0) Api.Stop(state.rightSourceId, true);
        }

        bool EnsureRuntime(string effectName)
        {
            return IsRuntimeSupported ||
                   Fail(effectName, "Controller haptics require the PICO native runtime (ENABLE_PICO_XR_SDK).");
        }

        bool Fail(string effectName, string reason)
        {
            var safeName = effectName ?? string.Empty;
            Debug.LogError("[PICO MCP] Haptics '" + safeName + "' failed: " + reason, this);
            m_OnFailed.Invoke(safeName, reason);
            return false;
        }

        static bool StartsImmediately(PXR_MCP_HapticsEffect effect)
        {
            if (effect.EffectType == PXR_MCP_HapticsEffectType.PhfBuffer) return true;
            foreach (var hand in Hands(effect.Target))
            {
                var settings = hand == PXR_MCP_HapticsTarget.Left ? effect.Left : effect.Right;
                if (settings.CacheType == PXR_MCP_HapticsCacheType.CacheNoVibrate) return false;
            }
            return true;
        }

        static SourceState NewSourceState(
            PXR_MCP_HapticsTarget target, PXR_MCP_HapticsHandSettings left,
            PXR_MCP_HapticsHandSettings right)
        {
            return new SourceState
            {
                target = target,
                leftChannelFlip = left.ChannelFlip,
                rightChannelFlip = right.ChannelFlip,
                leftAmplitudeScale = left.AmplitudeScale,
                rightAmplitudeScale = right.AmplitudeScale,
            };
        }

        static void SetSourceId(SourceState state, PXR_MCP_HapticsTarget hand, int sourceId)
        {
            if (hand == PXR_MCP_HapticsTarget.Left) state.leftSourceId = sourceId;
            else state.rightSourceId = sourceId;
        }

        static void ForEachHand(
            PXR_MCP_HapticsTarget target,
            Action<PXR_MCP_HapticsTarget, PXR_MCP_HapticsHandSettings> action,
            PXR_MCP_HapticsEffect effect)
        {
            foreach (var hand in Hands(target))
                action(hand, hand == PXR_MCP_HapticsTarget.Left ? effect.Left : effect.Right);
        }

        static IEnumerable<PXR_MCP_HapticsTarget> Hands(PXR_MCP_HapticsTarget target)
        {
            if (target == PXR_MCP_HapticsTarget.Left || target == PXR_MCP_HapticsTarget.Both)
                yield return PXR_MCP_HapticsTarget.Left;
            if (target == PXR_MCP_HapticsTarget.Right || target == PXR_MCP_HapticsTarget.Both)
                yield return PXR_MCP_HapticsTarget.Right;
        }

        static bool Includes(PXR_MCP_HapticsTarget target, PXR_MCP_HapticsTarget hand)
        {
            return target == PXR_MCP_HapticsTarget.Both || target == hand;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static IPXR_MCP_HapticsApi Api => ApiOverride ?? PicoHapticsApi.Instance;

        sealed class PicoHapticsApi : IPXR_MCP_HapticsApi
        {
            public static readonly PicoHapticsApi Instance = new PicoHapticsApi();

            public void SendImpulse(PXR_MCP_HapticsTarget target, float amplitude, int durationMs, int frequencyHz)
            {
#if ENABLE_PICO_XR_SDK
                PXR_Input.SendHapticImpulse(ToPicoTarget(target), amplitude, durationMs, frequencyHz);
#endif
            }

            public int SendAudio(
                PXR_MCP_HapticsTarget target, AudioClip audioClip, PXR_MCP_HapticsChannelFlip channelFlip,
                ref int sourceId, PXR_MCP_HapticsCacheType cacheType)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.SendHapticBuffer(
                    ToPicoTarget(target), audioClip, ToPicoChannelFlip(channelFlip), ref sourceId,
                    ToPicoCacheType(cacheType));
#else
                return -1;
#endif
            }

            public int SendPcm(
                PXR_MCP_HapticsTarget target, float[] pcmData, int bufferSize, int sampleRateHz, int channelCount,
                PXR_MCP_HapticsChannelFlip channelFlip, ref int sourceId, PXR_MCP_HapticsCacheType cacheType)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.SendHapticBuffer(
                    ToPicoTarget(target), pcmData, bufferSize, sampleRateHz, channelCount,
                    ToPicoChannelFlip(channelFlip), ref sourceId, ToPicoCacheType(cacheType));
#else
                return -1;
#endif
            }

            public int SendPhf(
                PXR_MCP_HapticsTarget target, TextAsset phfText, PXR_MCP_HapticsChannelFlip channelFlip,
                float amplitudeScale, ref int sourceId)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.SendHapticBuffer(
                    ToPicoTarget(target), phfText, ToPicoChannelFlip(channelFlip), amplitudeScale, ref sourceId);
#else
                return -1;
#endif
            }

            public int Start(int sourceId)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.StartHapticBuffer(sourceId);
#else
                return -1;
#endif
            }

            public int Pause(int sourceId)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.PauseHapticBuffer(sourceId);
#else
                return -1;
#endif
            }

            public int Resume(int sourceId)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.ResumeHapticBuffer(sourceId);
#else
                return -1;
#endif
            }

            public int Update(
                int sourceId, PXR_MCP_HapticsTarget target, PXR_MCP_HapticsChannelFlip channelFlip,
                float amplitudeScale)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.UpdateHapticBuffer(
                    sourceId, ToPicoTarget(target), ToPicoChannelFlip(channelFlip), amplitudeScale);
#else
                return -1;
#endif
            }

            public int Stop(int sourceId, bool clearCache)
            {
#if ENABLE_PICO_XR_SDK
                return PXR_Input.StopHapticBuffer(sourceId, clearCache);
#else
                return -1;
#endif
            }

#if ENABLE_PICO_XR_SDK
            static PXR_Input.VibrateType ToPicoTarget(PXR_MCP_HapticsTarget target)
            {
                switch (target)
                {
                    case PXR_MCP_HapticsTarget.Left: return PXR_Input.VibrateType.LeftController;
                    case PXR_MCP_HapticsTarget.Right: return PXR_Input.VibrateType.RightController;
                    default: return PXR_Input.VibrateType.BothController;
                }
            }

            static PXR_Input.ChannelFlip ToPicoChannelFlip(PXR_MCP_HapticsChannelFlip value)
                => value == PXR_MCP_HapticsChannelFlip.Yes
                    ? PXR_Input.ChannelFlip.Yes : PXR_Input.ChannelFlip.No;

            static PXR_Input.CacheType ToPicoCacheType(PXR_MCP_HapticsCacheType value)
            {
                switch (value)
                {
                    case PXR_MCP_HapticsCacheType.CacheAndVibrate: return PXR_Input.CacheType.CacheAndVibrate;
                    case PXR_MCP_HapticsCacheType.CacheNoVibrate: return PXR_Input.CacheType.CacheNoVibrate;
                    default: return PXR_Input.CacheType.DontCache;
                }
            }
#endif
        }
    }
}
