using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LogicCuteGuy.LCGUdonSharp.Installer
{
    // These identities are shared with SDK 3.10.5, existing worlds, and other packages.
    internal static class InstallerPayloadValidator
    {
        private static readonly Dictionary<string, string> RequiredGuids = new Dictionary<string, string>
        {
            { "Runtime/UdonSharp.Runtime.asmdef", "99835874ee819da44948776e0df4ff1d" },
            { "Editor/UdonSharp.Editor.asmdef", "84265b35cca3905448e623ef3903f0ff" },
            { "Runtime/Libraries/UdonSharp.Lib.asmdef", "e1a67d4778ffb214390ddafa61c1a557" },
            { "Editor/UdonSharpAssemblyDefinition.cs", "5136146375e9a0a498a72a0091b40cc1" },
            { "Editor/UdonSharpProgramAsset.cs", "c333ccfdd0cbdbc4ca30cef2dd6e6b9b" },
            { "UdonSharpLocator.asset", "de16ec4337e023649b01c85475dc06f9" },
        };

        internal static void Validate(string payload)
        {
            if (!Directory.Exists(payload))
                throw new DirectoryNotFoundException("LCGUdonSharp release is incomplete: missing " + payload +
                    ". Install a release ZIP built with Tools~/build_release.py, not a repository source archive.");

            ValidateFeatures(payload);
            RequireMeta(payload);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetDirectories(payload, "*", SearchOption.AllDirectories))
                if (IsImportedAsset(payload, path))
                    RequireMeta(path);
            foreach (string path in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
            {
                if (!IsImportedAsset(payload, path))
                    continue;
                if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    RequireMeta(path);
                    continue;
                }
                Match match = Regex.Match(File.ReadAllText(path), @"(?m)^guid: ([0-9a-f]{32})\r?$",
                    RegexOptions.IgnoreCase);
                if (!match.Success || !guids.Add(match.Groups[1].Value))
                    throw new InvalidDataException("Missing, invalid, or duplicate GUID in " + path);
            }
            foreach (var required in RequiredGuids)
            {
                string path = Path.Combine(payload, required.Key);
                if (!File.Exists(path) || !Regex.IsMatch(File.ReadAllText(path + ".meta"),
                        @"(?m)^guid: " + required.Value + @"\r?$"))
                    throw new InvalidDataException("LCGUdonSharp must preserve the SDK GUID for " + required.Key);
            }
            foreach (string name in new[] { "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll",
                         "System.Reflection.Metadata.dll", "System.Text.Encoding.CodePages.dll" })
            {
                string path = Path.Combine(payload, "Runtime", "Plugins", name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidDataException("LCGUdonSharp compiler dependency is missing or empty: " + path);
            }
        }

        // SDK GUIDs and Roslyn DLLs also exist in the stock compiler. They do not
        // prove that the payload contains the extensions used by our examples.
        internal static void ValidateFeatures(string payload)
        {
            var required = new Dictionary<string, string>
            {
                { "Runtime/UdonSharpAttributes.cs", "class LCGPacketAttribute" },
                { "Runtime/UdonSharpBehaviour.cs", "enum UdonExceptionKind" },
                { "Runtime/LCGBehaviours/LCGRuntime.cs", "class LCGRuntime" },
                { "Runtime/LCGBehaviours/LCGNetworkZone.cs", "class LCGNetworkZone" },
                { "Editor/Compiler/Lowering/CollectionSyntaxLowerer.cs", "class CollectionSyntaxLowerer" },
                { "Editor/Compiler/Binder/BoundNodes/BoundExceptionHandling.cs", "namespace UdonSharp.Compiler" },
            };
            foreach (var feature in required)
            {
                string path = Path.Combine(payload, feature.Key);
                if (!File.Exists(path) || !File.ReadAllText(path).Contains(feature.Value))
                    throw new InvalidDataException("LCGUdonSharp compiler feature is missing: " + feature.Key +
                        ". The payload must contain the LCG compiler, not the stock SDK compiler.");
            }
        }

        private static bool IsImportedAsset(string root, string path)
        {
            string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (part.StartsWith(".", StringComparison.Ordinal) || part.EndsWith("~", StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static void RequireMeta(string path)
        {
            if (!File.Exists(path + ".meta"))
                throw new InvalidDataException("LCGUdonSharp metadata is missing: " + path + ".meta");
        }
    }
}
