using System;
using System.Linq;
using System.Reflection;
using ByteDance.PICO.MCPExtensions.Tools;
using NUnit.Framework;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_ToolRegistrationTests
    {
        static readonly string[] ExpectedToolNames =
        {
            "pico_xr_vst",
            "pico_xr_controller",
            "pico_xr_haptics",
            "pico_xr_locomotion",
            "pico_xr_spatial_mesh",
            "pico_xr_plane",
            "pico_xr_hand",
            "pico_xr_grab",
            "pico_xr_package",
            "pico_xr_status"
        };

        [Test]
        public void PicoTools_AreAllEnabledByDefault()
        {
            var registrations = typeof(PXR_MCP_Tools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpToolAttribute>())
                .Where(attribute => attribute != null &&
                    attribute.Name.StartsWith("pico_xr_", StringComparison.Ordinal))
                .ToArray();

            CollectionAssert.AreEquivalent(
                ExpectedToolNames,
                registrations.Select(attribute => attribute.Name));

            foreach (var attribute in registrations)
            {
                Assert.That(
                    attribute.EnabledByDefault,
                    Is.True,
                    attribute.Name + " must be enabled by default.");
            }
        }
    }
}
