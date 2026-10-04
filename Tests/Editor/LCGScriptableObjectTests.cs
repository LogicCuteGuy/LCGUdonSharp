using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using UdonSharp.Compiler;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Serialization;
using UnityEditor;
using UnityEngine;

namespace UdonSharp.Tests
{
    public sealed class LCGScriptableObjectTests
    {
        public enum Rarity : byte { Rare = 7 }
        public class BaseData : ScriptableObject { public int zBase; }
        public sealed class ItemData : BaseData
        {
            public string label;
            public Rarity rarity;
            public int[] values;
            public Texture2D texture;
            [SerializeField] private float privateValue = 1.25f;
            [NonSerialized] public int ignored;
            public int ReadOnlyProperty => zBase;
        }
        public sealed class InvalidData : ScriptableObject { public decimal unsupported; }
        public sealed class ReferenceData : ScriptableObject { public UnityEngine.Object asset; }
        public class ZBaseData : ScriptableObject { public int shared; }
        public sealed class ADerivedData : ZBaseData { public new int shared; public string extra; }
        public sealed class NestedData : ScriptableObject
        {
            public ZBaseData child;
            public ZBaseData[] children;
            public NestedData next;
        }

        [Test]
        public void SdkScriptableObjects_KeepTheirNativeUdonStorage()
        {
            Type productType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.Economy.UdonProduct")).First(t => t != null);
            Assert.That(ScriptableObjectDataLayout.IsDataType(productType), Is.False);
            Assert.That(UdonSharpUtils.UserTypeToUdonType(productType), Is.EqualTo(productType));
            Assert.That(Serializer.CreatePooled(productType).GetUdonStorageType(), Is.EqualTo(productType));
        }

        [TestCase("item.texture", null, "")]
        [TestCase("((UdonSharp.Tests.LCGScriptableObjectTests.BaseData)item).zBase", null, "")]
        [TestCase("item is UdonSharp.Tests.LCGScriptableObjectTests.BaseData", null, "")]
        [TestCase("(item as UdonSharp.Tests.LCGScriptableObjectTests.BaseData).zBase", null, "")]
        [TestCase("item is UdonSharp.Tests.LCGScriptableObjectTests.BaseData data && data.zBase > 0", null, "")]
        [TestCase("item.name", "serialized fields only", "")]
        [TestCase("item.ReadOnlyProperty", "serialized fields only", "")]
        [TestCase("item.GetInstanceID()", "serialized fields only", "")]
        [TestCase("new ItemData[] { item }.GetValue(0)", "serialized fields only", "")]
        [TestCase("new ItemData[] { item }.GetType()", "serialized fields only", "")]
        [TestCase("(object)item", "casts require custom data types", "")]
        [TestCase("ScriptableObject.CreateInstance<ItemData>()", "runtime creation", "")]
        [TestCase("item.texture", "cannot use [UdonSynced]", "[UdonSynced]")]
        public void Compiler_BindsMetadataDataFieldsAndRejectsUnsupportedOperations(string expression, string error, string fieldAttribute)
        {
            string source = "using UdonSharp; using UnityEngine; using ItemData = UdonSharp.Tests.LCGScriptableObjectTests.ItemData; " +
                "public class Probe : UdonSharpBehaviour { " + fieldAttribute + " public ItemData item; " +
                "public object Run(UdonSharp.Tests.LCGScriptableObjectTests.Rarity rarity = UdonSharp.Tests.LCGScriptableObjectTests.Rarity.Rare) { return " + expression + "; } }";
            var references = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("ScriptableObjectBindingProbe", new[] { tree }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.Empty);
            var context = new CompilationContext(new UdonSharpCompileOptions { ConcurrentBuild = false });
            context.RoslynCompilation = compilation;
            var probe = compilation.GetTypeByMetadataName("Probe");
            var bind = new BindContext(context, probe, Array.Empty<Symbol>());
            // Probe is deliberately in-memory; register its behaviour representation
            // without relying on a Unity assembly being emitted for the test snippet.
            var type = new UdonSharpBehaviourTypeSymbol(probe, bind);
            var lookup = (System.Collections.IDictionary)typeof(CompilationContext).GetField("_typeSymbolLookup",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(context);
            lookup.Add(probe, type);
            var method = type.GetMember<MethodSymbol>(probe.GetMembers("Run").Single(), bind);
            var field = type.GetMember<FieldSymbol>(probe.GetMembers("item").Single(), bind);
            Action action = () => {
                using (bind.OpenMemberBindScope(field)) field.Bind(bind);
                using (bind.OpenMemberBindScope(method)) method.Bind(bind);
            };
            if (error == null) Assert.DoesNotThrow(() => action());
            else Assert.That(Assert.Throws<UdonSharp.Core.CompilerException>(() => action()).Message, Does.Contain(error));
        }

        [Test]
        public void Compiler_RejectsWritingDataFields()
        {
            var references = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("ScriptableObjectWriteProbe", references: references);
            var context = new CompilationContext(new UdonSharpCompileOptions { ConcurrentBuild = false });
            context.RoslynCompilation = compilation;
            var symbol = compilation.GetTypeByMetadataName(typeof(ItemData).FullName);
            var bind = new BindContext(context, symbol, Array.Empty<Symbol>());
            var type = context.GetTypeSymbol(symbol, bind);
            var field = type.GetMember<FieldSymbol>(symbol.GetMembers("label").Single(), bind);
            var access = new BoundScriptableObjectFieldAccessExpression(bind, null, field,
                new BoundConstantExpression((object)null, type));
            Assert.That(Assert.Throws<UdonSharp.Core.CompilerException>(() => access.EmitSet(null, null)).Message,
                Does.Contain("read-only"));
        }

        [Test]
        public void ShopExample_ExecutesDataReadsAndPurchasesInUdonVm()
        {
            const string folder = "Packages/com.logiccuteguy.lcgudonsharp/Example/ScriptableObjects/";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(folder + "ScriptableObjectShopExample.asset");
            Assert.That(asset, Is.Not.Null);
            var milk = AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "StrawberryMilk.asset");
            var tea = AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "GreenTea.asset");
            var serializer = Serializer.CreatePooled(milk.GetType());
            var storage = new SimpleValueStorage<object[]>();
            serializer.WriteWeak(storage, milk);
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("item"), storage.Value);
            var catalog = Array.CreateInstance(milk.GetType(), 2);
            catalog.SetValue(milk, 0); catalog.SetValue(tea, 1);
            Serializer.CreatePooled(catalog.GetType()).WriteWeak(storage, catalog);
            program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("catalog"), storage.Value);
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_start"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("coins")), Is.EqualTo(100));
            foreach (int expected in new[] { 65, 30, 30 })
            {
                vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("_interact"));
                Assert.That(vm.Interpret(), Is.Zero);
                Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("coins")), Is.EqualTo(expected));
            }
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("purchases")), Is.EqualTo(2));
            Assert.That(program.Heap.GetHeapVariable<string>(program.SymbolTable.GetAddressFromSymbol("lastStatus")), Is.EqualTo("Not enough coins"));
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("TestArrayCopy"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(program.Heap.GetHeapVariable<bool>(program.SymbolTable.GetAddressFromSymbol("snapshotArrayCopyValid")), Is.True);
            Assert.That(milk.GetType().GetField("price").GetValue(milk), Is.EqualTo(35));
        }

        [Test]
        public void Snapshot_LowersFieldsAndPreservesInspectorAsset()
        {
            var asset = ScriptableObject.CreateInstance<ItemData>();
            var texture = new Texture2D(1, 1);
            try
            {
                asset.label = "ไทย"; asset.rarity = Rarity.Rare; asset.zBase = 42;
                asset.values = new[] { 10, 20 }; asset.texture = texture;
                var serializer = Serializer.CreatePooled<ItemData>();
                var storage = new SimpleValueStorage<object[]>();
                serializer.Write(storage, in asset);
                Assert.That(serializer.GetUdonStorageType(), Is.EqualTo(typeof(object[])));
                Assert.That(UdonSharpUtils.UserTypeToUdonType(typeof(ItemData)), Is.EqualTo(typeof(object[])));
                // Ordinal declaring type/name ordering: BaseData.zBase first.
                Assert.That(storage.Value, Is.EqualTo(new object[] { ScriptableObjectDataLayout.GetTypeTag(typeof(ItemData)), 42, "ไทย", 1.25f, (byte)7, texture, new[] { 10, 20 } }));
                ((int[])storage.Value[6])[0] = 99;
                Assert.That(asset.values[0], Is.EqualTo(10));
                ItemData original = asset;
                serializer.Read(ref asset, storage);
                Assert.That(asset, Is.SameAs(original));
                object weak = asset;
                serializer.ReadWeak(ref weak, storage);
                Assert.That(weak, Is.SameAs(asset));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void NullEmptyAndAssetArrays_PreserveShape()
        {
            var serializer = Serializer.CreatePooled<ItemData[]>();
            var storage = new SimpleValueStorage<object[]>();
            ItemData[] source = null;
            serializer.Write(storage, in source); Assert.That(storage.Value, Is.Null);
            source = Array.Empty<ItemData>();
            serializer.Write(storage, in source); Assert.That(storage.Value, Is.Empty);
            var asset = ScriptableObject.CreateInstance<ItemData>();
            try
            {
                source = new[] { asset, null };
                serializer.Write(storage, in source);
                Assert.That(storage.Value.Length, Is.EqualTo(2));
                Assert.That(storage.Value[0], Is.TypeOf<object[]>());
                Assert.That(storage.Value[1], Is.Null);
                var original = source;
                serializer.Read(ref source, storage);
                Assert.That(source, Is.SameAs(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void UnsupportedFieldsAndDisguisedNestedAssets_AreRejected()
        {
            var invalid = ScriptableObject.CreateInstance<InvalidData>();
            var reference = ScriptableObject.CreateInstance<ReferenceData>();
            try
            {
                Assert.Throws<NotSupportedException>(() => Serializer.CreatePooled<InvalidData>().Write(new SimpleValueStorage<object[]>(), in invalid));
                reference.asset = invalid;
                Assert.Throws<NotSupportedException>(() => Serializer.CreatePooled<ReferenceData>().Write(new SimpleValueStorage<object[]>(), in reference));
            }
            finally { UnityEngine.Object.DestroyImmediate(invalid); UnityEngine.Object.DestroyImmediate(reference); }
        }

        [Test]
        public void DependencyTraversal_CollectsRuntimeAssetsWithoutOverwritingSnapshot()
        {
            var asset = ScriptableObject.CreateInstance<ItemData>();
            var texture = new Texture2D(1, 1);
            var previous = UsbSerializationContext.CurrentPolicy;
            try
            {
                asset.texture = texture;
                var original = new object[] { "keep" };
                var storage = new SimpleValueStorage<object[]>(original);
                UsbSerializationContext.CurrentPolicy = UdonSharpEditor.ProxySerializationPolicy.CollectRootDependencies;
                UsbSerializationContext.Dependencies.Clear();
                Serializer.CreatePooled<ItemData>().Write(storage, in asset);
                Assert.That(storage.Value, Is.SameAs(original));
                Assert.That(UsbSerializationContext.Dependencies.Contains(texture), Is.True);
                Assert.That(UsbSerializationContext.Dependencies.Contains(asset), Is.False);
            }
            finally
            {
                UsbSerializationContext.CurrentPolicy = previous;
                UsbSerializationContext.Dependencies.Clear();
                UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DerivedLayout_KeepsBasePrefixEvenWithHiddenFieldsAndReversedTypeNames()
        {
            var baseFields = ScriptableObjectDataLayout.GetFields(typeof(ZBaseData));
            var derivedFields = ScriptableObjectDataLayout.GetFields(typeof(ADerivedData));
            Assert.That(derivedFields.Take(baseFields.Length), Is.EqualTo(baseFields));
            var asset = ScriptableObject.CreateInstance<ADerivedData>();
            try
            {
                ((ZBaseData)asset).shared = 11; asset.shared = 22; asset.extra = "ไทย";
                ZBaseData source = asset;
                var storage = new SimpleValueStorage<object[]>();
                Serializer.CreatePooled<ZBaseData>().Write(storage, in source);
                Assert.That(storage.Value, Is.EqualTo(new object[] {
                    ScriptableObjectDataLayout.GetTypeTag(typeof(ADerivedData)), 11, "ไทย", 22 }));
                Assert.That(((string)storage.Value[0]).Contains(ScriptableObjectDataLayout.GetTypeToken(typeof(ZBaseData))), Is.True);
                Assert.That(((string)storage.Value[0]).Contains(ScriptableObjectDataLayout.GetTypeToken(typeof(ItemData))), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void NestedPolymorphicGraph_BakesNullSharedAssetsAndDerivedFields()
        {
            var root = ScriptableObject.CreateInstance<NestedData>();
            var child = ScriptableObject.CreateInstance<ADerivedData>();
            var nested = ScriptableObject.CreateInstance<NestedData>();
            try
            {
                ((ZBaseData)child).shared = 11; child.shared = 22;
                root.child = child; root.children = new ZBaseData[] { child, null }; root.next = nested;
                nested.child = child; nested.children = Array.Empty<ZBaseData>();
                var storage = new SimpleValueStorage<object[]>();
                Serializer.CreatePooled<NestedData>().Write(storage, in root);
                var bakedChild = (object[])storage.Value[1];
                var bakedChildren = (object[])storage.Value[2];
                var bakedNested = (object[])storage.Value[3];
                Assert.That(bakedChild[1], Is.EqualTo(11));
                Assert.That(bakedChild[3], Is.EqualTo(22));
                Assert.That(bakedChildren[0], Is.SameAs(bakedChild));
                Assert.That(bakedChildren[1], Is.Null);
                Assert.That(bakedNested[1], Is.SameAs(bakedChild));
                Assert.That(bakedNested[2], Is.Empty);
                Assert.That(bakedNested[3], Is.Null);
                object proxy = root; Serializer.CreatePooled<NestedData>().ReadWeak(ref proxy, storage);
                Assert.That(proxy, Is.SameAs(root));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(child); UnityEngine.Object.DestroyImmediate(nested); }
        }

        [Test]
        public void Cycles_FailWithoutOverwritingTargetAndNextWriteRecovers()
        {
            var first = ScriptableObject.CreateInstance<NestedData>();
            var second = ScriptableObject.CreateInstance<NestedData>();
            try
            {
                var original = new object[] { "keep" };
                var storage = new SimpleValueStorage<object[]>(original);
                var serializer = Serializer.CreatePooled<NestedData>();
                first.next = first;
                Assert.That(Assert.Throws<NotSupportedException>(() => serializer.Write(storage, in first)).Message, Does.Contain("Cyclic"));
                first.next = second; second.next = first;
                Assert.Throws<NotSupportedException>(() => serializer.Write(storage, in first));
                Assert.That(storage.Value, Is.SameAs(original));
                second.next = null;
                Assert.DoesNotThrow(() => serializer.Write(storage, in first));
                Assert.That(storage.Value, Is.Not.SameAs(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
        }

        [Test]
        public void DeepGraphs_RejectBeyondLimitAndRecoverAtSupportedDepth()
        {
            var assets = Enumerable.Range(0, 129).Select(_ => ScriptableObject.CreateInstance<NestedData>()).ToArray();
            try
            {
                for (int i = 0; i < assets.Length - 1; i++) assets[i].next = assets[i + 1];
                var serializer = Serializer.CreatePooled<NestedData>();
                var storage = new SimpleValueStorage<object[]>();
                Assert.That(Assert.Throws<NotSupportedException>(() => serializer.Write(storage, in assets[0])).Message, Does.Contain("128"));
                assets[127].next = null;
                Assert.DoesNotThrow(() => serializer.Write(storage, in assets[0]));
                Assert.That(storage.Value, Is.Not.Null);
            }
            finally { foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void NestedDependencyTraversal_CollectsDerivedRuntimeAssets()
        {
            var root = ScriptableObject.CreateInstance<NestedData>();
            var child = ScriptableObject.CreateInstance<ItemData>();
            var parent = ScriptableObject.CreateInstance<DependencyData>();
            var texture = new Texture2D(1, 1);
            var previous = UsbSerializationContext.CurrentPolicy;
            try
            {
                child.texture = texture; parent.child = child;
                var original = new object[] { "keep" };
                var storage = new SimpleValueStorage<object[]>(original);
                UsbSerializationContext.CurrentPolicy = UdonSharpEditor.ProxySerializationPolicy.CollectRootDependencies;
                UsbSerializationContext.Dependencies.Clear();
                Serializer.CreatePooled<DependencyData>().Write(storage, in parent);
                Assert.That(storage.Value, Is.SameAs(original));
                Assert.That(UsbSerializationContext.Dependencies.Contains(texture), Is.True);
                Assert.That(UsbSerializationContext.Dependencies.Contains(parent), Is.False);
                Assert.That(UsbSerializationContext.Dependencies.Contains(child), Is.False);
            }
            finally
            {
                UsbSerializationContext.CurrentPolicy = previous; UsbSerializationContext.Dependencies.Clear();
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(child);
                UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public sealed class DependencyData : ScriptableObject { public BaseData child; }

        public sealed class BranchData : ScriptableObject { public BranchData[] branches; }

        [Test]
        public void SharedSubgraphs_CannotBypassDepthLimitThroughCachedSnapshots()
        {
            var assets = Enumerable.Range(0, 129).Select(_ => ScriptableObject.CreateInstance<BranchData>()).ToArray();
            try
            {
                // First bake the shared suffix at a valid depth, then reach it one level deeper.
                for (int i = 1; i < 127; i++) assets[i].branches = new[] { assets[i + 1] };
                assets[0].branches = new[] { assets[1], assets[128] };
                assets[128].branches = new[] { assets[1] };
                var storage = new SimpleValueStorage<object[]>();
                Assert.That(Assert.Throws<NotSupportedException>(() => Serializer.CreatePooled<BranchData>().Write(storage, in assets[0])).Message, Does.Contain("128"));
                Assert.That(storage.Value, Is.Null);
            }
            finally { foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void EquipmentExample_ExecutesNestedPolymorphicReadsCastsAndArrayCopiesInUdonVm()
        {
            const string folder = "Packages/com.logiccuteguy.lcgudonsharp/Example/ScriptableObjects/";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(folder + "ScriptableObjectEquipmentExample.asset");
            Assert.That(asset, Is.Not.Null);
            var weapon = AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "TrainingSword.asset");
            var spell = AssetDatabase.LoadAssetAtPath<ScriptableObject>(folder + "FireSpell.asset");
            var storage = new SimpleValueStorage<object[]>();
            Type baseType = weapon.GetType().BaseType;
            Serializer.CreatePooled(baseType).WriteWeak(storage, weapon);
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("item"), storage.Value);
            var catalog = Array.CreateInstance(baseType, 2); catalog.SetValue(weapon, 0); catalog.SetValue(spell, 1);
            Serializer.CreatePooled(catalog.GetType()).WriteWeak(storage, catalog);
            program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol("catalog"), storage.Value);
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM(); vm.LoadProgram(program);
            Action<string> run = name => { vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name)); Assert.That(vm.Interpret(), Is.Zero, name); };
            run("_start"); run("TestDataFeatures");
            Assert.That(program.Heap.GetHeapVariable<bool>(program.SymbolTable.GetAddressFromSymbol("featureTestsPassed")), Is.True);
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("receiverEvaluations")), Is.EqualTo(1));
            run("_interact");
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("coins")), Is.EqualTo(65));
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("lastEffect")), Is.EqualTo(45));
            run("SelectNext"); run("_interact");
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("coins")), Is.EqualTo(45));
            Assert.That(program.Heap.GetHeapVariable<int>(program.SymbolTable.GetAddressFromSymbol("lastEffect")), Is.EqualTo(80));
        }
    }
}
