using System;
using System.Collections.Generic;
using NUnit.Framework;
using UdonSharp.Serialization;
using UdonSharpEditor;
using VRC.SDK3.Data;

namespace UdonSharp.Tests
{
    public sealed class LCGCollectionSerializationTests
    {
        private enum WideEnum : ulong { Maximum = ulong.MaxValue }

        [Test]
        public void PrimitiveElements_PreserveExactTokenTypes()
        {
            AssertElement(true, TokenType.Boolean);
            AssertElement(sbyte.MinValue, TokenType.SByte);
            AssertElement(byte.MaxValue, TokenType.Byte);
            AssertElement(short.MinValue, TokenType.Short);
            AssertElement(ushort.MaxValue, TokenType.UShort);
            AssertElement(int.MinValue, TokenType.Int);
            AssertElement(uint.MaxValue, TokenType.UInt);
            AssertElement(long.MinValue, TokenType.Long);
            AssertElement(ulong.MaxValue, TokenType.ULong);
            AssertElement(1.25f, TokenType.Float);
            AssertElement(1.25d, TokenType.Double);
            AssertElement("ไทย", TokenType.String);
        }

        private static void AssertElement<T>(T value, TokenType tokenType)
        {
            var source = new List<T> { value };
            var serializer = Serializer.CreatePooled<List<T>>();
            var storage = new SimpleValueStorage<DataList>();
            serializer.Write(storage, in source);
            Assert.That(storage.Value[0].TokenType, Is.EqualTo(tokenType));
            Assert.That(serializer.Deserialize(storage), Is.EqualTo(source));
        }

        [Test]
        public void ListAndDictionary_UseLoweredStorageAndRoundTrip()
        {
            var list = new List<int> { int.MinValue, 0, int.MaxValue };
            var storage = new SimpleValueStorage<DataList>();
            var serializer = Serializer.CreatePooled<List<int>>();
            Assert.That(serializer.GetUdonStorageType(), Is.EqualTo(typeof(DataList)));
            serializer.Write(storage, in list);
            Assert.That(storage.Value[2].TokenType, Is.EqualTo(TokenType.Int));
            Assert.That(serializer.Deserialize(storage), Is.EqualTo(list));

            var dictionary = new Dictionary<string, int> { { "alpha", 10 }, { "beta", 20 } };
            var dictionaryStorage = new SimpleValueStorage<DataDictionary>();
            var dictionarySerializer = Serializer.CreatePooled<Dictionary<string, int>>();
            Assert.That(dictionarySerializer.GetUdonStorageType(), Is.EqualTo(typeof(DataDictionary)));
            dictionarySerializer.Write(dictionaryStorage, in dictionary);
            Assert.That(dictionaryStorage.Value["beta"].Int, Is.EqualTo(20));
            Assert.That(dictionarySerializer.Deserialize(dictionaryStorage), Is.EqualTo(dictionary));
        }

        [Test]
        public void NullAndEmpty_CollectionsStayDistinct()
        {
            var serializer = Serializer.CreatePooled<List<string>>();
            var storage = new SimpleValueStorage<DataList>(new DataList());
            List<string> value = null;
            serializer.Write(storage, in value);
            Assert.That(storage.Value, Is.Null);
            Assert.That(serializer.Deserialize(storage), Is.Null);
            value = new List<string>();
            serializer.Write(storage, in value);
            Assert.That(serializer.Deserialize(storage), Is.Empty);
            value.Add(null);
            serializer.Write(storage, in value);
            Assert.That(serializer.Deserialize(storage), Is.EqualTo(value));

            var dictionaries = Serializer.CreatePooled<Dictionary<int, string>>();
            var dictionaryStorage = new SimpleValueStorage<DataDictionary>();
            Dictionary<int, string> dictionary = null;
            dictionaries.Write(dictionaryStorage, in dictionary);
            Assert.That(dictionaries.Deserialize(dictionaryStorage), Is.Null);
            dictionary = new Dictionary<int, string>();
            dictionaries.Write(dictionaryStorage, in dictionary);
            Assert.That(dictionaries.Deserialize(dictionaryStorage), Is.Empty);
        }

        [Test]
        public void NestedCollections_UseContainerTokens()
        {
            var source = new Dictionary<int, List<string>> { { 7, new List<string> { "ไทย", null } }, { 9, null } };
            var storage = new SimpleValueStorage<DataDictionary>();
            var serializer = Serializer.CreatePooled<Dictionary<int, List<string>>>();
            serializer.Write(storage, in source);
            Assert.That(storage.Value[7].TokenType, Is.EqualTo(TokenType.DataList));
            var result = serializer.Deserialize(storage);
            Assert.That(result[7], Is.EqualTo(source[7]));
            Assert.That(result[9], Is.Null);
        }

        [Test]
        public void Enums_UseUnderlyingNumericTokensIncludingExternEnums()
        {
            var source = new Dictionary<DayOfWeek, WideEnum> { { DayOfWeek.Friday, WideEnum.Maximum } };
            var storage = new SimpleValueStorage<DataDictionary>();
            var serializer = Serializer.CreatePooled<Dictionary<DayOfWeek, WideEnum>>();
            serializer.Write(storage, in source);
            Assert.That(storage.Value.GetKeys()[0].TokenType, Is.EqualTo(TokenType.Int));
            Assert.That(storage.Value[(int)DayOfWeek.Friday].ULong, Is.EqualTo(ulong.MaxValue));
            Assert.That(serializer.Deserialize(storage), Is.EqualTo(source));
        }

        [Test]
        public void ObjectElements_KeepReferenceTokenRepresentation()
        {
            var source = new List<object> { 42, "text", null };
            var storage = new SimpleValueStorage<DataList>();
            var serializer = Serializer.CreatePooled<List<object>>();
            serializer.Write(storage, in source);
            Assert.That(storage.Value[0].TokenType, Is.EqualTo(TokenType.Reference));
            Assert.That(serializer.Deserialize(storage), Is.EqualTo(source));
        }

        [Test]
        public void CollectionArrays_UseLoweredElementStorage()
        {
            var source = new[] { new List<int> { 7 }, null, new List<int>() };
            var serializer = Serializer.CreatePooled<List<int>[]>();
            Assert.That(serializer.GetUdonStorageType(), Is.EqualTo(typeof(DataList[])));
            var storage = new SimpleValueStorage<DataList[]>();
            serializer.Write(storage, in source);
            var result = serializer.Deserialize(storage);
            Assert.That(result[0], Is.EqualTo(source[0]));
            Assert.That(result[1], Is.Null);
            Assert.That(result[2], Is.Empty);
        }

        [Test]
        public void DependencyTraversal_DoesNotOverwriteCollections()
        {
            var previous = UsbSerializationContext.CurrentPolicy;
            try
            {
                UsbSerializationContext.CurrentPolicy = ProxySerializationPolicy.CollectRootDependencies;
                var serializer = Serializer.CreatePooled<Dictionary<string, List<int>>>();
                var originalStorage = new DataDictionary { { "kept", new DataToken(new DataList()) } };
                var storage = new SimpleValueStorage<DataDictionary>(originalStorage);
                var source = new Dictionary<string, List<int>> { { "a", new List<int> { 1 } }, { "b", null } };
                var originalSource = source;
                serializer.Write(storage, in source);
                Assert.That(storage.Value, Is.SameAs(originalStorage));
                serializer.Read(ref source, storage);
                Assert.That(source, Is.SameAs(originalSource));
                Assert.That(source["a"], Is.EqualTo(new[] { 1 }));
            }
            finally { UsbSerializationContext.CurrentPolicy = previous; }
        }

        [Test]
        public void CollectionDetection_LeavesOtherGenericTypesAlone()
        {
            Assert.That(CollectionSerializer<object>.IsCollection(typeof(HashSet<int>)), Is.False);
            Assert.That(CollectionSerializer<object>.IsCollection(typeof(IList<int>)), Is.False);
            Assert.That(UdonSharpUtils.UserTypeToUdonType(typeof(List<int>)), Is.EqualTo(typeof(DataList)));
            Assert.That(UdonSharpUtils.UserTypeToUdonType(typeof(Dictionary<string, int>)), Is.EqualTo(typeof(DataDictionary)));
        }
    }
}
