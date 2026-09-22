using ByteDance.PICO.MCPExtensions.Editor;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_HapticsEditorTests
    {
        GameObject m_Root;
        GameObject m_Left;
        GameObject m_Right;

        [SetUp]
        public void SetUp()
        {
            m_Root = new GameObject(PXR_MCP_Common.AgentOriginName);
            m_Root.AddComponent<XROrigin>();
            var offset = new GameObject(PXR_MCP_Common.CameraOffsetName);
            offset.transform.SetParent(m_Root.transform);
            m_Left = new GameObject(PXR_MCP_Common.LeftControllerName);
            m_Left.transform.SetParent(offset.transform);
            m_Right = new GameObject(PXR_MCP_Common.RightControllerName);
            m_Right.transform.SetParent(offset.transform);
            PXR_MCP_Haptics.RuntimeSupportOverride = true;
        }

        [TearDown]
        public void TearDown()
        {
            PXR_MCP_Haptics.RuntimeSupportOverride = null;
            if (m_Root != null) Object.DestroyImmediate(m_Root);
        }

        [Test]
        public void AttachBoth_CreatesOneManagedComponentPerController()
        {
            var result = PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Both, 0.75f, 80, 200);

            Assert.That(result.ok, Is.True);
            Assert.That(result.changed, Is.True);
            AssertConfigured(m_Left, PXR_MCP_ControllerSide.Left, 0.75f, 80, 200);
            AssertConfigured(m_Right, PXR_MCP_ControllerSide.Right, 0.75f, 80, 200);
        }

        [Test]
        public void AttachSameConfiguration_IsIdempotent()
        {
            Assert.That(PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Left).changed, Is.True);
            var second = PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Left);

            Assert.That(second.ok, Is.True);
            Assert.That(second.changed, Is.False);
            Assert.That(m_Left.GetComponents<PXR_MCP_ControllerHaptics>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void Configure_UpdatesOnlyProvidedValues()
        {
            PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Right, 0.4f, 90, 140);
            var result = PXR_MCP_Haptics.Configure(PXR_MCP_HapticsTarget.Right, amplitude: 0.9f);

            Assert.That(result.ok, Is.True);
            AssertConfigured(m_Right, PXR_MCP_ControllerSide.Right, 0.9f, 90, 140);
        }

        [Test]
        public void ConfigureBeforeAttach_IsRejectedWithoutAddingAComponent()
        {
            var result = PXR_MCP_Haptics.Configure(PXR_MCP_HapticsTarget.Left, amplitude: 0.9f);

            Assert.That(result.ok, Is.False);
            Assert.That(result.changed, Is.False);
            Assert.That(result.error, Does.Contain("attach first"));
            Assert.That(m_Left.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
        }

        [Test]
        public void AttachBoth_WithInvalidParameters_IsAtomic()
        {
            var result = PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Both, 1.1f, 100, 150);

            Assert.That(result.ok, Is.False);
            Assert.That(result.changed, Is.False);
            Assert.That(m_Left.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
            Assert.That(m_Right.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
        }

        [Test]
        public void Attach_WhenUserManagedComponentExists_DoesNotTakeOwnership()
        {
            var userComponent = m_Left.AddComponent<PXR_MCP_ControllerHaptics>();

            var result = PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Left);

            Assert.That(result.ok, Is.False);
            Assert.That(result.changed, Is.False);
            Assert.That(result.error, Does.Contain("user-managed"));
            Assert.That(userComponent.ManagedByMcp, Is.False);
        }

        [Test]
        public void Remove_DeletesOnlyMcpManagedComponents()
        {
            var userComponent = m_Left.AddComponent<PXR_MCP_ControllerHaptics>();
            PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Right);

            var result = PXR_MCP_Haptics.Remove(PXR_MCP_HapticsTarget.Both);

            Assert.That(result.ok, Is.True);
            Assert.That(result.changed, Is.True);
            Assert.That(m_Left.GetComponent<PXR_MCP_ControllerHaptics>(), Is.SameAs(userComponent));
            Assert.That(m_Right.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
        }

        [Test]
        public void UnsupportedRuntime_RejectsMutationWithoutAddingComponents()
        {
            PXR_MCP_Haptics.RuntimeSupportOverride = false;

            var result = PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Both);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("PICO native runtime"));
            Assert.That(m_Left.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
            Assert.That(m_Right.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
        }

        [Test]
        public void Remove_IsAvailableAfterRuntimeChanges()
        {
            PXR_MCP_Haptics.Attach(PXR_MCP_HapticsTarget.Left);
            PXR_MCP_Haptics.RuntimeSupportOverride = false;

            var result = PXR_MCP_Haptics.Remove(PXR_MCP_HapticsTarget.Left);

            Assert.That(result.ok, Is.True);
            Assert.That(result.changed, Is.True);
            Assert.That(m_Left.GetComponent<PXR_MCP_ControllerHaptics>(), Is.Null);
        }

        [Test]
        public void RuntimeSupport_MatchesTheActivePicoRuntimeDefine()
        {
            PXR_MCP_Haptics.RuntimeSupportOverride = null;
#if ENABLE_PICO_XR_SDK
            Assert.That(PXR_MCP_Haptics.IsRuntimeSupported, Is.True);
#else
            Assert.That(PXR_MCP_Haptics.IsRuntimeSupported, Is.False);
#endif
        }

        [Test]
        public void StatusWithoutAgentOrigin_IsAReadOnlyEmptySnapshot()
        {
            Object.DestroyImmediate(m_Root);
            m_Root = null;

            var result = PXR_MCP_Haptics.Status(PXR_MCP_HapticsTarget.Both);

            Assert.That(result.ok, Is.True);
            Assert.That(result.controllers, Is.Empty);
            Assert.That(result.reason, Does.Contain("No agent XR Origin"));
        }

        static void AssertConfigured(
            GameObject gameObject,
            PXR_MCP_ControllerSide side,
            float amplitude,
            int durationMs,
            int frequencyHz)
        {
            var components = gameObject.GetComponents<PXR_MCP_ControllerHaptics>();
            Assert.That(components, Has.Length.EqualTo(1));
            Assert.That(components[0].ManagedByMcp, Is.True);
            Assert.That(components[0].Controller, Is.EqualTo(side));
            Assert.That(components[0].Amplitude, Is.EqualTo(amplitude));
            Assert.That(components[0].DurationMs, Is.EqualTo(durationMs));
            Assert.That(components[0].FrequencyHz, Is.EqualTo(frequencyHz));
        }
    }
}
