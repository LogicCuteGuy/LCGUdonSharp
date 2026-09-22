using System;
using System.IO;
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
            MethodInfo stringTaskOverload = typeof(VRCAsync).GetMethod(nameof(VRCAsync.LoadStringAsync),
                new[] { typeof(VRC.SDKBase.VRCUrl) });
            MethodInfo stringOutOverload = typeof(VRCAsync).GetMethod(nameof(VRCAsync.LoadStringAsync),
                new[]
                {
                    typeof(VRC.SDKBase.VRCUrl),
                    typeof(VRC.SDK3.StringLoading.IVRCStringDownload).MakeByRefType(),
                });
            Assert.That(stringTaskOverload.ReturnType,
                Is.EqualTo(typeof(Task<VRC.SDK3.StringLoading.IVRCStringDownload>)));
            Assert.That(stringOutOverload, Is.Not.Null);
            Assert.That(stringOutOverload.ReturnType, Is.EqualTo(typeof(Task)));
            Assert.That(stringOutOverload.GetParameters()[1].IsOut, Is.True);
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
        public void AsyncLowering_RewritesYieldAndDelayIntoUdonContinuations()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
public class Sample
{
    public async void Run()
    {
        Before();
        await Task.Yield();
        Middle();
        await Task.Delay(250);
        After();
    }

    private void Before() { }
    private void Middle() { }
    private void After() { }
    private void SendCustomEventDelayedFrames(string eventName, int frames) { }
    private void SendCustomEventDelayedSeconds(string eventName, float seconds) { }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            string lowered = result.Tree.GetRoot().NormalizeWhitespace().ToFullString();

            Assert.That(result.Diagnostics, Is.Empty,
                string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.That(lowered, Does.Not.Contain("async void"));
            Assert.That(lowered, Does.Not.Contain("await "));
            Assert.That(lowered, Does.Contain("SendCustomEventDelayedFrames"));
            Assert.That(lowered, Does.Contain("SendCustomEventDelayedSeconds"));
            Assert.That(lowered, Does.Contain("_Run_resume"));
            Assert.That(lowered, Does.Contain("case 1:"));
            Assert.That(lowered, Does.Contain("case 2:"));

            CSharpCompilation loweredCompilation = CSharpCompilation.Create("AsyncLoweringTest",
                new[] { result.Tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(loweredCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                Is.Empty);
        }

        [Test]
        public void AsyncLowering_LeavesLegacyTreeUntouchedAndDiagnosesUnsupportedAwaiters()
        {
            SyntaxTree legacy = CSharpSyntaxTree.ParseText("public class Legacy { public void Run() { } }");
            Compiler.Lowering.AsyncSyntaxLoweringResult legacyResult =
                Compiler.Lowering.AsyncSyntaxLowerer.Rewrite(legacy);
            Assert.That(legacyResult.Changed, Is.False);
            Assert.That(legacyResult.Tree, Is.SameAs(legacy));

            SyntaxTree ordinaryAsync = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
public class OrdinaryCSharp { public async Task Run() { await Task.Yield(); } }");
            Compiler.Lowering.AsyncSyntaxLoweringResult filteredResult =
                Compiler.Lowering.AsyncSyntaxLowerer.Rewrite(ordinaryAsync, declaration => false);
            Assert.That(filteredResult.Changed, Is.False);
            Assert.That(filteredResult.Diagnostics, Is.Empty);
            Assert.That(filteredResult.Tree, Is.SameAs(ordinaryAsync));

            SyntaxTree unsupported = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
public class Sample { public async void Run() { await Task.Run(() => { }); } }");
            Compiler.Lowering.AsyncSyntaxLoweringResult unsupportedResult =
                RewriteAsyncWithSemantics(unsupported);
            Assert.That(unsupportedResult.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("Only Task.Yield()"));
            Assert.That(unsupportedResult.Changed, Is.False);
        }

        [Test]
        public void AsyncLowering_UsesExactTaskSymbolsAndRejectsUnsupportedMethodShapes()
        {
            SyntaxTree aliasSource = CSharpSyntaxTree.ParseText(@"
using AsyncTask = System.Threading.Tasks.Task;
public class Sample
{
    public async void Run() { await AsyncTask.Yield(); }
    private void SendCustomEventDelayedFrames(string name, int frames) { }
}");
            Assert.That(RewriteAsyncWithSemantics(aliasSource).Diagnostics, Is.Empty);

            SyntaxTree timeSpanDelay = CSharpSyntaxTree.ParseText(@"
using System;
using System.Threading.Tasks;
public class Sample { public async void Run() { await Task.Delay(TimeSpan.FromSeconds(1)); } }");
            Assert.That(RewriteAsyncWithSemantics(timeSpanDelay).Diagnostics.Select(d => d.Message),
                Has.Some.Contains("Only Task.Yield()"));

            SyntaxTree customTask = CSharpSyntaxTree.ParseText(@"
public static class Task
{
    public static System.Runtime.CompilerServices.YieldAwaitable Yield()
        => System.Threading.Tasks.Task.Yield();
}
public class Sample { public async void Run() { await Task.Yield(); } }");
            Assert.That(RewriteAsyncWithSemantics(customTask).Diagnostics.Select(d => d.Message),
                Has.Some.Contains("Only Task.Yield()"));

            string[] rejectedSources =
            {
                "using System.Threading.Tasks; public class Sample { public static async void Run() { await Task.Yield(); } }",
                "using System.Threading.Tasks; public class Sample { public async void Run<T>() { await Task.Yield(); } }",
                "using System.Threading.Tasks; public class Sample { public async void Run() { M(out int value); await Task.Yield(); } private void M(out int value) { value = 1; } }",
                "using System.Threading.Tasks; public class Sample { public async void Run() { if (this is Sample value) { } await Task.Yield(); } }",
                "using System.Threading.Tasks; public class Sample { public async void Run() { await Task.Delay(0); } }",
                "using System.Threading.Tasks; public class Sample { public int delay = 1; public async void Run() { await Task.Delay(delay); } }",
                @"using System;
using System.Threading.Tasks;
using VRC.SDK3.UdonNetworkCalling;
namespace VRC.SDK3.UdonNetworkCalling
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class NetworkCallableAttribute : Attribute { }
}
public class Sample
{
    [NetworkCallable] public async void Run() { await Task.Yield(); }
}",
            };

            foreach (string rejectedSource in rejectedSources)
                Assert.That(RewriteAsyncWithSemantics(CSharpSyntaxTree.ParseText(rejectedSource)).Diagnostics,
                    Is.Not.Empty, rejectedSource);
        }

        [Test]
        public void AsyncLowering_UsesDistinctContinuationEventsAcrossInheritance()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
public class Base
{
    public async void Run() { await Task.Yield(); }
    protected void SendCustomEventDelayedFrames(string name, int frames) { }
}
public class Derived : Base
{
    public new async void Run() { await Task.Yield(); }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            string[] continuationNames = result.Tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .Select(method => method.Identifier.ValueText)
                .Where(name => name.StartsWith("__uasync_") && name.EndsWith("_Run_resume"))
                .ToArray();

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(continuationNames, Has.Length.EqualTo(2));
            Assert.That(continuationNames.Distinct(), Has.Count.EqualTo(2));
        }

        [Test]
        public void AsyncLowering_WrapsStringCallbacksAfterLegacyBody()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
namespace VRC.SDKBase { public class VRCUrl { public string Get() => """"; } }
namespace VRC.Udon.Common.Interfaces { public interface IUdonEventReceiver { } }
namespace VRC.SDK3.StringLoading
{
    public interface IVRCStringDownload { VRC.SDKBase.VRCUrl Url { get; } }
    public static class VRCStringDownloader
    {
        public static void LoadUrl(VRC.SDKBase.VRCUrl url, VRC.Udon.Common.Interfaces.IUdonEventReceiver receiver) { }
    }
}
namespace UdonSharp
{
    public static class VRCAsync
    {
        public static Task<VRC.SDK3.StringLoading.IVRCStringDownload> LoadStringAsync(VRC.SDKBase.VRCUrl url) => null;
        public static Task LoadStringAsync(VRC.SDKBase.VRCUrl url,
            out VRC.SDK3.StringLoading.IVRCStringDownload result) { result = null; return null; }
    }
}
public class Sample : VRC.Udon.Common.Interfaces.IUdonEventReceiver
{
    private VRC.SDKBase.VRCUrl url;
    private VRC.SDK3.StringLoading.IVRCStringDownload result;
    public async void Run()
    {
        await UdonSharp.VRCAsync.LoadStringAsync(url, out result);
        Continued(result);
    }
    public void OnStringLoadSuccess(VRC.SDK3.StringLoading.IVRCStringDownload result) { Legacy(); }
    public void OnStringLoadError(VRC.SDK3.StringLoading.IVRCStringDownload result) { Legacy(); }
    private void Legacy() { }
    private void Continued(VRC.SDK3.StringLoading.IVRCStringDownload result) { }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            SyntaxNode loweredRoot = result.Tree.GetRoot();
            string lowered = loweredRoot.ToFullString();
            string[] callbacks = loweredRoot.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "OnStringLoadSuccess" ||
                                 method.Identifier.ValueText == "OnStringLoadError")
                .Select(method => method.NormalizeWhitespace().ToFullString())
                .ToArray();
            Assert.That(result.Diagnostics, Is.Empty,
                string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.That(lowered, Does.Contain("VRCStringDownloader.LoadUrl"));
            Assert.That(lowered, Does.Contain("result.Url.Get() == __uasync_"));
            Assert.That(callbacks, Has.Length.EqualTo(2));
            foreach (string callback in callbacks)
            {
                Assert.That(callback, Does.Contain("this.result = result;"));
                Assert.That(callback.IndexOf("Legacy();", StringComparison.Ordinal),
                    Is.LessThan(callback.IndexOf("this.result = result;", StringComparison.Ordinal)));
                Assert.That(callback.IndexOf("this.result = result;", StringComparison.Ordinal),
                    Is.LessThan(callback.IndexOf("_resume();", StringComparison.Ordinal)));
            }
            Assert.That(lowered, Does.Not.Contain("await UdonSharp.VRCAsync"));
        }

        [Test]
        public void AsyncLowering_RejectsUnsupportedStringOutTargets()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
namespace VRC.SDKBase { public class VRCUrl { public string Get() => """"; } }
namespace VRC.SDK3.StringLoading { public interface IVRCStringDownload { VRC.SDKBase.VRCUrl Url { get; } } }
namespace UdonSharp
{
    public static class VRCAsync
    {
        public static Task LoadStringAsync(VRC.SDKBase.VRCUrl url,
            out VRC.SDK3.StringLoading.IVRCStringDownload result) { result = null; return null; }
    }
}
public class PropertyTarget
{
    private VRC.SDKBase.VRCUrl url;
    private VRC.SDK3.StringLoading.IVRCStringDownload Result { get; set; }
    public async void Run() { await UdonSharp.VRCAsync.LoadStringAsync(url, out Result); }
}
public class MultiDimensionalTarget
{
    private VRC.SDKBase.VRCUrl url;
    private VRC.SDK3.StringLoading.IVRCStringDownload[,] results =
        new VRC.SDK3.StringLoading.IVRCStringDownload[1, 1];
    public async void Run() { await UdonSharp.VRCAsync.LoadStringAsync(url, out results[0, 0]); }
}
public class ArrayElementTarget
{
    private VRC.SDKBase.VRCUrl url;
    private VRC.SDK3.StringLoading.IVRCStringDownload[] results =
        new VRC.SDK3.StringLoading.IVRCStringDownload[1];
    public async void Run() { await UdonSharp.VRCAsync.LoadStringAsync(url, out results[0]); }
}
public class LocalTarget
{
    private VRC.SDKBase.VRCUrl url;
    public async void Run()
    {
        await UdonSharp.VRCAsync.LoadStringAsync(url,
            out VRC.SDK3.StringLoading.IVRCStringDownload result);
    }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            string[] diagnostics = result.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray();

            Assert.That(diagnostics.Count(message => message.Contains(
                "instance behaviour field")), Is.EqualTo(3));
            Assert.That(diagnostics, Has.Some.Contains("Locals in async Udon methods are not supported"));
        }

        [Test]
        public void AsyncLowering_WrapsImageCallbacksAndMatchesRequestIdentity()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
namespace UnityEngine { public class Material { } }
namespace VRC.SDKBase { public class VRCUrl { } }
namespace VRC.Udon.Common.Interfaces { public interface IUdonEventReceiver { } }
namespace VRC.SDK3.Image
{
    public class TextureInfo { }
    public interface IVRCImageDownload { }
    public class VRCImageDownloader
    {
        public IVRCImageDownload DownloadImage(VRC.SDKBase.VRCUrl url, UnityEngine.Material material,
            VRC.Udon.Common.Interfaces.IUdonEventReceiver receiver, TextureInfo textureInfo) => null;
    }
}
namespace UdonSharp
{
    public static class VRCAsync
    {
        public static Task<VRC.SDK3.Image.IVRCImageDownload> LoadImageAsync(
            VRC.SDK3.Image.VRCImageDownloader downloader, VRC.SDKBase.VRCUrl url,
            UnityEngine.Material material = null, VRC.SDK3.Image.TextureInfo textureInfo = null) => null;
    }
}
public class Sample : VRC.Udon.Common.Interfaces.IUdonEventReceiver
{
    private VRC.SDK3.Image.VRCImageDownloader downloader;
    private VRC.SDKBase.VRCUrl url;
    public async void Run()
    {
        await UdonSharp.VRCAsync.LoadImageAsync(downloader, url);
        Continued();
    }
    public void OnImageLoadSuccess(VRC.SDK3.Image.IVRCImageDownload result) { Legacy(); }
    public void OnImageLoadError(VRC.SDK3.Image.IVRCImageDownload result) { Legacy(); }
    private void Legacy() { }
    private void Continued() { }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(lowered, Does.Contain("DownloadImage"));
            Assert.That(lowered, Does.Contain("result == __uasync_"));
            Assert.That(lowered.IndexOf("Legacy();", StringComparison.Ordinal),
                Is.LessThan(lowered.LastIndexOf("_resume();", StringComparison.Ordinal)));
            Assert.That(lowered, Does.Not.Contain("await UdonSharp.VRCAsync"));
        }

        [Test]
        public void AsyncLowering_LowersVideoGpuSerializationAndEconomyAdapters()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
namespace UdonSharp
{
    public enum BehaviourSyncMode { Manual = 4 }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UdonBehaviourSyncModeAttribute : System.Attribute
    {
        public UdonBehaviourSyncModeAttribute(BehaviourSyncMode mode) { }
    }
    public static class VRCAsync
    {
        public static Task<object> LoadVideoAsync(object player, object url, bool playWhenReady = false) => null;
        public static Task<object> WaitForVideoEndAsync(object player) => null;
        public static Task<object> RequestGPUReadbackAsync(object source, int mipIndex = 0) => null;
        public static Task<object> RequestSerializationAsync() => null;
        public static Task<object> ListAvailableProductsAsync() => null;
        public static Task<object> ListPurchasesAsync(object player) => null;
        public static Task<object> ListProductOwnersAsync(object product) => null;
    }
}
public class VideoLoad
{
    private object player, url;
    public async void Run() { await UdonSharp.VRCAsync.LoadVideoAsync(player, url, true); Continued(); }
    public void OnVideoReady() { Legacy(); }
    public void OnVideoError(object error) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
public class VideoEnd
{
    private object player;
    public async void Run() { await UdonSharp.VRCAsync.WaitForVideoEndAsync(player); Continued(); }
    public void OnVideoEnd() { Legacy(); }
    public void OnVideoError(object error) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
public class Gpu
{
    private object source;
    public async void Run() { await UdonSharp.VRCAsync.RequestGPUReadbackAsync(source); Continued(); }
    public void OnAsyncGpuReadbackComplete(object request) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
[UdonSharp.UdonBehaviourSyncMode(UdonSharp.BehaviourSyncMode.Manual)]
public class Serialization
{
    public async void Run() { await UdonSharp.VRCAsync.RequestSerializationAsync(); Continued(); }
    public void OnPostSerialization(object result) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
public class Available
{
    public async void Run() { await UdonSharp.VRCAsync.ListAvailableProductsAsync(); Continued(); }
    public void OnListAvailableProducts(object products) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
public class Purchases
{
    private object player;
    public async void Run() { await UdonSharp.VRCAsync.ListPurchasesAsync(player); Continued(); }
    public void OnListPurchases(object products, object callbackPlayer) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
public class Owners
{
    private object product;
    public async void Run() { await UdonSharp.VRCAsync.ListProductOwnersAsync(product); Continued(); }
    public void OnListProductOwners(object callbackProduct, object owners) { Legacy(); }
    private void Legacy() { } private void Continued() { }
}
");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(lowered, Does.Contain(".LoadURL("));
            Assert.That(lowered, Does.Contain(".Play();"));
            Assert.That(lowered, Does.Contain("VRCAsyncGPUReadback.Request"));
            Assert.That(lowered, Does.Contain("RequestSerialization();"));
            Assert.That(lowered, Does.Contain("Store.ListAvailableProducts"));
            Assert.That(lowered, Does.Contain("Store.ListPurchases"));
            Assert.That(lowered, Does.Contain("Store.ListProductOwners"));
            Assert.That(lowered, Does.Not.Contain("await UdonSharp.VRCAsync"));
        }

        [Test]
        public void AsyncLowering_RealSdkExamplesProduceValidLoweredCSharp()
        {
            string[] examplePaths =
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
            MetadataReference[] references = new[]
                {
                    typeof(object).Assembly.Location,
                    Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location),
                        "Facades", "netstandard.dll"),
                    typeof(Task).Assembly.Location,
                    typeof(UnityEngine.Debug).Assembly.Location,
                    typeof(UdonSharpBehaviour).Assembly.Location,
                    typeof(VRC.SDKBase.VRCUrl).Assembly.Location,
                    typeof(VRC.SDK3.Image.VRCImageDownloader).Assembly.Location,
                    typeof(VRC.Udon.Common.SerializationResult).Assembly.Location,
                    Assembly.Load("VRC.Udon.Serialization.OdinSerializer").Location,
                    Path.GetFullPath("Packages/com.vrchat.worlds/Runtime/VRCSDK/Plugins/VRCEconomy.dll"),
                }
                .Distinct()
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();

            foreach (string examplePath in examplePaths)
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(examplePath), path: examplePath);
                CSharpCompilation compilation = CSharpCompilation.Create("RealSdkAsyncExample", new[] { tree },
                    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Compiler.Lowering.AsyncSyntaxLoweringResult result =
                    Compiler.Lowering.AsyncSyntaxLowerer.Rewrite(tree, declaration => true,
                        compilation.GetSemanticModel(tree));

                Assert.That(result.Diagnostics, Is.Empty, examplePath + ": " +
                    string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
                Assert.That(result.Tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AwaitExpressionSyntax>(),
                    Is.Empty, examplePath);

                CSharpCompilation loweredCompilation = compilation.ReplaceSyntaxTree(tree, result.Tree);
                Assert.That(loweredCompilation.GetDiagnostics()
                        .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                    Is.Empty, examplePath);
            }
        }

        [Test]
        public void AsyncLowering_RejectsSerializationAwaitWithoutManualSyncMode()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Threading.Tasks;
namespace UdonSharp
{
    public static class VRCAsync
    {
        public static Task<object> RequestSerializationAsync() => null;
    }
}
public class NotManual
{
    public async void Run() { await UdonSharp.VRCAsync.RequestSerializationAsync(); }
}");

            Compiler.Lowering.AsyncSyntaxLoweringResult result = RewriteAsyncWithSemantics(source);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("BehaviourSyncMode.Manual"));
        }

        [Test]
        public void ExtendedSyntaxLowering_LowersConcreteDynamicLocals()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
public class Sample
{
    public int Run()
    {
        dynamic value = 41;
        return value + 1;
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Changed, Is.True);
            Assert.That(lowered, Does.Not.Contain("dynamic value"));
            Assert.That(lowered, Does.Contain("int value"));
            AssertExtendedTreeCompiles(result);
        }

        [Test]
        public void ExtendedSyntaxLowering_LowersArrayLinqAndCapturedLambdasToLoops()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Linq;
public class Sample
{
    public int[] Run(int[] values, int minimum, int scale)
    {
        int[] result = values.Where(value => value >= minimum)
            .Select(value => value * scale).ToArray();
        return result;
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Changed, Is.True);
            Assert.That(lowered, Does.Not.Contain(".Where"));
            Assert.That(lowered, Does.Not.Contain(".Select"));
            Assert.That(lowered, Does.Contain("minimum"));
            Assert.That(lowered, Does.Contain("scale"));
            Assert.That(lowered, Does.Contain("for ("));
            AssertExtendedTreeCompiles(result);
        }

        [Test]
        public void ExtendedSyntaxLowering_DiagnosesEscapingDelegatesAndUnprovenDynamic()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System;
public class Sample
{
    public Func<int, int> Escape(int amount) => value => value + amount;
    public int Read(dynamic value) => value.Missing();
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("Escaping delegates"));
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("runtime dynamic dispatch"));
        }

        [Test]
        public void ExtendedSyntaxLowering_LowersArrayBackedSpanLocals()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System;
public class Sample
{
    public int[] Run(int[] values)
    {
        Span<int> window = values.AsSpan(1, 3);
        window[0] = 9;
        int length = window.Length;
        window.Fill(length);
        int[] copy = window.ToArray();
        window.Clear();
        return copy;
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Changed, Is.True);
            Assert.That(lowered, Does.Not.Contain("Span<int>"));
            Assert.That(lowered, Does.Not.Contain(".Fill"));
            Assert.That(lowered, Does.Not.Contain(".Clear"));
            Assert.That(lowered, Does.Contain("__uspan_"));
            Assert.That(lowered, Does.Contain("for ("));
            AssertExtendedTreeCompiles(result);
        }

        [Test]
        public void ExtendedSyntaxLowering_DiagnosesSpanParameters()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System;
public class Sample { public int Read(Span<int> value) => value[0]; }");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("Only method-local array-backed"));
        }

        [Test]
        public void ExtendedSyntaxLowering_BindsNamedSpanArgumentsAndParentSliceBounds()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System;
public class Sample
{
    public int NextStart() => 1;
    public int NextLength() => 3;
    public int[] Run(int[] values)
    {
        Span<int> parent = values.AsSpan(length: NextLength(), start: NextStart());
        Span<int> child = parent.Slice(length: 1, start: parent[0]);
        child.Fill(parent[0]);
        if (child.Length > 0)
            child[0] = parent[0];
        return child.ToArray();
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            string lowered = result.Tree.GetRoot().ToFullString();
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(lowered, Does.Contain("_parent_length"));
            Assert.That(lowered, Does.Contain("_parent_offset"));
            Assert.That(lowered, Does.Not.Contain("parent[0]"));
            Assert.That(lowered.IndexOf("= NextLength();", StringComparison.Ordinal),
                Is.LessThan(lowered.IndexOf("= NextStart();", StringComparison.Ordinal)));
            AssertExtendedTreeCompiles(result);
        }

        [Test]
        public void ExtendedSyntaxLowering_DoesNotBypassCustomAsSpanExtensions()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System;
public static class CustomExtensions
{
    public static Span<int> AsSpan(this int[] values, int start, int length)
        => new Span<int>(values, start + 1, length);
}
public class Sample
{
    public void Run(int[] values)
    {
        Span<int> custom = values.AsSpan(0, 1);
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("Only method-local array-backed"));
        }

        [Test]
        public void ExtendedSyntaxLowering_DoesNotTreatEscapedNamesAsDynamicOrSystemSpan()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
public class Span<T> { }
public class Sample
{
    public int Run()
    {
        int @dynamic = 1;
        Span<int> value = null;
        return @dynamic;
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Changed, Is.False);
        }

        [Test]
        public void ExtendedSyntaxLowering_RejectsWhereAfterSelectUntilProjectionSpillingExists()
        {
            SyntaxTree source = CSharpSyntaxTree.ParseText(@"
using System.Linq;
public class Sample
{
    public int Next(int value) => value + 1;
    public int[] Run(int[] values)
    {
        int[] result = values.Select(value => Next(value))
            .Where(value => value > 0).ToArray();
        return result;
    }
}");

            Compiler.Lowering.ExtendedSyntaxLoweringResult result = RewriteExtendedWithSemantics(source);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Message),
                Has.Some.Contains("Where after Select"));
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
        public void GenericRestrictions_AllowSdkContactProxyAndInheritedMemberTypes()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation("public class Sample { }")
                .AddReferences(MetadataReference.CreateFromFile(Assembly.Load("VRC.Dynamics").Location));
            INamedTypeSymbol sender = compilation.GetTypeByMetadataName("VRC.Dynamics.ContactSenderProxy");
            Assert.That(sender, Is.Not.Null);
            INamedTypeSymbol genericBase = sender.BaseType;
            Assert.That(genericBase.IsGenericType, Is.True);
            Assert.That(genericBase.GetMembers("player"), Is.Not.Empty);

            foreach (ITypeSymbol type in new ITypeSymbol[]
                { sender, genericBase, genericBase.OriginalDefinition, compilation.CreateArrayTypeSymbol(sender) })
                Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                    type, Compiler.GenericUseSite.TypeReference), Is.Null, type.ToDisplayString());
        }

        [Test]
        public void GenericRestrictions_MetadataReferenceExemptionDoesNotAllowAllocationOrRuntimeTypes()
        {
            CSharpCompilation compilation = CreateGenericRestrictionCompilation("public class Sample { }");
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")
                .Construct(compilation.GetSpecialType(SpecialType.System_Int32),
                    compilation.GetSpecialType(SpecialType.System_Int32));

            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.ObjectCreation), Does.Contain("generic heap objects"));
            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                type, Compiler.GenericUseSite.RuntimeType), Does.Contain("generic heap object types"));
            Assert.That(Compiler.GenericRestrictionPolicy.GetViolation(
                type.ConstructUnboundGenericType(), Compiler.GenericUseSite.TypeReference),
                Does.Contain("Open generic type"));
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

        private static Compiler.Lowering.AsyncSyntaxLoweringResult RewriteAsyncWithSemantics(SyntaxTree tree)
        {
            MetadataReference[] references = new[]
                {
                    typeof(object).Assembly.Location,
                    typeof(Task).Assembly.Location,
                }
                .Distinct()
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
            CSharpCompilation compilation = CSharpCompilation.Create("AsyncSemanticTest", new[] { tree },
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return Compiler.Lowering.AsyncSyntaxLowerer.Rewrite(tree, declaration => true,
                compilation.GetSemanticModel(tree));
        }

        private static Compiler.Lowering.ExtendedSyntaxLoweringResult RewriteExtendedWithSemantics(SyntaxTree tree)
        {
            MetadataReference[] references = new[]
                {
                    typeof(object).Assembly.Location,
                    typeof(Enumerable).Assembly.Location,
                    typeof(System.Dynamic.DynamicObject).Assembly.Location,
                    typeof(Span<>).Assembly.Location,
                    typeof(Debug).Assembly.Location,
                }
                .Distinct()
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
            CSharpCompilation compilation = CSharpCompilation.Create("ExtendedSemanticTest", new[] { tree },
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return Compiler.Lowering.ExtendedSyntaxLowerer.Rewrite(tree, declaration => true,
                compilation.GetSemanticModel(tree));
        }

        private static void AssertExtendedTreeCompiles(
            Compiler.Lowering.ExtendedSyntaxLoweringResult result)
        {
            MetadataReference[] references = new[]
                {
                    typeof(object).Assembly.Location,
                    typeof(Enumerable).Assembly.Location,
                    typeof(Span<>).Assembly.Location,
                    typeof(Debug).Assembly.Location,
                }
                .Distinct()
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
            CSharpCompilation compilation = CSharpCompilation.Create("LoweredExtendedSemanticTest",
                new[] { result.Tree }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                Is.Empty);
        }
    }
}
