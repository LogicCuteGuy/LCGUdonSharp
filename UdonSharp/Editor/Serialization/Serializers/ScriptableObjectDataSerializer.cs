using System;
using UnityEngine;

namespace UdonSharp.Serialization
{
    internal sealed class ScriptableObjectDataSerializer<T> : Serializer<T> where T : ScriptableObject
    {
        public ScriptableObjectDataSerializer(TypeSerializationMetadata metadata) : base(metadata) { }

        protected override bool HandlesTypeSerialization(TypeSerializationMetadata metadata) =>
            ScriptableObjectDataLayout.IsDataOrArray(metadata.cSharpType);

        protected override Serializer MakeSerializer(TypeSerializationMetadata metadata)
        {
            Type type = metadata.cSharpType;
            Type serializer = type.IsArray ? typeof(ScriptableObjectDataArraySerializer<>) :
                typeof(ScriptableObjectDataSerializer<>);
            return (Serializer)Activator.CreateInstance(serializer.MakeGenericType(type.IsArray ? type.GetElementType() : type), metadata);
        }

        public override Type GetUdonStorageType() => typeof(object[]);

        // A snapshot cannot reconstruct an asset reference. Preserve the original
        // Inspector assignment when syncing the Udon heap back to its editor proxy.
        public override void Read(ref T targetObject, IValueStorage sourceObject) { }
        public override void ReadWeak(ref object targetObject, IValueStorage sourceObject) { }

        public override void Write(IValueStorage targetObject, in T sourceObject)
        {
            if (sourceObject == null)
            {
                if (!UsbSerializationContext.CollectDependencies) targetObject.Value = null;
                return;
            }
            if (sourceObject.GetType() != typeof(T))
                throw new NotSupportedException("Polymorphic ScriptableObject data references are not supported; use the exact asset type.");
            var fields = ScriptableObjectDataLayout.GetFields(typeof(T));
            var snapshot = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(sourceObject);
                ValidateAssetReferences(value);
                Serializer serializer = CreatePooled(fields[i].FieldType);
                IValueStorage storage = ValueStorageUtil.CreateStorage(serializer.GetUdonStorageType());
                serializer.WriteWeak(storage, value);
                snapshot[i] = storage.Value;
            }
            if (!UsbSerializationContext.CollectDependencies) targetObject.Value = snapshot;
        }

        private static void ValidateAssetReferences(object value)
        {
            if (value is ScriptableObject)
                throw new NotSupportedException("Nested ScriptableObject references are not supported in baked data.");
            if (value is Array array)
                foreach (object element in array) ValidateAssetReferences(element);
        }
    }

    internal sealed class ScriptableObjectDataArraySerializer<T> : Serializer<T[]> where T : ScriptableObject
    {
        public ScriptableObjectDataArraySerializer(TypeSerializationMetadata metadata) : base(metadata) { }
        protected override bool HandlesTypeSerialization(TypeSerializationMetadata metadata) => false;
        protected override Serializer MakeSerializer(TypeSerializationMetadata metadata) => throw new NotSupportedException();
        public override Type GetUdonStorageType() => typeof(object[]);
        public override void Read(ref T[] targetObject, IValueStorage sourceObject) { }
        public override void ReadWeak(ref object targetObject, IValueStorage sourceObject) { }
        public override void Write(IValueStorage targetObject, in T[] sourceObject)
        {
            if (sourceObject == null)
            {
                if (!UsbSerializationContext.CollectDependencies) targetObject.Value = null;
                return;
            }
            var snapshot = new object[sourceObject.Length];
            var serializer = CreatePooled<T>();
            for (int i = 0; i < sourceObject.Length; i++)
            {
                var storage = new SimpleValueStorage<object[]>();
                serializer.Write(storage, in sourceObject[i]);
                snapshot[i] = storage.Value;
            }
            if (!UsbSerializationContext.CollectDependencies) targetObject.Value = snapshot;
        }
    }
}
