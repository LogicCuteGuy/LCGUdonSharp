using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using UnityEngine;

namespace UdonSharp.Tests
{
    public sealed class LCGNetworkingPublicApiTests
    {
        [Test]
        public void PacketAttribute_SupportsFieldsAndMethodsOnly()
        {
            AttributeUsageAttribute usage = typeof(LCGPacketAttribute)
                .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .Cast<AttributeUsageAttribute>()
                .Single();

            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Field | AttributeTargets.Method));
            Assert.That(usage.AllowMultiple, Is.False);
        }

        [Test]
        public void PacketAttribute_DefaultsToAnyAuthorityWithoutCallback()
        {
            LCGPacketAttribute attribute = new LCGPacketAttribute();

            Assert.That(attribute.Authority, Is.EqualTo(LCGPacketAuthority.Any));
            Assert.That(attribute.Callback, Is.Null);
        }

        [Test]
        public void Zone_RequiresColliderAndDefaultsToFreeze()
        {
            RequireComponent requirement = typeof(LCGNetworkZone)
                .GetCustomAttributes(typeof(RequireComponent), true)
                .Cast<RequireComponent>()
                .Single();

            Assert.That(requirement.m_Type0, Is.EqualTo(typeof(Collider)));
            Assert.That(LCGZoneExitMode.Freeze, Is.EqualTo(default(LCGZoneExitMode)));
        }

        [Test]
        public void RuntimeFrameLimit_LeavesHeadroomBelowVrcMaximum()
        {
            Assert.That(LCGRuntime.HeaderSize, Is.GreaterThanOrEqualTo(20));
            Assert.That(LCGRuntime.MaxFrameBytes, Is.EqualTo(12 * 1024));
            Assert.That(LCGRuntime.MaxFrameBytes, Is.LessThan(16 * 1024));
        }

        [Test]
        public void StringWireEncoding_DistinguishesNullAndEmpty()
        {
            MethodInfo encode = typeof(LCGRuntime).GetMethod("EncodeValue",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo decode = typeof(LCGRuntime).GetMethod("DecodeValue",
                BindingFlags.NonPublic | BindingFlags.Static);

            byte[] encodedNull = (byte[])encode.Invoke(null,
                new object[] { (int)LCGPacketType.String, null });
            byte[] encodedEmpty = (byte[])encode.Invoke(null,
                new object[] { (int)LCGPacketType.String, string.Empty });

            Assert.That(encodedNull, Is.EqualTo(new byte[] { 0 }));
            Assert.That(encodedEmpty, Is.EqualTo(new byte[] { 1 }));
            Assert.That(decode.Invoke(null,
                new object[] { (int)LCGPacketType.String, encodedNull, 0, encodedNull.Length }), Is.Null);
            Assert.That(decode.Invoke(null,
                new object[] { (int)LCGPacketType.String, encodedEmpty, 0, encodedEmpty.Length }), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SignedByteWireEncoding_RoundTripsEveryValue()
        {
            MethodInfo encode = typeof(LCGRuntime).GetMethod("EncodeValue",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo decode = typeof(LCGRuntime).GetMethod("DecodeValue",
                BindingFlags.NonPublic | BindingFlags.Static);

            for (int value = sbyte.MinValue; value <= sbyte.MaxValue; value++)
            {
                byte[] encoded = (byte[])encode.Invoke(null,
                    new object[] { (int)LCGPacketType.SByte, (sbyte)value });
                Assert.That(encoded, Is.EqualTo(new[] { (byte)(value & 255) }), "Wire value " + value);
                Assert.That(decode.Invoke(null,
                    new object[] { (int)LCGPacketType.SByte, encoded, 0, encoded.Length }),
                    Is.EqualTo((sbyte)value), "Round trip " + value);
            }
            Assert.That(decode.Invoke(null,
                new object[] { (int)LCGPacketType.SByte, new byte[0], 0, 0 }), Is.Null);
            Assert.That(decode.Invoke(null,
                new object[] { (int)LCGPacketType.SByte, new byte[2], 0, 2 }), Is.Null);
        }

        [Test]
        public void ManualObjectSync_HasStaticRequestApi()
        {
            MethodInfo request = typeof(LCGNetwork).GetMethod(nameof(LCGNetwork.RequestObjectSync),
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(request, Is.Not.Null);
            Assert.That(request.GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(GameObject) }));
        }

        [Test]
        public void ManualObjectSync_RuntimeAbiExposesCompilerDispatchRegisters()
        {
            Assert.That(typeof(LCGRuntime).GetField("__lcgObjectSyncTarget", BindingFlags.Public | BindingFlags.Instance),
                Is.Not.Null);
            Assert.That(typeof(LCGRuntime).GetMethod("__lcgRequestObjectSync",
                BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
        }

        [Test]
        public void CompilerSymbol_HasAttributeHandlesAttributesBeforeBinding()
        {
            Assembly compilerAssembly = typeof(Compiler.UdonSharpCompilerV1).Assembly;
            Type symbolType = compilerAssembly.GetType("UdonSharp.Compiler.Symbols.Symbol", true);
            Type methodSymbolType = compilerAssembly.GetType(
                "UdonSharp.Compiler.Symbols.ExternSynthesizedMethodSymbol", true);
            object unboundMethod = FormatterServices.GetUninitializedObject(methodSymbolType);
            MethodInfo hasAttribute = symbolType
                .GetMethod("HasAttribute", BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(LCGPacketAttribute));

            object result = null;
            Assert.DoesNotThrow(() => result = hasAttribute.Invoke(unboundMethod, Array.Empty<object>()));
            Assert.That(result, Is.False);
        }

        [TestCase("ReceiveAnnouncement", true)]
        [TestCase("LocalOnly", false)]
        public void CompilerSymbol_DetectsSourcePacketAttributeBeforeBinding(string methodName, bool expected)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(@"using System;
namespace UdonSharp
{
    public sealed class LCGPacketAttribute : Attribute { }
}
public class PacketReceiver
{
    [UdonSharp.LCGPacket] public void ReceiveAnnouncement(string message) { }
    public void LocalOnly(string message) { }
}");
            CSharpCompilation compilation = CSharpCompilation.Create("PacketAttributeRegression",
                new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.Empty);

            IMethodSymbol sourceMethod = compilation.GetTypeByMetadataName("PacketReceiver")
                .GetMembers(methodName).OfType<IMethodSymbol>().Single();
            Assembly compilerAssembly = typeof(Compiler.UdonSharpCompilerV1).Assembly;
            Type symbolType = compilerAssembly.GetType("UdonSharp.Compiler.Symbols.Symbol", true);
            Type methodSymbolType = compilerAssembly.GetType(
                "UdonSharp.Compiler.Symbols.UdonSharpBehaviourMethodSymbol", true);
            object unboundMethod = FormatterServices.GetUninitializedObject(methodSymbolType);
            symbolType.GetField("<RoslynSymbol>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(unboundMethod, sourceMethod);
            MethodInfo hasAttribute = symbolType
                .GetMethod("HasAttribute", BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(LCGPacketAttribute));

            Assert.That(hasAttribute.Invoke(unboundMethod, Array.Empty<object>()), Is.EqualTo(expected));
        }

        [Test]
        public void Behaviour_ExposesTargetedMethodAndForcedFieldApis()
        {
            MethodInfo[] targeted = typeof(UdonSharpBehaviour).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == nameof(UdonSharpBehaviour.SendLCGNetworkEvent))
                .ToArray();

            Assert.That(targeted, Has.Length.EqualTo(9));
            Assert.That(typeof(UdonSharpBehaviour).GetMethod(nameof(UdonSharpBehaviour.ForceSendPacket),
                new[] { typeof(string) }), Is.Not.Null);
        }

        [Test]
        public void VRCAsync_ExposesBuildTimeAwaitableApi()
        {
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.LoadStringAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRC.SDK3.StringLoading.IVRCStringDownload>)));
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.LoadImageAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRC.SDK3.Image.IVRCImageDownload>)));
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.LoadVideoAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRCVideoLoadResult>)));
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.WaitForVideoEndAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRCVideoPlaybackResult>)));
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.RequestGPUReadbackAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest>)));
            Assert.That(typeof(VRCAsync).GetMethod(nameof(VRCAsync.RequestSerializationAsync)).ReturnType,
                Is.EqualTo(typeof(Task<VRC.Udon.Common.SerializationResult>)));
        }

        [Test]
        public void Compiler_HasLowerPhaseAndSdkAdapterRegistry()
        {
            Assembly compilerAssembly = typeof(Compiler.UdonSharpCompilerV1).Assembly;
            Type contextType = compilerAssembly.GetType("UdonSharp.Compiler.CompilationContext", true);
            Type phaseType = contextType.GetNestedType("CompilePhase", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(Enum.GetNames(phaseType), Does.Contain("Lower"));

            Type registryType = compilerAssembly.GetType(
                "UdonSharp.Compiler.Lowering.SdkCallbackAdapterRegistry", true);
            PropertyInfo adapters = registryType.GetProperty("Adapters",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Array values = ((System.Collections.IEnumerable)adapters.GetValue(null)).Cast<object>().ToArray();
            Assert.That(values.Length, Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void GenericRestrictions_RejectListTypeReferences()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
using System.Collections.Generic;
public class Sample { public List<int> Values; }");
            ITypeSymbol listType = compilation.GetTypeByMetadataName("Sample")
                .GetMembers("Values").OfType<IFieldSymbol>().Single().Type;

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                listType, Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("List<T>"));
        }

        [Test]
        public void GenericRestrictions_RejectListNestedInsideAnotherType()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
using System.Collections.Generic;
public class Wrapper<T> { }
public class Sample { public Wrapper<List<int>> Values; }");
            ITypeSymbol wrapperType = compilation.GetTypeByMetadataName("Sample")
                .GetMembers("Values").OfType<IFieldSymbol>().Single().Type;

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                wrapperType, Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("List<T>"));
        }

        [Test]
        public void GenericRestrictions_RejectListDerivedTypes()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
using System.Collections.Generic;
public class DerivedList : List<int> { }
public class Sample { public DerivedList Values; }");
            ITypeSymbol derivedListType = compilation.GetTypeByMetadataName("DerivedList");

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                derivedListType, Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("List<T>"));
        }

        [Test]
        public void GenericRestrictions_RejectOpenGenericTypeReferences()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
using System;
public class Box<T> { }
public class Sample { public Type GetTypeValue() => typeof(Box<>); }");
            SyntaxNode unboundType = compilation.SyntaxTrees.Single().GetRoot()
                .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax>()
                .Single().Type;
            ITypeSymbol type = compilation.GetSemanticModel(unboundType.SyntaxTree).GetTypeInfo(unboundType).Type;

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("Open generic type"));
        }

        [Test]
        public void GenericRestrictions_RejectGenericHeapObjectCreation()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public class Box<T> { }
public class Sample { public object Create() => new Box<int>(); }");
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("Box`1").Construct(
                compilation.GetSpecialType(SpecialType.System_Int32));

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.ObjectCreation);

            Assert.That(violation, Does.Contain("generic heap objects"));
        }

        [Test]
        public void GenericRestrictions_RejectGenericHeapObjectTypeReferences()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public class Box<T> { }
public class Sample { public Box<int> Value; }");
            ITypeSymbol type = compilation.GetTypeByMetadataName("Sample")
                .GetMembers("Value").OfType<IFieldSymbol>().Single().Type;

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("generic heap object types"));
        }

        [Test]
        public void GenericRestrictions_RejectTypesDerivedFromGenericHeapObjects()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public class Box<T> { }
public class IntBox : Box<int> { }");

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                compilation.GetTypeByMetadataName("IntBox"), Compiler.GenericUseSite.TypeReference);

            Assert.That(violation, Does.Contain("generic heap object types"));
        }

        [Test]
        public void GenericRestrictions_PreserveCompilerOnlyTaskHandlesButRejectConstruction()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation("public class Sample { }");
            INamedTypeSymbol taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1").Construct(
                compilation.GetSpecialType(SpecialType.System_Int32));

            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                taskType, Compiler.GenericUseSite.TypeReference), Is.Null);
            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                taskType, Compiler.GenericUseSite.ObjectCreation), Does.Contain("generic heap objects"));
        }

        [Test]
        public void GenericRestrictions_RejectOpenConstructedRuntimeTypes()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public static class Helpers<T> { }
public class Sample<T> { public System.Type Get() => typeof(Helpers<T>); }");
            SyntaxNode openType = compilation.SyntaxTrees.Single().GetRoot()
                .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax>()
                .Single().Type;
            ITypeSymbol type = compilation.GetSemanticModel(openType.SyntaxTree).GetTypeInfo(openType).Type;

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.RuntimeType);

            Assert.That(violation, Does.Contain("Open generic type"));
        }

        [Test]
        public void GenericRestrictions_RejectGenericBehavioursWithoutRequiringProgramAsset()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
namespace UdonSharp { public class UdonSharpBehaviour { } }
public class GenericBehaviour<T> : UdonSharp.UdonSharpBehaviour { }");
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("GenericBehaviour`1");

            string violation = Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.BehaviourDeclaration);

            Assert.That(violation, Does.Contain("Generic U# behaviour"));
        }

        [Test]
        public void GenericRestrictions_AllowClosedInterfacesAndStaticGenericSpecialization()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public interface IValue<T> { T Get(); }
public static class Helpers<T> { public static T Identity(T value) => value; }
public class Sample : IValue<int> { public int Get() => Helpers<int>.Identity(1); }");
            INamedTypeSymbol closedInterface = compilation.GetTypeByMetadataName("Sample").Interfaces.Single();
            INamedTypeSymbol staticHelper = compilation.GetTypeByMetadataName("Helpers`1").Construct(
                compilation.GetSpecialType(SpecialType.System_Int32));

            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                closedInterface, Compiler.GenericUseSite.TypeReference), Is.Null);
            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                staticHelper, Compiler.GenericUseSite.TypeReference), Is.Null);
        }

        [TestCase("public interface IContract { const int Value = 1; }")]
        [TestCase("public interface IContract { class Nested { } }")]
        public void GenericRestrictions_RejectUnsupportedInterfaceMembers(string source)
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(source);
            ISymbol member = compilation.GetTypeByMetadataName("IContract").GetMembers().Single();

            string violation = Compiler.GenericRestrictionPolicy.GetUnsupportedInterfaceMemberViolation(member);

            Assert.That(violation, Does.Contain("not supported"));
        }

        [Test]
        public void GenericRestrictions_MultipleConcreteBasesRemainRoslynBuildError()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation(@"
public class First { }
public class Second { }
public class Invalid : First, Second { }");

            Assert.That(compilation.GetDiagnostics().Select(diagnostic => diagnostic.Id), Does.Contain("CS1721"));
        }

        private static CSharpCompilation CreateGenericRestrictionCompilation(string source)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            return CSharpCompilation.Create("GenericRestrictionTest", new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
