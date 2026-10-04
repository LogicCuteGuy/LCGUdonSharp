using System;
using System.Collections.Generic;
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
            var snapshot = ScriptableObjectSnapshot.Build(sourceObject, typeof(T));
            if (!UsbSerializationContext.CollectDependencies) targetObject.Value = snapshot;
        }
    }

    internal static class ScriptableObjectSnapshot
    {
        public static object[] Build(object source, Type declaredType) =>
            Build(source, declaredType, new Dictionary<ScriptableObject, object[]>(), new HashSet<ScriptableObject>(),
                new Dictionary<ScriptableObject, int>(), 0, out _);

        private static object[] Build(object source, Type declaredType,
            Dictionary<ScriptableObject, object[]> snapshots, HashSet<ScriptableObject> active,
            Dictionary<ScriptableObject, int> heights, int depth, out int height)
        {
            height = 0;
            if (source == null || (source is ScriptableObject destroyed && destroyed == null)) return null;
            if (declaredType.IsArray)
            {
                var array = (Array)source;
                var result = new object[array.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = Build(array.GetValue(i), declaredType.GetElementType(), snapshots, active, heights, depth, out int childHeight);
                    height = Math.Max(height, childHeight);
                }
                return result;
            }
            var asset = (ScriptableObject)source;
            Type actualType = asset.GetType();
            if (!declaredType.IsAssignableFrom(actualType) || !ScriptableObjectDataLayout.IsDataType(actualType))
                throw new NotSupportedException($"ScriptableObject '{actualType}' is not compatible with '{declaredType}'.");
            if (active.Contains(asset))
                throw new NotSupportedException($"Cyclic ScriptableObject data reference at '{asset.name}' ({actualType.FullName}).");
            if (snapshots.TryGetValue(asset, out var existing))
            {
                height = heights[asset];
                if (depth + height > 128)
                    throw new NotSupportedException("ScriptableObject data nesting exceeds 128 assets.");
                return existing;
            }
            if (depth >= 128)
                throw new NotSupportedException("ScriptableObject data nesting exceeds 128 assets.");
            var fields = ScriptableObjectDataLayout.GetFields(actualType);
            var snapshot = new object[fields.Length + 1];
            snapshot[0] = ScriptableObjectDataLayout.GetTypeTag(actualType);
            // Share repeated edges, but reject cycles before passing data to the SDK serializer.
            snapshots.Add(asset, snapshot);
            active.Add(asset);
            height = 1;
            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(asset);
                if (ScriptableObjectDataLayout.IsDataOrArray(fields[i].FieldType))
                {
                    snapshot[i + 1] = Build(value, fields[i].FieldType, snapshots, active, heights, depth + 1, out int childHeight);
                    height = Math.Max(height, childHeight + 1);
                    continue;
                }
                ValidateAssetReferences(value);
                Serializer serializer = Serializer.CreatePooled(fields[i].FieldType);
                IValueStorage storage = ValueStorageUtil.CreateStorage(serializer.GetUdonStorageType());
                serializer.WriteWeak(storage, value);
                snapshot[i + 1] = storage.Value;
            }
            active.Remove(asset);
            heights.Add(asset, height);
            return snapshot;
        }

        private static void ValidateAssetReferences(object value)
        {
            if (value is ScriptableObject)
                throw new NotSupportedException("Custom data assets must use a typed ScriptableObject field, not UnityEngine.Object.");
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
            var snapshot = ScriptableObjectSnapshot.Build(sourceObject, typeof(T[]));
            if (!UsbSerializationContext.CollectDependencies) targetObject.Value = snapshot;
        }
    }
}
