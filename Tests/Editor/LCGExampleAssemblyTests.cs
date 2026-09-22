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
using VRC.SDK3.Data;

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

        private static readonly string[] AsyncSdkExampleScriptPaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncStringDownloadExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncImageDownloadExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncVideoLoadExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncVideoEndExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncGpuReadbackExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncSerializationExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncAvailableProductsExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncPurchasesExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/AsyncAwait/AsyncProductOwnersExample.cs",
        };

        private static readonly string[] ExtendedLanguageExamplePaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/RefOutExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/GenericHierarchyExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/LinqClosureExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/DynamicExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/SpanExample.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/ExtendedLanguage/ExceptionHandlingExample.cs",
        };

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
                if (scriptPath.EndsWith("ListTypesExample.cs", System.StringComparison.Ordinal))
                {
                    Assert.That(assembly, Does.Contain("VRCSDK3DataDataList"));
                    Assert.That(assembly, Does.Contain("VRCSDK3DataDataDictionary"));
                    Assert.That(assembly, Does.Contain("VRCSDK3DataVRCJson.__TrySerializeToJson"));
                    Assert.That(assembly, Does.Contain("VRCSDK3DataVRCJson.__TryDeserializeFromJson"));
                    Assert.That(assembly, Does.Contain("VRCSDK3DataDataToken.__Bitcast"));
                    Assert.That(assembly, Does.Contain("__lcg_sync_syncedValues"));
                }
            }
        }

        [Test]
        public void CollectionJsonBinaryAndSyncExample_ExecutesInUdonVm()
        {
            const string assetPath =
                "Packages/com.logiccuteguy.lcgudonsharp/Example/GenericRestrictions/ListTypesExample.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(assetPath);
            Assert.That(asset, Is.Not.Null);

            var program = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);

            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("RunCollectionValidation"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<int>(
                program.SymbolTable.GetAddressFromSymbol("jsonRoundTripCount")), Is.EqualTo(4));
            Assert.That(program.Heap.GetHeapVariable<int>(
                program.SymbolTable.GetAddressFromSymbol("binaryRoundTripValue")), Is.EqualTo(1065353465));

            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_onPreSerialization"));
            Assert.That(vm.Interpret(), Is.Zero);
            string payload = program.Heap.GetHeapVariable<string>(
                program.SymbolTable.GetAddressFromSymbol("__lcg_sync_syncedValues"));
            Assert.That(payload, Is.EqualTo("[]"));

            program.Heap.SetHeapVariable(
                program.SymbolTable.GetAddressFromSymbol("__lcg_sync_syncedValues"), "[5,6]");
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_onDeserialization"));
            Assert.That(vm.Interpret(), Is.Zero);
            DataList values = program.Heap.GetHeapVariable<DataList>(
                program.SymbolTable.GetAddressFromSymbol("syncedValues"));
            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That((int)values[0], Is.EqualTo(5));
            Assert.That((int)values[1], Is.EqualTo(6));

            program.Heap.SetHeapVariable(
                program.SymbolTable.GetAddressFromSymbol("__lcg_sync_syncedValues"), "{");
            LogAssert.Expect(LogType.Error,
                new Regex("Failed to deserialize synchronized collection 'syncedValues':"));
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_onDeserialization"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<DataList>(
                program.SymbolTable.GetAddressFromSymbol("syncedValues")).Count, Is.EqualTo(2));

            program.Heap.SetHeapVariable(
                program.SymbolTable.GetAddressFromSymbol("__lcg_sync_syncedValues"), "");
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_onDeserialization"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<DataList>(
                program.SymbolTable.GetAddressFromSymbol("syncedValues")), Is.Null);
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
        public void AsyncSdkExamples_CompileIntoLegacyCallbacksAndAwaitContinuations()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);

            var cacheType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            object cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            MethodInfo getUasm = cacheType.GetMethod("GetUASMStr");

            foreach (string scriptPath in AsyncSdkExampleScriptPaths)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Assert.That(script, Is.Not.Null, scriptPath + " was not imported by Unity.");

                string programAssetPath = Path.ChangeExtension(scriptPath, ".asset");
                var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(programAssetPath);
                Assert.That(programAsset, Is.Not.Null, programAssetPath + " is missing.");
                Assert.That(programAsset.sourceCsScript, Is.SameAs(script));

                string assembly = (string)getUasm.Invoke(cache, new object[] { programAsset });
                Assert.That(assembly, Does.Contain("_interact"));
                Assert.That(assembly, Does.Contain("_Interact_resume"));
                GetAsyncSdkMarkers(scriptPath, out string operationMarker, out string callbackMarker);
                Assert.That(assembly, Does.Contain(operationMarker));
                Assert.That(assembly, Does.Contain(callbackMarker));
            }
        }

        private static void GetAsyncSdkMarkers(string scriptPath, out string operation, out string callback)
        {
            if (scriptPath.Contains("String")) { operation = "LoadUrl"; callback = "_onStringLoadSuccess"; }
            else if (scriptPath.Contains("Image")) { operation = "DownloadImage"; callback = "_onImageLoadSuccess"; }
            else if (scriptPath.Contains("VideoLoad")) { operation = "LoadURL"; callback = "_onVideoReady"; }
            else if (scriptPath.Contains("VideoEnd")) { operation = "_Interact_resume"; callback = "_onVideoEnd"; }
            else if (scriptPath.Contains("Gpu")) { operation = "VRCAsyncGPUReadback"; callback = "_onAsyncGpuReadbackComplete"; }
            else if (scriptPath.Contains("Serialization")) { operation = "RequestSerialization"; callback = "_onPostSerialization"; }
            else if (scriptPath.Contains("AvailableProducts")) { operation = "ListAvailableProducts"; callback = "_onListAvailableProducts"; }
            else if (scriptPath.Contains("Purchases")) { operation = "ListPurchases"; callback = "_onListPurchases"; }
            else { operation = "ListProductOwners"; callback = "_onListProductOwners"; }
        }

        [Test]
        public void RefOutAndGenericHierarchyExamples_CompileThroughUdonSharp()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);

            var cacheType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            object cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            MethodInfo getUasm = cacheType.GetMethod("GetUASMStr");

            foreach (string scriptPath in ExtendedLanguageExamplePaths)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Assert.That(script, Is.Not.Null, scriptPath + " was not imported by Unity.");

                string programAssetPath = Path.ChangeExtension(scriptPath, ".asset");
                var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(programAssetPath);
                Assert.That(programAsset, Is.Not.Null, programAssetPath + " is missing.");
                Assert.That(programAsset.sourceCsScript, Is.SameAs(script));

                string assembly = (string)getUasm.Invoke(cache, new object[] { programAsset });
                Assert.That(assembly, Is.Not.Empty, programAssetPath + " emitted no UASM.");
                Assert.That(assembly, Does.Contain("_interact"));
            }
        }

        [Test]
        public void ExceptionHandlingExample_ExecutesCompilerManagedFailuresInUdonVm()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();
            Assert.That(UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);

            System.Type behaviourType = System.Type.GetType(
                "LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage.ExceptionHandlingExample, LogicCuteGuy.LCGUdonSharp.Examples",
                true);
            var asset = UdonSharp.UdonSharpProgramAsset.GetProgramAssetForClass(behaviourType);
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_interact"));
            Assert.That(vm.Interpret(), Is.Zero);

            var heap = program.Heap;
            var symbols = program.SymbolTable;
            const int ExpectedAggregateResult = 1111111135;
            Assert.That(heap.GetHeapVariable<int>(symbols.GetAddressFromSymbol("result")), Is.EqualTo(ExpectedAggregateResult));
            Assert.That(heap.GetHeapVariable<int>(symbols.GetAddressFromSymbol("finallyCount")), Is.EqualTo(211));
            Assert.That(heap.GetHeapVariable<int>(symbols.GetAddressFromSymbol("sideEffectCount")), Is.EqualTo(1));
            Assert.That(heap.GetHeapVariable<string>(symbols.GetAddressFromSymbol("message")), Is.EqualTo("explicit payload"));
            Assert.That(heap.GetHeapVariable<int>(symbols.GetAddressFromSymbol("code")), Is.EqualTo(17));
            Assert.That(heap.GetHeapVariable<string>(symbols.GetAddressFromSymbol("operation")), Is.EqualTo("demo"));
            foreach (string caughtFlag in new[]
                     {
                         "argumentCaught", "arrayCaught", "stringCaught", "divideCaught", "moduloCaught",
                         "nullCaught", "negativeWriteCaught", "rethrowCaught", "finallyOverrideCaught",
                     })
            {
                Assert.That(heap.GetHeapVariable<bool>(symbols.GetAddressFromSymbol(caughtFlag)), Is.True, caughtFlag);
            }
        }

        [Test]
        public void ExceptionHandlingExample_UncaughtFailureLogsClearsAndAllowsLaterEvent()
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            LogAssert.NoUnexpectedReceived();

            System.Type behaviourType = System.Type.GetType(
                "LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage.ExceptionHandlingExample, LogicCuteGuy.LCGUdonSharp.Examples",
                true);
            var asset = UdonSharp.UdonSharpProgramAsset.GetProgramAssetForClass(behaviourType);
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);

            LogAssert.Expect(LogType.Error, "uncaught compiler-managed failure");
            LogAssert.Expect(LogType.Error, "uncaught-demo");
            LogAssert.Expect(LogType.Error, "5");
            LogAssert.Expect(LogType.Error, "91");
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("ThrowUncaught"));
            Assert.That(vm.Interpret(), Is.Zero);

            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_interact"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<int>(
                program.SymbolTable.GetAddressFromSymbol("result")), Is.EqualTo(1111111135));
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

            Assert.That(guide, Does.Contain("## Collections, JSON, bytes, and bits"));
            Assert.That(guide, Does.Contain("List<int> values"));
            Assert.That(guide, Does.Contain("Dictionary<string, int> scores"));
            Assert.That(guide, Does.Contain("IList<int> interfaceValues"));

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
