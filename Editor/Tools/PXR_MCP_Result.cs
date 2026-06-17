// PXR_MCP_Result.cs
// Step 2: shared result envelope returned by every PICO MCP tool.
//
// Why a dedicated type instead of reusing Step 1 POCOs:
//   * MCP clients (LLMs) need a *uniform* shape across all tools so they can
//     reliably parse status / summary regardless of which tool ran.
//   * `summary` is the LLM-facing one-liner the model can echo to the user.
//   * `data` carries the raw Step 1 result so the LLM can drill in if needed.
//   * `status` is a string enum, easier for LLM tool-call routing than booleans.
//
// Compiled into the Tools asmdef. The asmdef itself references
// Unity.AI.MCP.Editor, so its presence implicitly requires AI Assistant 2.x.

using System;

namespace ByteDance.PICO.MCPExtensions.Tools
{
    public class PXR_MCP_Result
    {
        // -------------------------------------------------------------
        // Status vocabulary (kept as plain strings for JSON friendliness)
        // -------------------------------------------------------------
        public const string StatusOk             = "ok";
        public const string StatusAlreadyPresent = "already_present";
        public const string StatusSkipped        = "skipped";
        public const string StatusError          = "error";

        public string status;     // one of the Status* constants
        public string summary;    // human-readable one-liner for the LLM to echo
        public string warning;    // populated when status == skipped
        public string error;      // populated when status == error
        public object data;       // raw Step 1 result, so the LLM can introspect

        // -------------------------------------------------------------
        // Factory helpers
        // -------------------------------------------------------------
        public static PXR_MCP_Result Ok(string summary, object data = null)
            => new PXR_MCP_Result { status = StatusOk, summary = summary, data = data };

        public static PXR_MCP_Result AlreadyPresent(string summary, object data = null)
            => new PXR_MCP_Result { status = StatusAlreadyPresent, summary = summary, data = data };

        public static PXR_MCP_Result Skipped(string summary, string warning, object data = null)
            => new PXR_MCP_Result { status = StatusSkipped, summary = summary, warning = warning, data = data };

        public static PXR_MCP_Result Error(string summary, string error, object data = null)
            => new PXR_MCP_Result { status = StatusError, summary = summary, error = error, data = data };

        public static PXR_MCP_Result FromException(string toolName, Exception ex)
            => new PXR_MCP_Result
            {
                status  = StatusError,
                summary = "[" + toolName + "] threw " + ex.GetType().Name,
                error   = ex.Message,
            };
    }
}
