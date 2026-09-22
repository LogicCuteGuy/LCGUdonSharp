using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp.Compiler.Symbols;

namespace UdonSharp.Compiler.Binder
{
    internal static class ExternResolverExtensions
    {
        private static Dictionary<string, System.Reflection.Assembly> _assemblyNameLookup;
        private static bool _ranInit;
        private static readonly object _initLock = new object();

        private static void InitResolverExtensions()
        {
            if (_ranInit)
                return;

            lock (_initLock)
            {
                if (_ranInit)
                    return;

                _assemblyNameLookup = new Dictionary<string, System.Reflection.Assembly>();

                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!assembly.IsDynamic)
                    {
                        string assemblyFileName = Path.GetFileName(assembly.Location);

                        if (!string.IsNullOrWhiteSpace(assemblyFileName) && !_assemblyNameLookup.ContainsKey(assemblyFileName))
                            _assemblyNameLookup.Add(assemblyFileName, assembly);
                    }
                }

                _ranInit = true;
            }
        }

        public static System.Reflection.Assembly GetAssemblyFromMetadata(this ModuleMetadata metadata)
        {
            InitResolverExtensions();

            if (metadata == null)
                return null;

            return _assemblyNameLookup.TryGetValue(metadata.Name, out var asm) ? asm : null;
        }

        public static System.Reflection.Assembly GetExternAssembly(this INamedTypeSymbol typeSymbol)
        {
            // Metadata location determines reflection lookup independently of
            // whether the compiler exposes the type directly to Udon.
            if (typeSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
                return null;

            return typeSymbol.Locations.FirstOrDefault()?.MetadataModule?.GetMetadata().GetAssemblyFromMetadata();
        }

        public static bool IsExternType(this ITypeSymbol typeSymbol)
        {
            // This compiler-owned enum is in runtime metadata, but is not an
            // exposed Udon type. Use normal user-enum lowering to integer storage.
            if (typeSymbol.TypeKind == TypeKind.Enum &&
                typeSymbol.ToDisplayString() == typeof(UdonExceptionKind).FullName)
                return false;

            return typeSymbol.Locations.FirstOrDefault()?.IsInMetadata ?? false;
        }

        public static bool IsUdonSharpBehaviour(this INamedTypeSymbol typeSymbol)
        {
            while (typeSymbol != null)
            {
                if (!TypeSymbol.TryGetSystemType(typeSymbol, out var externType))
                    return false;
                
                if (externType == typeof(UdonSharpBehaviour))
                    return true;

                typeSymbol = typeSymbol.BaseType;
            }

            return false;
        }
    }
}
