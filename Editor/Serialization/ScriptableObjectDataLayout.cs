using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UdonSharp.Serialization
{
    // Compiler and serializer must use exactly the same ordering. Do not rely on
    // reflection/source declaration order, which can differ across assemblies.
    internal static class ScriptableObjectDataLayout
    {
        public static bool IsDataType(Type type) => type != null &&
            type.IsSubclassOf(typeof(ScriptableObject)) &&
            !Compiler.Udon.CompilerUdonInterface.IsExposedToUdon("Type_" +
                Compiler.Udon.CompilerUdonInterface.GetUdonTypeName(type));

        public static bool IsDataOrArray(Type type) => IsDataType(type) ||
            (type != null && type.IsArray && IsDataType(type.GetElementType()));

        public static FieldInfo[] GetFields(Type type)
        {
            if (!IsDataType(type))
                throw new NotSupportedException($"'{type}' is not a custom ScriptableObject data type.");
            if (type.IsGenericType)
                throw new NotSupportedException("Generic ScriptableObject data types are not supported.");

            var fields = new List<FieldInfo>();
            var hierarchy = new Stack<Type>();
            for (Type current = type; current != typeof(ScriptableObject); current = current.BaseType)
                hierarchy.Push(current);
            foreach (Type current in hierarchy)
                fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(IsSerialized)
                    .OrderBy(field => field.Name, StringComparer.Ordinal));

            foreach (FieldInfo field in fields)
                if (!IsSupportedFieldType(field.FieldType))
                    throw new NotSupportedException($"ScriptableObject data field '{field.DeclaringType.FullName}.{field.Name}' " +
                        $"has unsupported type '{field.FieldType}'. Use Udon-safe values, custom ScriptableObjects or one-dimensional arrays.");

            // Base fields must remain a prefix of every derived layout.
            return fields.ToArray();
        }

        public static string GetTypeToken(Type type) => "|" + type.Assembly.GetName().Name + ":" + type.FullName + "|";

        public static string GetTypeTag(Type type)
        {
            string tag = "";
            for (Type current = type; IsDataType(current); current = current.BaseType)
                tag += GetTypeToken(current);
            return tag;
        }

        private static bool IsSerialized(FieldInfo field) => !field.IsStatic && !field.IsInitOnly &&
            !field.IsNotSerialized && !field.IsDefined(typeof(SerializeReference), false) &&
            (field.IsPublic || field.IsDefined(typeof(SerializeField), false));

        private static bool IsSupportedFieldType(Type type)
        {
            if (IsDataType(type)) return true;
            if (type.IsArray)
                return type.GetArrayRank() == 1 && !type.GetElementType().IsArray &&
                    IsSupportedFieldType(type.GetElementType());
            if (type.IsEnum || type == typeof(string) || (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)))
                return true;
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Color32) ||
                type == typeof(Rect) || type == typeof(Bounds) || type == typeof(Matrix4x4) ||
                type == typeof(LayerMask) || type == typeof(VRC.SDKBase.VRCUrl))
                return true;
            // Asset references stay references. Custom ScriptableObjects are not
            // placed in the runtime heap, including via a UnityEngine.Object field.
            return typeof(UnityEngine.Object).IsAssignableFrom(type) &&
                !typeof(ScriptableObject).IsAssignableFrom(type) &&
                Compiler.Udon.CompilerUdonInterface.IsExposedToUdon("Type_" +
                    Compiler.Udon.CompilerUdonInterface.GetUdonTypeName(type));
        }
    }
}
