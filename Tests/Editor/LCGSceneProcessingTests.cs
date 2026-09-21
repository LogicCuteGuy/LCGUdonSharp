using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGSceneProcessingTests
    {
        [OneTimeSetUp]
        public void CompilePrograms()
        {
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            Assert.That(UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False,
                "PlayerObject support must compile to Udon, not only to C#.");
        }

        [TestCase(1, false, 0)]
        [TestCase(150, false, 256)]
        [TestCase(150, true, int.MaxValue)]
        [TestCase(150, true, int.MinValue)]
        public void PacketBytePacking_ExecutesInUdonVmWithNegativeMarkerAndUnicodeLength(int length, bool unicode, int zoneId)
        {
            var asset = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(LCGRuntime));
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            var heap = program.Heap;
            var symbols = program.SymbolTable;
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("receivers"), new VRC.Udon.UdonBehaviour[1]);
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("playerObjectReceivers"), new[] { false });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("__lcgSenderReceiverId"), 0);
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("__lcgSenderArgCount"), 0);
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("__lcgSenderZoneId"), zoneId);
            string address = new string(unicode ? '\u0e01' : 'A', length);
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("__lcgSenderAddress"), address);
            // Avoid scheduling a Unity event: execute just the actual packing and queue path.
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("methodFlushScheduled"), true);
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("__lcgSendMethod"));
            Assert.That(vm.Interpret(), Is.Zero);
            var frames = heap.GetHeapVariable<object[]>(symbols.GetAddressFromSymbol("pendingMethodFrames"));
            var frame = (byte[])frames[0];
            Assert.That(frame, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(frame, 24), Is.EqualTo(-1));
            Assert.That(BitConverter.ToInt32(frame, 4), Is.EqualTo(zoneId));
            Assert.That(frame[20] | frame[21] << 8, Is.EqualTo(length * 2));
            Assert.That(System.Text.Encoding.Unicode.GetString(frame, LCGRuntime.HeaderSize, length * 2), Is.EqualTo(address));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SceneProcessing_RegistersPlayerObjectReceiversOnRootAndChildren(bool onChild)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("User PlayerObject");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<VRC.SDK3.Components.VRCPlayerObject>();
                GameObject target = root;
                if (onChild)
                {
                    target = new GameObject("Child receiver");
                    target.transform.SetParent(root.transform);
                }
                target.AddUdonSharpComponent<LCGManualObjectSync>();
                root.SetActive(false);
                CreateProcessor().OnProcessScene(scene, null);
                LCGRuntime runtime = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<LCGRuntime>(true)).Single();
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(runtime);
                Assert.That(GetSerializedVariable(backing, "playerObjectReceivers"), Is.EqualTo(new[] { true }));
            }
            finally { CloseAfterBehaviourSetup(scene); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerObjectZoneHierarchies_FailBeforeAddingHelpers(bool zoneInsidePlayerObject)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Root");
                SceneManager.MoveGameObjectToScene(root, scene);
                var child = new GameObject("Child");
                child.transform.SetParent(root.transform);
                GameObject zoneObject = zoneInsidePlayerObject ? child : root;
                GameObject playerObject = zoneInsidePlayerObject ? root : child;
                playerObject.AddComponent<VRC.SDK3.Components.VRCPlayerObject>();
                zoneObject.AddComponent<BoxCollider>().isTrigger = true;
                zoneObject.AddUdonSharpComponent<LCGNetworkZone>();
                var error = Assert.Throws<BuildFailedException>(() => CreateProcessor().OnProcessScene(scene, null));
                Assert.That(error.Message, Does.Contain("PlayerObject"));
                Assert.That(scene.GetRootGameObjects(), Has.Length.EqualTo(1));
            }
            finally { CloseAfterBehaviourSetup(scene); }
        }

        [Test]
        public void CompilationContext_ChangesDiagnosticDefineWithoutReusingStaleCache()
        {
            Type settingsType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharpEditor.UdonSharpSettings");
            object settings = settingsType.GetMethod("GetSettings").Invoke(null, null);
            FieldInfo setting = settingsType.GetField("lcgNetworkDiagnostics");
            object previous = setting.GetValue(settings);
            Type contextType = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.Compiler.CompilationContext");
            MethodInfo resetCaches = contextType.GetMethod("ResetAssemblyCaches",
                BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                resetCaches.Invoke(null, null);
                foreach (bool enabled in new[] { false, true })
                {
                    setting.SetValue(settings, enabled);
                    object assemblies = contextType.GetMethod("GetBuildAssemblies")
                        .Invoke(null, new object[] { false, EditorUserBuildSettings.activeBuildTarget });
                    object[] discovered = ((System.Collections.IEnumerable)assemblies).Cast<object>().ToArray();
                    Assert.That(discovered, Is.Not.Empty);
                    foreach (object assembly in discovered)
                    {
                        var defines = ((System.Collections.IEnumerable)assembly.GetType().GetProperty("Defines")
                            .GetValue(assembly)).Cast<string>();
                        Assert.That(defines.Contains("LCG_NETWORK_DIAGNOSTICS"), Is.EqualTo(enabled));
                    }
                }
            }
            finally
            {
                setting.SetValue(settings, previous);
                resetCaches.Invoke(null, null);
            }
        }

        [Test]
        public void SceneProcessing_RegistersMailboxForClientCloningAndPreservesExistingTemplates()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var world = new GameObject("LCG test descriptor");
                SceneManager.MoveGameObjectToScene(world, scene);
                var descriptor = world.AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
                var existingObject = new GameObject("Existing player template");
                SceneManager.MoveGameObjectToScene(existingObject, scene);
                var existing = existingObject.AddComponent<VRC.SDK3.Components.VRCPlayerObject>();
                existingObject.SetActive(false);
                descriptor.PlayerPersistence = new[] { existing };

                CreateProcessor().OnProcessScene(scene, null);

                var mailbox = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<LCGRuntimePlayer>(true)).Single();
                var template = mailbox.GetComponent<VRC.SDK3.Components.VRCPlayerObject>();
                Assert.That(descriptor.PlayerPersistence, Is.EquivalentTo(new[] { existing, template }));
                Assert.That(mailbox.gameObject.activeSelf, Is.False,
                    "A build contains an inactive template, not a live scene mailbox.");
                Assert.That(existingObject.activeSelf, Is.False);
                Assert.That(descriptor.NetworkIDCollection.Any(pair => pair.gameObject == mailbox.gameObject), Is.True);
            }
            finally { CloseAfterBehaviourSetup(scene); }
        }

        [Test]
        public void SceneProcessing_PreservesGeneratedBindingsThroughProxySerialization()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var target = new GameObject("LCG test receiver");
                SceneManager.MoveGameObjectToScene(target, scene);
                // A real compiled packet receiver exercises registration and binding.
                var receiver = target.AddUdonSharpComponent<LCGManualObjectSync>();
                CreateProcessor().OnProcessScene(scene, null);

                LCGRuntime runtime = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<LCGRuntime>(true)).Single();
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
                var runtimeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(runtime);
                Assert.That(GetSerializedVariable(backing, "__lcgRuntime"), Is.SameAs(runtimeBacking));
                Assert.That(GetSerializedVariable(backing, "__lcgReceiverId"), Is.EqualTo(0));

                // The normal UdonSharp scene callback clears and rebuilds public variables.
                Type manager = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharpEditor.UdonSharpEditorManager");
                manager.GetMethod("OnSceneBuildInternal", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { false, scene.GetRootGameObjects() });

                Assert.That(GetSerializedVariable(backing, "__lcgRuntime"), Is.SameAs(runtimeBacking));
                Assert.That(GetSerializedVariable(backing, "__lcgReceiverId"), Is.EqualTo(0));
                Assert.That(GetSerializedVariable(backing, "__lcgZoneId"), Is.EqualTo(0));
                Assert.That(GetSerializedVariable(runtimeBacking, "mailboxTemplate"), Is.Not.Null);
                Assert.That((Array)GetSerializedVariable(runtimeBacking, "packetAddresses"), Has.Length.EqualTo(2));
            }
            finally { CloseAfterBehaviourSetup(scene); }
        }

        [Test]
        public void UnsupportedZoneBehaviour_DoesNotLeavePartiallyGeneratedHelpers()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var target = new GameObject("Invalid LCG zone");
                SceneManager.MoveGameObjectToScene(target, scene);
                target.AddComponent<BoxCollider>().isTrigger = true;
                target.AddUdonSharpComponent<LCGNetworkZone>();
                var receiver = target.AddUdonSharpComponent<LCGManualObjectSync>();
                UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver).SyncMethod =
                    VRC.SDKBase.Networking.SyncType.Continuous;

                Assert.Throws<BuildFailedException>(() => CreateProcessor().OnProcessScene(scene, null));
                Assert.That(scene.GetRootGameObjects(), Has.Length.EqualTo(1));
                Assert.That(target.GetComponents<LCGZoneOwnershipGuard>(), Is.Empty);
            }
            finally { CloseAfterBehaviourSetup(scene); }
        }

        private static void CloseAfterBehaviourSetup(Scene scene)
        {
            // UdonSharp schedules inspector setup in delayCall; let it finish before
            // destroying the fixture's backing components.
            EditorApplication.delayCall += () => {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            };
        }

        private static IProcessSceneWithReport CreateProcessor()
        {
            Type type = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharpEditor.LCGNetworkSceneProcessor");
            return (IProcessSceneWithReport)Activator.CreateInstance(type, true);
        }

        private static object GetSerializedVariable(VRC.Udon.UdonBehaviour behaviour, string name)
        {
            Assert.That(behaviour.publicVariables.TryGetVariableValue(name, out object value), Is.True,
                "Missing serialized binding: " + name);
            return value;
        }
    }
}
