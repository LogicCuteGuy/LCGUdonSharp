using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using VRC.SDK3.Data;

namespace UdonSharp.Serialization
{
    // Matches CollectionSyntaxLowerer's exact List<> and Dictionary<,> erasure.
    internal sealed class CollectionSerializer<T> : Serializer<T>
    {
        public CollectionSerializer(TypeSerializationMetadata metadata) : base(metadata) { }

        internal static bool IsCollection(Type type)
        {
            if (!type.IsGenericType)
                return false;
            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(List<>) || definition == typeof(Dictionary<,>);
        }

        public override Type GetUdonStorageType() =>
            typeof(T).GetGenericTypeDefinition() == typeof(List<>) ? typeof(DataList) : typeof(DataDictionary);

        protected override bool HandlesTypeSerialization(TypeSerializationMetadata metadata)
        {
            VerifyTypeCheckSanity();
            return IsCollection(metadata.cSharpType);
        }

        protected override Serializer MakeSerializer(TypeSerializationMetadata metadata)
        {
            VerifyTypeCheckSanity();
            return (Serializer)Activator.CreateInstance(typeof(CollectionSerializer<>).MakeGenericType(metadata.cSharpType), metadata);
        }

        public override void Write(IValueStorage targetObject, in T sourceObject)
        {
            VerifySerializationSanity();
            if (targetObject == null)
            {
                UdonSharpUtils.LogError($"Field for {typeof(T)} does not exist");
                return;
            }
            object result = null;
            if ((object)sourceObject != null)
            {
                Type[] arguments = typeof(T).GetGenericArguments();
                if (sourceObject is IList list)
                {
                    var converted = new DataList();
                    foreach (object item in list)
                        converted.Add(CollectionTokenConverter.Write(arguments[0], item));
                    result = converted;
                }
                else
                {
                    var converted = new DataDictionary();
                    foreach (DictionaryEntry entry in (IDictionary)sourceObject)
                    {
                        DataToken key = CollectionTokenConverter.Write(arguments[0], entry.Key);
                        DataToken value = CollectionTokenConverter.Write(arguments[1], entry.Value);
                        // Dependency traversal intentionally produces no values.
                        if (!UsbSerializationContext.CollectDependencies)
                            converted.Add(key, value);
                    }
                    result = converted;
                }
            }
            if (!UsbSerializationContext.CollectDependencies)
                targetObject.Value = result;
        }

        public override void Read(ref T targetObject, IValueStorage sourceObject)
        {
            VerifySerializationSanity();
            if (sourceObject == null)
            {
                UdonSharpUtils.LogError($"Field for {typeof(T)} does not exist");
                return;
            }
            object result = null;
            if (sourceObject.Value != null)
            {
                Type[] arguments = typeof(T).GetGenericArguments();
                result = Activator.CreateInstance(typeof(T));
                if (sourceObject.Value is DataList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        object item = CollectionTokenConverter.Read(arguments[0], list[i]);
                        if (!UsbSerializationContext.CollectDependencies)
                            ((IList)result).Add(item);
                    }
                }
                else
                {
                    var dictionary = (DataDictionary)sourceObject.Value;
                    DataList keys = dictionary.GetKeys();
                    for (int i = 0; i < keys.Count; i++)
                    {
                        object key = CollectionTokenConverter.Read(arguments[0], keys[i]);
                        object value = CollectionTokenConverter.Read(arguments[1], dictionary[keys[i]]);
                        if (!UsbSerializationContext.CollectDependencies)
                            ((IDictionary)result).Add(key, value);
                    }
                }
            }
            if (!UsbSerializationContext.CollectDependencies)
                targetObject = (T)result;
        }
    }

    internal static class CollectionTokenConverter
    {
        // Select by the declared type: List<object> must box even numeric values as
        // Reference tokens, while nested collections use native container tokens.
        private static Type TokenValueType(Type type)
        {
            if (type.IsEnum)
                return Enum.GetUnderlyingType(type);
            if (CollectionSerializer<object>.IsCollection(type))
                return Serializer.CreatePooled(type).GetUdonStorageType();
            if (type == typeof(string) || type == typeof(bool) ||
                type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) ||
                type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double))
                return type;
            return typeof(object);
        }

        internal static DataToken Write(Type type, object value)
        {
            Type tokenType = TokenValueType(type);
            if (type.IsEnum)
                value = Convert.ChangeType(value, tokenType);
            else
            {
                Serializer serializer = Serializer.CreatePooled(type);
                IValueStorage storage = ValueStorageUtil.CreateStorage(serializer.GetUdonStorageType());
                serializer.WriteWeak(storage, value);
                value = storage.Value;
            }
            if (UsbSerializationContext.CollectDependencies)
                return default;
            ConstructorInfo constructor = typeof(DataToken).GetConstructor(new[] { tokenType });
            return (DataToken)constructor.Invoke(new[] { value });
        }

        internal static object Read(Type type, DataToken token)
        {
            object value;
            switch (token.TokenType)
            {
                case TokenType.Null: value = null; break;
                case TokenType.Boolean: value = token.Boolean; break;
                case TokenType.SByte: value = token.SByte; break;
                case TokenType.Byte: value = token.Byte; break;
                case TokenType.Short: value = token.Short; break;
                case TokenType.UShort: value = token.UShort; break;
                case TokenType.Int: value = token.Int; break;
                case TokenType.UInt: value = token.UInt; break;
                case TokenType.Long: value = token.Long; break;
                case TokenType.ULong: value = token.ULong; break;
                case TokenType.Float: value = token.Float; break;
                case TokenType.Double: value = token.Double; break;
                case TokenType.String: value = token.String; break;
                case TokenType.DataList: value = token.DataList; break;
                case TokenType.DataDictionary: value = token.DataDictionary; break;
                case TokenType.Reference: value = token.Reference; break;
                default: throw new InvalidOperationException($"Cannot deserialize {token.TokenType} as {type}.");
            }
            if (type.IsEnum)
                return Enum.ToObject(type, value);
            Serializer serializer = Serializer.CreatePooled(type);
            IValueStorage storage = ValueStorageUtil.CreateStorage(serializer.GetUdonStorageType());
            storage.Value = value;
            object result = null;
            serializer.ReadWeak(ref result, storage);
            return result;
        }
    }
}
