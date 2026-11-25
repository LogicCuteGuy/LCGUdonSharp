using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UdonSharp.Serialization;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Data;
using Stock = UdonSharp.Compiler;

namespace UdonSharp.LCGCompiler
{
    /// <summary>
    /// Hooks the VRChat SDK's UdonSharp without modifying it. This file compiles into the stock
    /// UdonSharp.Editor assembly through UdonSharp.Editor.asmref, so it can reach its internals.
    /// Stock compile entry points are redirected to the LCG compiler in UdonSharp.LCGCompiler, and the
    /// LCG serializers are registered for List/Dictionary and ScriptableObject data fields.
    /// </summary>
    [InitializeOnLoad]
    internal static class LCGUdonSharpHook
    {
        private const string HarmonyId = "com.logiccuteguy.lcgudonsharp";

        static LCGUdonSharpHook()
        {
            var harmony = new Harmony(HarmonyId);

            using (new UdonSharpUtils.UdonSharpAssemblyLoadStripScope())
            {
                harmony.UnpatchAll(HarmonyId);

                // ponytail: Harmony cannot patch a call site Mono already inlined; these are patched on
                // load, before stock code calls them. Patch the callers too if a stock compile ever slips through.
                Prefix(harmony, typeof(Stock.UdonSharpCompilerV1), nameof(Stock.UdonSharpCompilerV1.Compile), nameof(Compile), typeof(Stock.UdonSharpCompileOptions));
                Prefix(harmony, typeof(Stock.UdonSharpCompilerV1), nameof(Stock.UdonSharpCompilerV1.CompileSync), nameof(CompileSync), typeof(Stock.UdonSharpCompileOptions));
                Prefix(harmony, typeof(Stock.UdonSharpCompilerV1), nameof(Stock.UdonSharpCompilerV1.WaitForCompile), nameof(WaitForCompile), Type.EmptyTypes);
                Prefix(harmony, typeof(UdonSharpUtils), nameof(UdonSharpUtils.UserTypeToUdonType), nameof(UserTypeToUdonType), typeof(Type));

                harmony.Patch(AccessTools.Method(typeof(Stock.CompilationContext), nameof(Stock.CompilationContext.ResetAssemblyCaches), Type.EmptyTypes),
                    postfix: new HarmonyMethod(typeof(LCGUdonSharpHook), nameof(ResetAssemblyCaches)));
            }

            RegisterSerializers();
        }

        // Exact parameter types: stock has overloads (e.g. a private 3-arg Compile) that make a name-only lookup ambiguous.
        private static void Prefix(Harmony harmony, Type type, string original, string replacement, params Type[] parameters)
        {
            MethodInfo target = AccessTools.Method(type, original, parameters);
            if (target == null)
                throw new MissingMethodException(type.FullName, original);

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(LCGUdonSharpHook), replacement));
        }

        // Harmony matches prefix parameters to the original by name.
        private static bool Compile(Stock.UdonSharpCompileOptions options)
        {
            UdonSharpCompilerV1.Compile(options);
            return false;
        }

        private static bool CompileSync(Stock.UdonSharpCompileOptions options)
        {
            UdonSharpCompilerV1.CompileSync(options);
            return false;
        }

        private static bool WaitForCompile()
        {
            UdonSharpCompilerV1.WaitForCompile();
            return false;
        }

        private static void ResetAssemblyCaches() => CompilationContext.ResetAssemblyCaches();

        // Udon storage types for the LCG data shapes; everything else falls through to stock.
        private static bool UserTypeToUdonType(Type type, ref Type __result)
        {
            if (type == null)
                return true;

            Type udonType = null;
            if (ScriptableObjectDataLayout.IsDataOrArray(type))
                udonType = typeof(object[]);
            else if (CollectionSerializer<object>.IsCollection(type))
                udonType = type.GetGenericTypeDefinition() == typeof(List<>) ? typeof(DataList) : typeof(DataDictionary);
            else if (type.IsArray && CollectionSerializer<object>.IsCollection(type.GetElementType()))
                udonType = UdonSharpUtils.UserTypeToUdonType(type.GetElementType()).MakeArrayType();
            else if (type.IsEnum && UdonSharpUtils.IsUserDefinedEnum(type))
                udonType = type.GetEnumUnderlyingType();

            if (udonType == null)
                return true;

            __result = UdonSharpUtils.RemapBaseType(udonType);
            return false;
        }

        private static void RegisterSerializers()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var serializers = (List<Serializer>)typeof(Serializer).GetField("_typeCheckSerializers", flags).GetValue(null);

            // Same priority order as LCGUdonSharp's earlier compiler fork: data snapshots first, collections after arrays.
            serializers.Insert(0, new ScriptableObjectDataSerializer<ScriptableObject>(null));
            serializers.Insert(serializers.FindIndex(serializer => serializer is ArraySerializer<object>) + 1,
                new CollectionSerializer<object>(null));

            var cache = (System.Collections.IDictionary)typeof(Serializer).GetField("_typeSerializerDictionary", flags).GetValue(null);
            cache.Clear();
        }
    }
}
