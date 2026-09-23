using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_HapticsManagerTests
    {
        sealed class FakeApi : IPXR_MCP_HapticsApi
        {
            public readonly List<string> calls = new List<string>();
            public int nextSourceId = 10;
            public int result;

            public void SendImpulse(PXR_MCP_HapticsTarget target, float amplitude, int durationMs, int frequencyHz)
                => calls.Add($"impulse:{target}:{amplitude}:{durationMs}:{frequencyHz}");

            public int SendAudio(
                PXR_MCP_HapticsTarget target, AudioClip audioClip, PXR_MCP_HapticsChannelFlip channelFlip,
                ref int sourceId, PXR_MCP_HapticsCacheType cacheType)
            {
                sourceId = nextSourceId++;
                calls.Add($"audio:{target}:{channelFlip}:{cacheType}:{sourceId}");
                return result;
            }

            public int SendPcm(
                PXR_MCP_HapticsTarget target, float[] pcmData, int bufferSize, int sampleRateHz, int channelCount,
                PXR_MCP_HapticsChannelFlip channelFlip, ref int sourceId, PXR_MCP_HapticsCacheType cacheType)
            {
                sourceId = nextSourceId++;
                calls.Add($"pcm:{target}:{bufferSize}:{sampleRateHz}:{channelCount}:{sourceId}");
                return result;
            }

            public int SendPhf(
                PXR_MCP_HapticsTarget target, TextAsset phfText, PXR_MCP_HapticsChannelFlip channelFlip,
                float amplitudeScale, ref int sourceId)
            {
                sourceId = nextSourceId++;
                calls.Add($"phf:{target}:{amplitudeScale}:{sourceId}");
                return result;
            }

            public int Start(int sourceId) { calls.Add("start:" + sourceId); return result; }
            public int Pause(int sourceId) { calls.Add("pause:" + sourceId); return result; }
            public int Resume(int sourceId) { calls.Add("resume:" + sourceId); return result; }
            public int Update(
                int sourceId, PXR_MCP_HapticsTarget target, PXR_MCP_HapticsChannelFlip channelFlip,
                float amplitudeScale)
            {
                calls.Add($"update:{sourceId}:{target}:{channelFlip}:{amplitudeScale}");
                return result;
            }
            public int Stop(int sourceId, bool clearCache)
            {
                calls.Add($"stop:{sourceId}:{clearCache}");
                return result;
            }
        }

        readonly List<Object> m_CreatedObjects = new List<Object>();
        FakeApi m_Api;
        PXR_MCP_HapticsManager m_Manager;

        [SetUp]
        public void SetUp()
        {
            m_Api = new FakeApi();
            PXR_MCP_HapticsManager.ApiOverride = m_Api;
            PXR_MCP_HapticsManager.RuntimeSupportOverride = true;
            var go = Track(new GameObject("Haptics Manager"));
            m_Manager = go.AddComponent<PXR_MCP_HapticsManager>();
        }

        [TearDown]
        public void TearDown()
        {
            PXR_MCP_HapticsManager.ApiOverride = null;
            PXR_MCP_HapticsManager.RuntimeSupportOverride = null;
            foreach (var item in m_CreatedObjects)
                if (item != null) Object.DestroyImmediate(item);
            m_CreatedObjects.Clear();
        }

        [TestCase(0f, 0, 50)]
        [TestCase(1f, 65535, 500)]
        [TestCase(0.5f, 100, 150)]
        public void TryValidateImpulse_AcceptsDocumentedRange(
            float amplitude, int durationMs, int frequencyHz)
        {
            Assert.That(PXR_MCP_HapticsManager.TryValidateImpulse(
                amplitude, durationMs, frequencyHz, out var error), Is.True);
            Assert.That(error, Is.Null);
        }

        [TestCase(-0.01f, 100, 150)]
        [TestCase(1.01f, 100, 150)]
        [TestCase(0.5f, -1, 150)]
        [TestCase(0.5f, 65536, 150)]
        [TestCase(0.5f, 100, 49)]
        [TestCase(0.5f, 100, 501)]
        public void TryValidateImpulse_RejectsOutOfRangeValues(
            float amplitude, int durationMs, int frequencyHz)
        {
            Assert.That(PXR_MCP_HapticsManager.TryValidateImpulse(
                amplitude, durationMs, frequencyHz, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void SendImpulseBoth_UsesIndependentHandParametersAndCallbacks()
        {
            var effect = Effect(
                "Hit", PXR_MCP_HapticsEffectType.Impulse, PXR_MCP_HapticsTarget.Both,
                Hand(0.2f, 40, 120), Hand(0.9f, 90, 240));
            m_Manager.UpsertEffect(effect, true);
            var events = new List<string>();
            m_Manager.OnRequested.AddListener((name, detail) => events.Add("requested:" + name + ":" + detail));
            m_Manager.OnSent.AddListener((name, detail) => events.Add("sent:" + name + ":" + detail));
            m_Manager.OnStarted.AddListener((name, detail) => events.Add("started:" + name + ":" + detail));

            Assert.That(m_Manager.TrySend("Hit"), Is.True);

            Assert.That(m_Api.calls, Is.EqualTo(new[]
            {
                "impulse:Left:0.2:40:120",
                "impulse:Right:0.9:90:240",
            }));
            Assert.That(events, Is.EqualTo(new[]
            {
                "requested:Hit:Both", "sent:Hit:Both", "started:Hit:Both",
            }));
        }

        [Test]
        public void SendAudioBoth_TracksIndependentSourcesAndLifecycle()
        {
            var leftClip = Track(AudioClip.Create("left", 16, 1, 1000, false));
            var rightClip = Track(AudioClip.Create("right", 16, 1, 1000, false));
            var left = Hand(audioClip: leftClip, cacheType: PXR_MCP_HapticsCacheType.CacheNoVibrate);
            var right = Hand(audioClip: rightClip, cacheType: PXR_MCP_HapticsCacheType.CacheNoVibrate);
            m_Manager.UpsertEffect(Effect("Engine", PXR_MCP_HapticsEffectType.AudioClipBuffer,
                PXR_MCP_HapticsTarget.Both, left, right), true);

            Assert.That(m_Manager.TrySend("Engine"), Is.True);
            Assert.That(m_Manager.TryStart("Engine"), Is.True);
            Assert.That(m_Manager.TryPause("Engine"), Is.True);
            Assert.That(m_Manager.TryResume("Engine"), Is.True);
            Assert.That(m_Manager.TryUpdateAmplitude("Engine", 1.5f), Is.True);
            Assert.That(m_Manager.TryStop("Engine", true), Is.True);

            Assert.That(m_Api.calls, Does.Contain("audio:Left:No:CacheNoVibrate:10"));
            Assert.That(m_Api.calls, Does.Contain("audio:Right:No:CacheNoVibrate:11"));
            Assert.That(m_Api.calls, Does.Contain("start:10"));
            Assert.That(m_Api.calls, Does.Contain("start:11"));
            Assert.That(m_Api.calls, Does.Contain("update:10:Left:No:1.5"));
            Assert.That(m_Api.calls, Does.Contain("update:11:Right:No:1.5"));
            Assert.That(m_Api.calls, Does.Contain("stop:10:True"));
            Assert.That(m_Api.calls, Does.Contain("stop:11:True"));
            LogAssert.Expect(LogType.Error,
                "[PICO MCP] Haptics 'Engine' failed: No buffered source exists. Call Send first.");
            Assert.That(m_Manager.TryPause("Engine"), Is.False);
        }

        [Test]
        public void SendPhf_UsesAmplitudeScaleDirectly()
        {
            var phf = Track(new TextAsset("{}"));
            m_Manager.UpsertEffect(Effect("Pulse", PXR_MCP_HapticsEffectType.PhfBuffer,
                PXR_MCP_HapticsTarget.Left, Hand(phfText: phf, amplitudeScale: 1.7f), Hand()), true);

            Assert.That(m_Manager.TrySend("Pulse"), Is.True);

            Assert.That(m_Api.calls, Does.Contain("phf:Left:1.7:10"));
        }

        [Test]
        public void SendPcm_IsCodeOnlyAndSupportsLaterLifecycleCalls()
        {
            Assert.That(m_Manager.SendPcm(
                "RuntimePcm", PXR_MCP_HapticsTarget.Right, new[] { 0f, 0.5f }, 2, 1000, 1,
                amplitudeScale: 1.2f), Is.True);
            Assert.That(m_Manager.TryPause("RuntimePcm"), Is.True);

            Assert.That(m_Api.calls, Does.Contain("pcm:Right:2:1000:1:10"));
            Assert.That(m_Api.calls, Does.Contain("update:10:Right:No:1.2"));
            Assert.That(m_Api.calls, Does.Contain("pause:10"));
        }

        [Test]
        public void Failure_InvokesVisibleFailureCallback()
        {
            string callbackName = null;
            string callbackReason = null;
            m_Manager.OnFailed.AddListener((name, reason) =>
            {
                callbackName = name;
                callbackReason = reason;
            });

            LogAssert.Expect(LogType.Error,
                "[PICO MCP] Haptics 'Missing' failed: Named haptics effect was not found.");
            Assert.That(m_Manager.TrySend("Missing"), Is.False);
            Assert.That(callbackName, Is.EqualTo("Missing"));
            Assert.That(callbackReason, Does.Contain("not found"));
        }

        [Test]
        public void DuplicateManager_DisablesSecondInstance()
        {
            LogAssert.Expect(LogType.Error, "[PICO MCP] Only one enabled PXR_MCP_HapticsManager is allowed.");
            var second = Track(new GameObject("Second")).AddComponent<PXR_MCP_HapticsManager>();

            Assert.That(PXR_MCP_HapticsManager.Instance, Is.SameAs(m_Manager));
            Assert.That(second.enabled, Is.False);
        }

        static PXR_MCP_HapticsEffect Effect(
            string name, PXR_MCP_HapticsEffectType type, PXR_MCP_HapticsTarget target,
            PXR_MCP_HapticsHandSettings left, PXR_MCP_HapticsHandSettings right)
            => PXR_MCP_HapticsManager.CreateEffect(name, type, target, left, right);

        static PXR_MCP_HapticsHandSettings Hand(
            float amplitude = 0.5f, int durationMs = 100, int frequencyHz = 150,
            AudioClip audioClip = null, TextAsset phfText = null,
            PXR_MCP_HapticsChannelFlip channelFlip = PXR_MCP_HapticsChannelFlip.No,
            PXR_MCP_HapticsCacheType cacheType = PXR_MCP_HapticsCacheType.DontCache,
            float amplitudeScale = 1f)
            => PXR_MCP_HapticsManager.CreateHandSettings(
                amplitude, durationMs, frequencyHz, audioClip, phfText,
                channelFlip, cacheType, amplitudeScale);

        T Track<T>(T item) where T : Object
        {
            m_CreatedObjects.Add(item);
            return item;
        }
    }
}
