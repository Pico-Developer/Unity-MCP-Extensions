using ByteDance.PICO.MCPExtensions.Editor;
using ByteDance.PICO.MCPExtensions.Tools;
using NUnit.Framework;

namespace ByteDance.PICO.MCPExtensions.Tests
{
    public class PXR_MCP_PackageToolTests
    {
        [Test]
        public void RemoveMissingPackage_ReportsNoChange()
        {
            const string packageName = "com.bytedance.pico.mcp-validation.definitely-missing";
            var envelope = PXR_MCP_Tools.PicoXrPackage(new PXR_MCP_Tools.PackageParams
            {
                Action = "Remove",
                PackageName = packageName,
            }) as PXR_MCP_Result;

            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope.status, Is.EqualTo(PXR_MCP_Result.StatusOk));
            Assert.That(envelope.summary, Is.EqualTo(packageName + " was not installed; no change made."));

            var data = envelope.data as PXR_MCP_PackageOps.PackageResult;
            Assert.That(data, Is.Not.Null);
            Assert.That(data.ok, Is.True);
            Assert.That(data.changed, Is.False);
            Assert.That(data.previousVersion, Is.Null);
        }
    }
}
