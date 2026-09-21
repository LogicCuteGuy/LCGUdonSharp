using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGExampleAssemblyTests
    {
        private const string AssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LogicCuteGuy.LCGUdonSharp.Examples.asmdef";
        private const string UdonSharpAssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset";
        private const string ExpectedAssemblyName = "LogicCuteGuy.LCGUdonSharp.Examples";
        private const string GenericRestrictionsGuidePath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/README.md";
        private const string AsyncExampleScriptPath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncYieldDelayExample.cs";

        private static readonly string[] ExampleScriptPaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGPacketFieldShowcase.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGPacketMethodShowcase.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGZoneObjectShowcase.cs",
        };

        private static readonly string[] GenericRestrictionScriptPaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/OpenGenericsExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/GenericBehavioursExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/GenericHeapObjectsExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/ListTypesExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/InterfaceMembersExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/MultipleConcreteBasesExample.cs",
        };

        [Test]
        public void GenericRestrictionExamples_AreConcreteUdonSharpScripts()
        {
            string guide = File.ReadAllText(GenericRestrictionsGuidePath);

            foreach (string scriptPath in GenericRestrictionScriptPaths)
            {
                Assert.That(File.Exists(scriptPath), Is.True, scriptPath + " is missing.");
                string resolvedAssembly = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath);
                Assert.That(Path.GetFileNameWithoutExtension(resolvedAssembly), Is.EqualTo(ExpectedAssemblyName));

                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Assert.That(script, Is.Not.Null, scriptPath + " was not imported by Unity.");
                Assert.That(typeof(UdonSharp.UdonSharpBehaviour).IsAssignableFrom(script.GetClass()), Is.True,
                    scriptPath + " is not a concrete UdonSharpBehaviour.");

                string programAssetPath = Path.ChangeExtension(scriptPath, ".asset");
                var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(programAssetPath);
                Assert.That(programAsset, Is.Not.Null, programAssetPath + " is missing.");
                Assert.That(programAsset.sourceCsScript, Is.SameAs(script),
                    programAssetPath + " points to the wrong source script.");
                Assert.That(guide, Does.Contain(Path.GetFileName(scriptPath)),
                    scriptPath + " is not linked from the guide.");
            }
        }

        [Test]
        public void GenericRestrictionExamples_CompileThroughUdonSharp()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);

            var cacheType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            object cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            MethodInfo getUasm = cacheType.GetMethod("GetUASMStr");

            foreach (string scriptPath in GenericRestrictionScriptPaths)
            {
                string programAssetPath = Path.ChangeExtension(scriptPath, ".asset");
                var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(programAssetPath);
                string assembly = (string)getUasm.Invoke(cache, new object[] { programAsset });
                Assert.That(assembly, Is.Not.Empty, programAssetPath + " emitted no UASM.");
                Assert.That(assembly, Does.Contain("_interact"),
                    programAssetPath + " did not emit its Interact entry point.");
            }
        }

        [Test]
        public void AsyncYieldDelayExample_CompilesIntoScheduledUdonContinuations()
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AsyncExampleScriptPath);
            Assert.That(script, Is.Not.Null, AsyncExampleScriptPath + " was not imported by Unity.");

            string programAssetPath = Path.ChangeExtension(AsyncExampleScriptPath, ".asset");
            var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(programAssetPath);
            Assert.That(programAsset, Is.Not.Null, programAssetPath + " is missing.");
            Assert.That(programAsset.sourceCsScript, Is.SameAs(script));

            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);

            var cacheType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            object cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            string assembly = (string)cacheType.GetMethod("GetUASMStr").Invoke(cache, new object[] { programAsset });
            Assert.That(assembly, Does.Contain("_interact"));
            Assert.That(assembly, Does.Contain("_Interact_resume"));
            Assert.That(assembly, Does.Contain("SendCustomEventDelayedFrames"));
            Assert.That(assembly, Does.Contain("SendCustomEventDelayedSeconds"));
        }

        [Test]
        public void GenericRestrictionsGuide_HasEverySectionAndBadGoodPair()
        {
            Assert.That(File.Exists(GenericRestrictionsGuidePath), Is.True,
                "The generic restriction example guide is missing.");

            string guide = File.ReadAllText(GenericRestrictionsGuidePath);
            string[] restrictionSections =
            {
                "## Open generics",
                "## Generic behaviours",
                "## Generic heap objects",
                "## List<T>",
                "## Unsupported interface members",
                "## Multiple concrete bases",
            };

            foreach (string section in restrictionSections)
            {
                int sectionStart = guide.IndexOf(section, System.StringComparison.Ordinal);
                Assert.That(sectionStart, Is.GreaterThanOrEqualTo(0), section + " example is missing.");

                int sectionEnd = guide.IndexOf("\n## ", sectionStart + section.Length,
                    System.StringComparison.Ordinal);
                if (sectionEnd < 0)
                    sectionEnd = guide.Length;

                string sectionBody = guide.Substring(sectionStart, sectionEnd - sectionStart);
                Assert.That(sectionBody, Does.Contain("**Rejected**"),
                    section + " needs a clearly labelled rejected example.");
                Assert.That(sectionBody, Does.Contain("**Replacement**"),
                    section + " needs a clearly labelled replacement example.");
            }

            string[] requiredTopics =
            {
                "## Task<T> compatibility note",
                "Static generic helpers",
                "Closed generic interfaces",
                "Composition",
            };

            foreach (string topic in requiredTopics)
                Assert.That(guide, Does.Contain(topic), topic + " example is missing.");
        }

        [Test]
        public void ExampleAssembly_IsRegisteredAsUdonSharpAssembly()
        {
            Object assemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(AssemblyPath);
            Object udonSharpAssemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(UdonSharpAssemblyPath);

            Assert.That(assemblyDefinition, Is.Not.Null, "Example asmdef is missing.");
            Assert.That(udonSharpAssemblyDefinition, Is.Not.Null,
                "Example UdonSharp assembly definition is missing.");

            SerializedObject serializedDefinition = new SerializedObject(udonSharpAssemblyDefinition);
            SerializedProperty sourceAssembly = serializedDefinition.FindProperty("sourceAssembly");

            Assert.That(sourceAssembly, Is.Not.Null,
                "Example registration is not a UdonSharpAssemblyDefinition asset.");
            Assert.That(sourceAssembly.objectReferenceValue, Is.SameAs(assemblyDefinition),
                "Example UdonSharp assembly registration points to the wrong asmdef.");

            foreach (string scriptPath in ExampleScriptPaths)
            {
                string resolvedAssembly = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath);
                Assert.That(Path.GetFileNameWithoutExtension(resolvedAssembly), Is.EqualTo(ExpectedAssemblyName),
                    scriptPath + " does not resolve to the registered example assembly.");
            }
        }

        [Test]
        public void NetworkingExamples_CompileThroughUdonSharp()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False,
                "Networking examples must complete Udon assembly generation, not just C# binding.");
        }

        [TestCase("LCGRuntime")]
        [TestCase("LCGRuntimePlayer")]
        [TestCase("LCGNetworkZone")]
        [TestCase("LCGZoneOwnershipGuard")]
        [TestCase("LCGManualObjectSync")]
        public void RuntimeBehaviour_HasMatchingMonoScript(string className)
        {
            string path = "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/" + className + ".cs";
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null, path);
            System.Type scriptClass = script.GetClass();
            Assert.That(scriptClass, Is.Not.Null, path + " must resolve to a Unity script class.");
            Assert.That(scriptClass.Name, Is.EqualTo(className));
            Assert.That(typeof(UdonSharp.UdonSharpBehaviour).IsAssignableFrom(scriptClass), Is.True);
        }

        [Test]
        public void RuntimeEnumSwitch_DoesNotCopyByteIntoInt32()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(
                "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LCGRuntime.asset");
            Assert.That(asset, Is.Not.Null);
            var cacheType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            var cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            string assembly = (string)cacheType.GetMethod("GetUASMStr").Invoke(cache, new object[] { asset });
            Assert.That(assembly, Does.Contain("SystemUInt32Array.__Get__SystemInt32__SystemUInt32"),
                "This regression requires the runtime's compiled enum switch jump tables.");
            Assert.That(assembly, Does.Contain("SystemConvert.__ToInt32__SystemByte__SystemInt32"));

            var types = new Dictionary<string, string>();
            foreach (Match declaration in Regex.Matches(assembly, @"(?m)^\s*(\w+): %(\w+),"))
                types[declaration.Groups[1].Value] = declaration.Groups[2].Value;
            foreach (Match copy in Regex.Matches(assembly, @"PUSH, (\w+)\s+PUSH, (\w+)\s+COPY"))
            {
                string source = copy.Groups[1].Value;
                string target = copy.Groups[2].Value;
                Assert.That(types[source] == "SystemByte" && types[target] == "SystemInt32", Is.False,
                    "Byte-backed enums must be converted, not copied, into integer slots: " + copy.Value);
            }
        }
    }
}
