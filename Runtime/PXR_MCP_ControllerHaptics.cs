using System;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace ByteDance.PICO.MCPExtensions
{
    public enum PXR_MCP_ControllerSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// Runtime bridge for PICO controller vibration. The component deliberately
    /// owns no gameplay trigger: call one of its public methods from any script
    /// or UnityEvent at the point where the application wants haptic feedback.
    /// </summary>
    [AddComponentMenu("PICO MCP/Controller Haptics")]
    [DisallowMultipleComponent]
    public sealed class PXR_MCP_ControllerHaptics : MonoBehaviour
    {
        public const float MinAmplitude = 0f;
        public const float MaxAmplitude = 1f;
        public const int MinDurationMs = 0;
        public const int MaxDurationMs = 65535;
        public const int MinFrequencyHz = 50;
        public const int MaxFrequencyHz = 500;

        [SerializeField]
        PXR_MCP_ControllerSide m_Controller = PXR_MCP_ControllerSide.Left;

        [SerializeField, Tooltip("Vibration amplitude. Valid range: 0 to 1.")]
        float m_Amplitude = 0.5f;

        [SerializeField, Tooltip("Vibration duration in milliseconds. Valid range: 0 to 65535.")]
        int m_DurationMs = 100;

        [SerializeField, Tooltip("Vibration frequency in hertz. Valid range: 50 to 500.")]
        int m_FrequencyHz = 150;

        [SerializeField, HideInInspector]
        bool m_ManagedByMcp;

#if UNITY_INCLUDE_TESTS
        internal static Action<PXR_MCP_ControllerSide, float, int, int> SendOverride;
#endif

        public PXR_MCP_ControllerSide Controller => m_Controller;
        public float Amplitude => m_Amplitude;
        public int DurationMs => m_DurationMs;
        public int FrequencyHz => m_FrequencyHz;
        public bool ManagedByMcp => m_ManagedByMcp;

        /// <summary>Uses the serialized defaults. Suitable for a parameterless UnityEvent.</summary>
        public void Vibrate() => Send(m_Amplitude, m_DurationMs, m_FrequencyHz);

        /// <summary>
        /// Uses a caller-supplied amplitude with the serialized duration and frequency.
        /// Suitable for a UnityEvent&lt;float&gt; or business logic driven by a scalar value.
        /// </summary>
        public void VibrateWithAmplitude(float amplitude) => Send(amplitude, m_DurationMs, m_FrequencyHz);

        /// <summary>Uses explicit PICO impulse parameters.</summary>
        public void Vibrate(float amplitude, int durationMs, int frequencyHz)
            => Send(amplitude, durationMs, frequencyHz);

        /// <summary>Stops the vibration on this component's controller.</summary>
        public void Stop() => Send(0f, 0, m_FrequencyHz);

        public static bool TryValidate(float amplitude, int durationMs, int frequencyHz, out string error)
        {
            if (float.IsNaN(amplitude) || float.IsInfinity(amplitude) ||
                amplitude < MinAmplitude || amplitude > MaxAmplitude)
            {
                error = $"amplitude must be between {MinAmplitude} and {MaxAmplitude}";
                return false;
            }

            if (durationMs < MinDurationMs || durationMs > MaxDurationMs)
            {
                error = $"durationMs must be between {MinDurationMs} and {MaxDurationMs}";
                return false;
            }

            if (frequencyHz < MinFrequencyHz || frequencyHz > MaxFrequencyHz)
            {
                error = $"frequencyHz must be between {MinFrequencyHz} and {MaxFrequencyHz}";
                return false;
            }

            error = null;
            return true;
        }

        internal void Configure(
            PXR_MCP_ControllerSide controller,
            float amplitude,
            int durationMs,
            int frequencyHz,
            bool managedByMcp)
        {
            if (!TryValidate(amplitude, durationMs, frequencyHz, out var error))
                throw new ArgumentOutOfRangeException(nameof(amplitude), error);

            m_Controller = controller;
            m_Amplitude = amplitude;
            m_DurationMs = durationMs;
            m_FrequencyHz = frequencyHz;
            m_ManagedByMcp = managedByMcp;
        }

        void Send(float amplitude, int durationMs, int frequencyHz)
        {
            if (!TryValidate(amplitude, durationMs, frequencyHz, out var error))
            {
                Debug.LogError("[PICO MCP] Controller haptics rejected: " + error, this);
                return;
            }

#if UNITY_INCLUDE_TESTS
            if (SendOverride != null)
            {
                SendOverride(m_Controller, amplitude, durationMs, frequencyHz);
                return;
            }
#endif

#if ENABLE_PICO_XR_SDK
            PXR_Input.SendHapticImpulse(
                m_Controller == PXR_MCP_ControllerSide.Left
                    ? PXR_Input.VibrateType.LeftController
                    : PXR_Input.VibrateType.RightController,
                amplitude,
                durationMs,
                frequencyHz);
#else
            Debug.LogWarning(
                "[PICO MCP] Controller haptics require the PICO native runtime " +
                "(ENABLE_PICO_XR_SDK). No vibration was sent.", this);
#endif
        }
    }
}
