using ByteDance.PICO.MCPExtensions.Editor;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_HapticsEditorTests
    {
        GameObject m_Root;

        [SetUp]
        public void SetUp()
        {
            m_Root = new GameObject(PXR_MCP_Common.AgentOriginName);
            m_Root.AddComponent<XROrigin>();
            PXR_MCP_Haptics.RuntimeSupportOverride = true;
        }

        [TearDown]
        public void TearDown()
        {
            PXR_MCP_Haptics.RuntimeSupportOverride = null;
            if (m_Root != null) Object.DestroyImmediate(m_Root);
        }

        [Test]
        public void Attach_CreatesOneManagerOnOriginWithDefaultEffect()
        {
            var result = PXR_MCP_Haptics.Attach(Configuration("Hit"));

            Assert.That(result.ok, Is.True);
            Assert.That(result.changed, Is.True);
            Assert.That(result.attached, Is.True);
            Assert.That(result.managerPath, Is.EqualTo(PXR_MCP_Common.AgentOriginName));
            Assert.That(result.defaultEffect, Is.EqualTo("Hit"));
            Assert.That(result.effects, Has.Count.EqualTo(1));
            Assert.That(m_Root.GetComponents<PXR_MCP_HapticsManager>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void AttachSameConfiguration_IsIdempotent()
        {
            Assert.That(PXR_MCP_Haptics.Attach(Configuration("Hit")).changed, Is.True);
            var second = PXR_MCP_Haptics.Attach(Configuration("Hit"));

            Assert.That(second.ok, Is.True);
            Assert.That(second.changed, Is.False);
            Assert.That(m_Root.GetComponents<PXR_MCP_HapticsManager>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void UpsertEffect_AddsMultipleNamedEffectsToOneManager()
        {
            PXR_MCP_Haptics.Attach(Configuration("Hit"));
            var second = Configuration("Damage");
            second.target = PXR_MCP_HapticsTarget.Left;
            second.left.impulseAmplitude = 0.9f;

            var result = PXR_MCP_Haptics.UpsertEffect(second);

            Assert.That(result.ok, Is.True);
            Assert.That(result.effects, Has.Count.EqualTo(2));
            Assert.That(result.effects[1].name, Is.EqualTo("Damage"));
            Assert.That(result.effects[1].left.impulseAmplitude, Is.EqualTo(0.9f));
            Assert.That(m_Root.GetComponents<PXR_MCP_HapticsManager>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void AttachBoth_PreservesIndependentLeftRightParameters()
        {
            var configuration = Configuration("Asymmetric");
            configuration.left.impulseAmplitude = 0.2f;
            configuration.right.impulseAmplitude = 0.8f;
            configuration.left.impulseFrequencyHz = 100;
            configuration.right.impulseFrequencyHz = 300;

            var result = PXR_MCP_Haptics.Attach(configuration);

            Assert.That(result.effects[0].target, Is.EqualTo("Both"));
            Assert.That(result.effects[0].left.impulseAmplitude, Is.EqualTo(0.2f));
            Assert.That(result.effects[0].right.impulseAmplitude, Is.EqualTo(0.8f));
            Assert.That(result.effects[0].left.impulseFrequencyHz, Is.EqualTo(100));
            Assert.That(result.effects[0].right.impulseFrequencyHz, Is.EqualTo(300));
        }

        [Test]
        public void Attach_DoesNotRequireControllerGameObjects()
        {
            var result = PXR_MCP_Haptics.Attach(Configuration("NoControllerNodes"));

            Assert.That(result.ok, Is.True);
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Not.Null);
        }

        [Test]
        public void UpsertBeforeAttach_IsRejectedWithoutAddingManager()
        {
            var result = PXR_MCP_Haptics.UpsertEffect(Configuration("Hit"));

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("attach first"));
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Null);
        }

        [Test]
        public void InvalidEffect_IsAtomic()
        {
            var configuration = Configuration("Invalid");
            configuration.left.impulseAmplitude = 1.1f;

            var result = PXR_MCP_Haptics.Attach(configuration);

            Assert.That(result.ok, Is.False);
            Assert.That(result.changed, Is.False);
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Null);
        }

        [Test]
        public void Attach_WhenUserManagedManagerExists_DoesNotTakeOwnership()
        {
            var userManager = m_Root.AddComponent<PXR_MCP_HapticsManager>();

            var result = PXR_MCP_Haptics.Attach(Configuration("Hit"));

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("user-managed"));
            Assert.That(userManager.ManagedByMcp, Is.False);
        }

        [Test]
        public void RemoveEffect_LeavesManagerAndOtherEffects()
        {
            PXR_MCP_Haptics.Attach(Configuration("Hit"));
            PXR_MCP_Haptics.UpsertEffect(Configuration("Damage"));

            var result = PXR_MCP_Haptics.RemoveEffect("Hit");

            Assert.That(result.ok, Is.True);
            Assert.That(result.effects, Has.Count.EqualTo(1));
            Assert.That(result.effects[0].name, Is.EqualTo("Damage"));
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Not.Null);
        }

        [Test]
        public void Remove_DeletesOnlyMcpManagedManager()
        {
            PXR_MCP_Haptics.Attach(Configuration("Hit"));

            var result = PXR_MCP_Haptics.Remove();

            Assert.That(result.ok, Is.True);
            Assert.That(result.changed, Is.True);
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Null);
        }

        [Test]
        public void UnsupportedRuntime_RejectsMutationWithoutAddingManager()
        {
            PXR_MCP_Haptics.RuntimeSupportOverride = false;

            var result = PXR_MCP_Haptics.Attach(Configuration("Hit"));

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("PICO native runtime"));
            Assert.That(m_Root.GetComponent<PXR_MCP_HapticsManager>(), Is.Null);
        }

        [Test]
        public void AttachWithoutAgentOrigin_DoesNotCreateOne()
        {
            Object.DestroyImmediate(m_Root);
            m_Root = null;

            var result = PXR_MCP_Haptics.Attach(Configuration("Hit"));

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("No agent XR Origin"));
            Assert.That(Object.FindFirstObjectByType<PXR_MCP_HapticsManager>(), Is.Null);
        }

        [Test]
        public void StatusWithoutAgentOrigin_IsReadOnlyEmptySnapshot()
        {
            Object.DestroyImmediate(m_Root);
            m_Root = null;

            var result = PXR_MCP_Haptics.Status();

            Assert.That(result.ok, Is.True);
            Assert.That(result.attached, Is.False);
            Assert.That(result.reason, Does.Contain("No agent XR Origin"));
        }

        static PXR_MCP_HapticsEffectConfiguration Configuration(string name)
        {
            return new PXR_MCP_HapticsEffectConfiguration
            {
                name = name,
                effectType = PXR_MCP_HapticsEffectType.Impulse,
                target = PXR_MCP_HapticsTarget.Both,
                setDefault = true,
            };
        }
    }
}
