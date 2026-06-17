// PXR_MCP_PackageOps.cs
// Step 1 sub-system: package & sample management.
//
// Design notes:
//   * Wraps UnityEditor.PackageManager.Client + PackageManager.UI.Sample only.
//   * Never edits Packages/manifest.json by hand — let Client resolve deps & lockfile.
//   * Blocking-style helpers (loop on Request.IsCompleted, 60s timeout) so MenuItems
//     get an immediate result. Underlying async Request stays accessible if needed.
//   * Idempotent:
//       - Add: already-installed-and-version-matches  -> success no-op
//       - Remove: not-installed                       -> success no-op
//       - ImportSample: importPath exists on disk     -> success no-op
//   * No hardcoded versions. Caller supplies "name" or "name@version".
//     If version omitted, registry resolves "latest stable".
//   * No Editor restart triggered here. Caller decides.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Sample = UnityEditor.PackageManager.UI.Sample;

namespace ByteDance.PICO.MCPExtensions.Editor
{
    public static class PXR_MCP_PackageOps
    {
        public const int DefaultTimeoutMs = 60_000;

        // -----------------------------------------------------------------
        // Result types (plain POCOs so Step 2 can serialize them directly).
        // -----------------------------------------------------------------
        public class PackageResult
        {
            public bool ok;
            public string packageName;
            public string version;       // resolved version if ok==true; null otherwise
            public string previousVersion; // populated when an Add changed the version
            public bool alreadyPresent;  // true => no-op happened (idempotent path)
            public string error;         // populated when ok==false
        }

        public class PackageInfoLite
        {
            public string name;
            public string displayName;
            public string version;
            public string source;     // Registry / Embedded / Local / Git ...
            public string resolvedPath;
        }

        public class SampleInfoLite
        {
            public string packageName;
            public string packageVersion;
            public string displayName;
            public string description;
            public bool imported;
            public string importPath;
        }

        public class SampleImportResult
        {
            public bool ok;
            public string packageName;
            public string sampleName;
            public string importPath;
            public bool alreadyPresent;
            public bool skipped;        // true => skipped because of a missing prerequisite (e.g. package not installed)
            public string warning;      // human-readable reason when skipped==true
            public string error;        // populated when ok==false
        }

        // -----------------------------------------------------------------
        // Query
        // -----------------------------------------------------------------

        // Returns metadata for one installed package, or null if not installed.
        public static PackageInfoLite GetInstalled(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName)) return null;
            var listed = ListInstalled();
            return listed.FirstOrDefault(p => string.Equals(p.name, packageName, StringComparison.OrdinalIgnoreCase));
        }

        // Returns all currently installed packages (project + embedded + built-in).
        public static List<PackageInfoLite> ListInstalled(int timeoutMs = DefaultTimeoutMs)
        {
            var req = Client.List(offlineMode: true, includeIndirectDependencies: false);
            if (!WaitFor(req, timeoutMs)) return new List<PackageInfoLite>();
            if (req.Status != StatusCode.Success || req.Result == null) return new List<PackageInfoLite>();
            return req.Result.Select(ToLite).ToList();
        }

        // -----------------------------------------------------------------
        // Add / Remove / Switch version
        // -----------------------------------------------------------------

        // identifier may be "com.unity.xr.interaction.toolkit" or "com.unity.xr.hands@1.5.0"
        // or any git URL / tarball Client.Add accepts.
        public static PackageResult Add(string identifier, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return new PackageResult { ok = false, error = "identifier is empty" };

            SplitIdentifier(identifier, out var name, out var requestedVersion);

            var current = !string.IsNullOrEmpty(name) ? GetInstalled(name) : null;
            if (current != null)
            {
                // Already installed; skip if no version change requested or versions equal.
                if (string.IsNullOrEmpty(requestedVersion) ||
                    string.Equals(current.version, requestedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new PackageResult
                    {
                        ok = true,
                        packageName = current.name,
                        version = current.version,
                        alreadyPresent = true,
                    };
                }
            }

            var req = Client.Add(identifier);
            if (!WaitFor(req, timeoutMs))
                return new PackageResult { ok = false, packageName = name, error = "timeout waiting for Client.Add" };
            if (req.Status != StatusCode.Success || req.Result == null)
                return new PackageResult
                {
                    ok = false,
                    packageName = name,
                    error = req.Error != null ? req.Error.message : "Client.Add failed",
                };

            return new PackageResult
            {
                ok = true,
                packageName = req.Result.name,
                version = req.Result.version,
                previousVersion = current != null ? current.version : null,
                alreadyPresent = false,
            };
        }

        public static PackageResult Remove(string packageName, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return new PackageResult { ok = false, error = "packageName is empty" };

            var current = GetInstalled(packageName);
            if (current == null)
            {
                return new PackageResult
                {
                    ok = true,
                    packageName = packageName,
                    alreadyPresent = false,
                    version = null,
                };
            }

            var req = Client.Remove(packageName);
            if (!WaitFor(req, timeoutMs))
                return new PackageResult { ok = false, packageName = packageName, error = "timeout waiting for Client.Remove" };
            if (req.Status != StatusCode.Success)
                return new PackageResult
                {
                    ok = false,
                    packageName = packageName,
                    error = req.Error != null ? req.Error.message : "Client.Remove failed",
                };

            return new PackageResult
            {
                ok = true,
                packageName = packageName,
                previousVersion = current.version,
                version = null,
            };
        }

        // Switch to a specific version. Same as Add("name@version") but signature is clearer.
        public static PackageResult Update(string packageName, string version, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return new PackageResult { ok = false, error = "packageName is empty" };
            var identifier = string.IsNullOrWhiteSpace(version) ? packageName : packageName + "@" + version;
            return Add(identifier, timeoutMs);
        }

        // -----------------------------------------------------------------
        // Samples
        // -----------------------------------------------------------------

        // List samples declared by a given package@version. If version is null, the currently-installed version is used.
        public static List<SampleInfoLite> ListSamples(string packageName, string version = null)
        {
            if (string.IsNullOrWhiteSpace(packageName)) return new List<SampleInfoLite>();
            if (string.IsNullOrWhiteSpace(version))
            {
                var info = GetInstalled(packageName);
                if (info == null) return new List<SampleInfoLite>();
                version = info.version;
            }

            var samples = Sample.FindByPackage(packageName, version);
            var list = new List<SampleInfoLite>();
            foreach (var s in samples)
            {
                list.Add(new SampleInfoLite
                {
                    packageName = packageName,
                    packageVersion = version,
                    displayName = s.displayName,
                    description = s.description,
                    importPath = s.importPath,
                    imported = !string.IsNullOrEmpty(s.importPath) && Directory.Exists(s.importPath),
                });
            }
            return list;
        }

        // Import (= copy) a sample into Assets/Samples/<package>/<version>/<sample>.
        // overwrite=false honours the "already imported" idempotent path.
        public static SampleImportResult ImportSample(string packageName, string sampleName, bool overwrite = false, string version = null)
        {
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(sampleName))
                return new SampleImportResult { ok = false, packageName = packageName, sampleName = sampleName, error = "packageName or sampleName is empty" };

            if (string.IsNullOrWhiteSpace(version))
            {
                var info = GetInstalled(packageName);
                if (info == null)
                    return new SampleImportResult
                    {
                        ok = true,           // not a hard failure — just nothing to do
                        skipped = true,
                        packageName = packageName,
                        sampleName = sampleName,
                        warning = "package '" + packageName + "' is not installed; install it before importing samples",
                    };
                version = info.version;
            }

            var match = Sample.FindByPackage(packageName, version)
                .FirstOrDefault(s => string.Equals(s.displayName, sampleName, StringComparison.OrdinalIgnoreCase));
            if (match.Equals(default(Sample)))
                return new SampleImportResult { ok = false, packageName = packageName, sampleName = sampleName, error = "sample not found" };

            var already = !string.IsNullOrEmpty(match.importPath) && Directory.Exists(match.importPath);
            if (already && !overwrite)
            {
                return new SampleImportResult
                {
                    ok = true,
                    packageName = packageName,
                    sampleName = sampleName,
                    importPath = match.importPath,
                    alreadyPresent = true,
                };
            }

            bool imported;
            try { imported = match.Import(overwrite ? Sample.ImportOptions.OverridePreviousImports : Sample.ImportOptions.None); }
            catch (Exception e)
            {
                return new SampleImportResult { ok = false, packageName = packageName, sampleName = sampleName, error = e.Message };
            }

            if (!imported)
                return new SampleImportResult { ok = false, packageName = packageName, sampleName = sampleName, error = "Sample.Import returned false" };

            AssetDatabase.Refresh();
            return new SampleImportResult
            {
                ok = true,
                packageName = packageName,
                sampleName = sampleName,
                importPath = match.importPath,
                alreadyPresent = false,
            };
        }

        // -----------------------------------------------------------------
        // MenuItem shortcuts (manual smoke tests for Step 1).
        // -----------------------------------------------------------------

        const string XRI = "com.unity.xr.interaction.toolkit";
        const string XR_HANDS = "com.unity.xr.hands";

        [MenuItem("PICO MCP/Packages/Show Installed (Console)")]
        static void Menu_ShowInstalled()
        {
            var list = ListInstalled();
            Debug.Log("[PICO MCP] Installed packages (" + list.Count + "):\n" +
                      string.Join("\n", list.Select(p => "  " + p.name + "@" + p.version + "  [" + p.source + "]")));
        }

        [MenuItem("PICO MCP/Packages/XRI/Install (latest)")]
        static void Menu_AddXRI() => LogResult(Add(XRI));

        [MenuItem("PICO MCP/Packages/XRI/Remove")]
        static void Menu_RemoveXRI() => LogResult(Remove(XRI));

        [MenuItem("PICO MCP/Packages/XRI/List Samples (Console)")]
        static void Menu_ListXRISamples()
        {
            var samples = ListSamples(XRI);
            Debug.Log("[PICO MCP] " + XRI + " samples (" + samples.Count + "):\n" +
                      string.Join("\n", samples.Select(s => (s.imported ? "[x] " : "[ ] ") + s.displayName)));
        }

        [MenuItem("PICO MCP/Packages/XRI/Import Sample 'Starter Assets'")]
        static void Menu_ImportXRIStarter() => LogResult(ImportSample(XRI, "Starter Assets"));

        [MenuItem("PICO MCP/Packages/XR Hands/Install (latest)")]
        static void Menu_AddXRHands() => LogResult(Add(XR_HANDS));

        [MenuItem("PICO MCP/Packages/XR Hands/Remove")]
        static void Menu_RemoveXRHands() => LogResult(Remove(XR_HANDS));

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        static bool WaitFor(Request req, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!req.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) return false;
                Thread.Sleep(50);
            }
            return true;
        }

        static void SplitIdentifier(string identifier, out string name, out string version)
        {
            var at = identifier.IndexOf('@');
            if (at < 0) { name = identifier.Trim(); version = null; return; }
            // Heuristic: only treat the `@` as a version separator if the prefix looks like a reverse-DNS package id.
            // git URLs may start with "git@github.com:..." — leave those untouched.
            var head = identifier.Substring(0, at);
            if (head.Contains('.') && !head.Contains('/') && !head.Contains(':'))
            {
                name = head.Trim();
                version = identifier.Substring(at + 1).Trim();
            }
            else
            {
                name = identifier.Trim();
                version = null;
            }
        }

        static PackageInfoLite ToLite(PackageInfo p) => new PackageInfoLite
        {
            name = p.name,
            displayName = p.displayName,
            version = p.version,
            source = p.source.ToString(),
            resolvedPath = p.resolvedPath,
        };

        static void LogResult(PackageResult r)
        {
            if (r.ok)
                Debug.Log("[PICO MCP] Package OK: " + r.packageName + (r.version != null ? "@" + r.version : "") +
                          (r.alreadyPresent ? " (already present)" : "") +
                          (r.previousVersion != null ? " (was " + r.previousVersion + ")" : ""));
            else
                Debug.LogError("[PICO MCP] Package FAIL: " + r.packageName + " -> " + r.error);
        }

        static void LogResult(SampleImportResult r)
        {
            if (r.skipped)
            {
                Debug.LogWarning("[PICO MCP] Sample SKIP: " + r.packageName + " :: " + r.sampleName + " -> " + r.warning);
                return;
            }
            if (r.ok)
                Debug.Log("[PICO MCP] Sample OK: " + r.packageName + " :: " + r.sampleName +
                          (r.alreadyPresent ? " (already present)" : "") + " -> " + r.importPath);
            else
                Debug.LogError("[PICO MCP] Sample FAIL: " + r.packageName + " :: " + r.sampleName + " -> " + r.error);
        }
    }
}
