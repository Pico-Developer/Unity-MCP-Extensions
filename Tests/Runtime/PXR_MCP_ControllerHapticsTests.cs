using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_ControllerHapticsTests
    {
        readonly List<GameObject> m_CreatedObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            PXR_MCP_ControllerHaptics.SendOverride = null;
            foreach (var gameObject in m_CreatedObjects)
            {
                if (gameObject != null) Object.DestroyImmediate(gameObject);
            }
            m_CreatedObjects.Clear();
        }

        [TestCase(0f, 0, 50)]
        [TestCase(1f, 65535, 500)]
        [TestCase(0.5f, 100, 150)]
        public void TryValidate_AcceptsDocumentedRange(float amplitude, int durationMs, int frequencyHz)
        {
            Assert.That(
                PXR_MCP_ControllerHaptics.TryValidate(amplitude, durationMs, frequencyHz, out var error),
                Is.True);
            Assert.That(error, Is.Null);
        }

        [TestCase(-0.01f, 100, 150)]
        [TestCase(1.01f, 100, 150)]
        [TestCase(0.5f, -1, 150)]
        [TestCase(0.5f, 65536, 150)]
        [TestCase(0.5f, 100, 49)]
        [TestCase(0.5f, 100, 501)]
        public void TryValidate_RejectsOutOfRangeValues(float amplitude, int durationMs, int frequencyHz)
        {
            Assert.That(
                PXR_MCP_ControllerHaptics.TryValidate(amplitude, durationMs, frequencyHz, out var error),
                Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void TryValidate_RejectsNonFiniteAmplitude()
        {
            Assert.That(PXR_MCP_ControllerHaptics.TryValidate(float.NaN, 100, 150, out _), Is.False);
            Assert.That(PXR_MCP_ControllerHaptics.TryValidate(float.PositiveInfinity, 100, 150, out _), Is.False);
        }

        [Test]
        public void PublicEntryPoints_ForwardExpectedParametersExactlyOnce()
        {
            var gameObject = Track(new GameObject("Haptics"));
            var component = gameObject.AddComponent<PXR_MCP_ControllerHaptics>();
            component.Configure(PXR_MCP_ControllerSide.Right, 0.5f, 100, 150, true);

            var calls = new List<(PXR_MCP_ControllerSide side, float amplitude, int durationMs, int frequencyHz)>();
            PXR_MCP_ControllerHaptics.SendOverride =
                (side, amplitude, durationMs, frequencyHz) => calls.Add((side, amplitude, durationMs, frequencyHz));

            component.Vibrate();
            component.VibrateWithAmplitude(0.8f);
            component.Vibrate(0.9f, 80, 180);
            component.Stop();

            Assert.That(calls, Has.Count.EqualTo(4));
            Assert.That(calls[0], Is.EqualTo((PXR_MCP_ControllerSide.Right, 0.5f, 100, 150)));
            Assert.That(calls[1], Is.EqualTo((PXR_MCP_ControllerSide.Right, 0.8f, 100, 150)));
            Assert.That(calls[2], Is.EqualTo((PXR_MCP_ControllerSide.Right, 0.9f, 80, 180)));
            Assert.That(calls[3], Is.EqualTo((PXR_MCP_ControllerSide.Right, 0f, 0, 150)));
        }

        GameObject Track(GameObject gameObject)
        {
            m_CreatedObjects.Add(gameObject);
            return gameObject;
        }
    }
}
