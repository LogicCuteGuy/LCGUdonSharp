using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace UdonSharp.Tests
{
    public sealed class LCGExceptionCompilerTests
    {
        [Test]
        public void ExceptionEnum_SerializerRoundTripsIntegerStorage()
        {
            var serializer = Serialization.Serializer.CreatePooled<UdonExceptionKind>();
            Assert.That(serializer.GetUdonStorageType(), Is.EqualTo(typeof(int)));
            Assert.That(UdonSharpUtils.UserTypeToUdonType(typeof(UdonExceptionKind)), Is.EqualTo(typeof(int)));
            var storage = new Serialization.SimpleValueStorage<int>();
            foreach (int value in new[] { 0, (int)UdonExceptionKind.NotSupported, -1, 12345 })
            {
                var source = (UdonExceptionKind)value;
                serializer.Write(storage, in source);
                Assert.That(storage.Value, Is.EqualTo(value));
                var result = default(UdonExceptionKind);
                serializer.Read(ref result, storage);
                Assert.That(result, Is.EqualTo(source));
            }
        }

        [Test]
        public void ExceptionEnum_ArraySerializerRoundTripsIntegerStorage()
        {
            var serializer = Serialization.Serializer.CreatePooled<UdonExceptionKind[]>();
            Assert.That(serializer.GetUdonStorageType(), Is.EqualTo(typeof(int[])));
            var storage = new Serialization.SimpleValueStorage<int[]>();
            var source = new[] { UdonExceptionKind.NotSupported, (UdonExceptionKind)12345 };
            serializer.Write(storage, in source);
            Assert.That(storage.Value, Is.EqualTo(new[] { (int)source[0], (int)source[1] }));
            UdonExceptionKind[] result = null;
            serializer.Read(ref result, storage);
            Assert.That(result, Is.EqualTo(source));
            source = Array.Empty<UdonExceptionKind>();
            serializer.Write(storage, in source);
            serializer.Read(ref result, storage);
            Assert.That(result, Is.Empty);
            source = null;
            serializer.Write(storage, in source);
            serializer.Read(ref result, storage);
            Assert.That(result, Is.Null);
        }

        [TestCase(typeof(UdonExceptionKind), false)]
        [TestCase(typeof(DayOfWeek), true)]
        public void MetadataEnum_UsesCorrectExternClassification(Type runtimeType, bool expected)
        {
            var references = new[] { typeof(object).Assembly.Location, runtimeType.Assembly.Location }
                .Distinct().Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("EnumClassification", references: references);
            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName(runtimeType.FullName);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.TypeKind, Is.EqualTo(TypeKind.Enum));
            Assert.That(symbol.Locations[0].IsInMetadata, Is.True);
            Type resolver = typeof(Compiler.UdonSharpCompilerV1).Assembly.GetType(
                "UdonSharp.Compiler.Binder.ExternResolverExtensions", true);
            MethodInfo resolveAssembly = resolver.GetMethod("GetExternAssembly", BindingFlags.Public | BindingFlags.Static);
            Assert.That(resolveAssembly.Invoke(null, new object[] { symbol }), Is.EqualTo(runtimeType.Assembly),
                "Metadata enums must retain their C# assembly even when lowered to integer Udon storage.");
            MethodInfo classify = resolver.GetMethod("IsExternType", BindingFlags.Public | BindingFlags.Static);
            Assert.That(classify.Invoke(null, new object[] { symbol }), Is.EqualTo(expected));
        }
    }
}
