using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
    }
}
