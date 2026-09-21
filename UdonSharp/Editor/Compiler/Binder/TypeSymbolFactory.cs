

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Core;

namespace UdonSharp.Compiler
{
    internal enum GenericUseSite
    {
        TypeReference,
        RuntimeType,
        ObjectCreation,
        BehaviourDeclaration,
    }

    internal static class GenericRestrictionPolicy
    {
        public static string GetViolation(ITypeSymbol type, GenericUseSite useSite)
        {
            string listViolation = GetListViolation(type);
            if (listViolation != null)
                return listViolation;

            if (type is IArrayTypeSymbol arrayType)
                return GetViolation(arrayType.ElementType, useSite);

            if (!(type is INamedTypeSymbol namedType))
                return null;

            if (namedType.IsUnboundGenericType)
                return $"Open generic type '{namedType}' is not supported by U#; supply concrete Udon-safe type arguments.";

            if (useSite == GenericUseSite.RuntimeType && ContainsOpenTypeParameter(namedType))
                return $"Open generic type '{namedType}' is not supported by U# at runtime; supply concrete Udon-safe type arguments.";

            if (useSite == GenericUseSite.BehaviourDeclaration && namedType.IsGenericType)
                return $"Generic U# behaviour '{namedType}' is not supported; behaviours must be non-generic concrete types.";

            // SDK extern members can be declared on generic metadata base classes
            // (for example ContactBaseProxy<TProxy, TContact>). Binding their symbols
            // does not allocate a U# heap object; extern exposure is checked separately.
            if (useSite == GenericUseSite.TypeReference && namedType.IsExternType() &&
                namedType.ContainingNamespace?.ToDisplayString().StartsWith("VRC.", StringComparison.Ordinal) == true)
                return null;

            INamedTypeSymbol genericHeapType = null;
            if (useSite == GenericUseSite.ObjectCreation || !IsCompilerOnlyTaskHandle(namedType))
                genericHeapType = FindGenericHeapType(namedType);

            if (genericHeapType != null)
            {
                string subject = useSite == GenericUseSite.ObjectCreation
                    ? "Instance generic heap objects"
                    : "Instance generic heap object types";
                return $"{subject} such as '{genericHeapType}' are not supported by U#; use arrays or static generic helpers instead.";
            }

            return null;
        }

        private static INamedTypeSymbol FindGenericHeapType(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                if (currentType.TypeKind == TypeKind.Class &&
                    currentType.IsGenericType &&
                    !currentType.IsStatic)
                    return currentType;
            }

            return null;
        }

        private static bool IsCompilerOnlyTaskHandle(INamedTypeSymbol type)
        {
            INamedTypeSymbol definition = type.OriginalDefinition;
            return definition.Arity == 1 &&
                   definition.Name == "Task" &&
                   definition.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
        }

        private static bool ContainsOpenTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
                return true;

            if (type is IArrayTypeSymbol arrayType)
                return ContainsOpenTypeParameter(arrayType.ElementType);

            return type is INamedTypeSymbol namedType &&
                   namedType.TypeArguments.Any(ContainsOpenTypeParameter);
        }

        private static string GetListViolation(ITypeSymbol type)
        {
            return GetListViolation(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        }

        private static string GetListViolation(ITypeSymbol type, HashSet<ITypeSymbol> visitedTypes)
        {
            if (type == null || !visitedTypes.Add(type))
                return null;

            if (type is IArrayTypeSymbol arrayType)
                return GetListViolation(arrayType.ElementType, visitedTypes);

            if (!(type is INamedTypeSymbol namedType))
                return null;

            INamedTypeSymbol definition = namedType.OriginalDefinition;
            if (definition.Arity == 1 &&
                definition.Name == "List" &&
                definition.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
                return "List<T> is not supported by U#; use an array or a VRCUrl-style Udon-safe container instead.";

            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                string violation = GetListViolation(typeArgument, visitedTypes);
                if (violation != null)
                    return violation;
            }

            string baseViolation = GetListViolation(namedType.BaseType, visitedTypes);
            if (baseViolation != null)
                return baseViolation;

            return null;
        }

        public static string GetUnsupportedInterfaceMemberViolation(ISymbol member)
        {
            switch (member)
            {
                case IEventSymbol eventSymbol:
                    return $"U# interface events are not supported: '{eventSymbol}'";
                case IFieldSymbol fieldSymbol:
                    return $"U# interface fields and constants are not supported: '{fieldSymbol}'";
                case INamedTypeSymbol nestedType:
                    return $"U# nested interface types are not supported: '{nestedType}'";
                case IPropertySymbol propertySymbol when propertySymbol.IsIndexer:
                    return $"U# interface indexers are not supported: '{propertySymbol}'";
                case IPropertySymbol propertySymbol when propertySymbol.IsStatic:
                    return $"U# interface static properties are not supported: '{propertySymbol}'";
                case IMethodSymbol methodSymbol when methodSymbol.IsStatic:
                    return $"U# interface static methods are not supported: '{methodSymbol}'";
                case IMethodSymbol methodSymbol when methodSymbol.IsGenericMethod:
                    return $"U# interface generic methods are not supported: '{methodSymbol}'";
                case IMethodSymbol methodSymbol when !methodSymbol.IsAbstract:
                    return $"U# default interface methods are not supported: '{methodSymbol}'";
                default:
                    return null;
            }
        }
    }
}

namespace UdonSharp.Compiler.Binder
{
    internal static class TypeSymbolFactory
    {
        public static TypeSymbol CreateSymbol(ITypeSymbol type, AbstractPhaseContext context)
        {
            string violation = GenericRestrictionPolicy.GetViolation(type, GenericUseSite.TypeReference);
            if (violation != null)
                throw new CompilerException(violation, context.CurrentNode?.GetLocation());

            switch (type)
            {
                case INamedTypeSymbol namedType when namedType.IsExternType():
                    return new ExternTypeSymbol(namedType, context);
                case INamedTypeSymbol namedType when namedType.TypeKind == TypeKind.Interface:
                    return new UdonSharpBehaviourTypeSymbol(namedType, context);
                case INamedTypeSymbol namedType when namedType.IsUdonSharpBehaviour():
                    return new UdonSharpBehaviourTypeSymbol(namedType, context);
                case INamedTypeSymbol namedType:
                    return new ImportedUdonSharpTypeSymbol(namedType, context);
                // This is just used to be able to query all symbols on system/unity types that use pointer types
                // Udon does not actually support pointer types
                case IPointerTypeSymbol pointerType when pointerType.PointedAtType.IsExternType():
                    return new ExternTypeSymbol((INamedTypeSymbol)pointerType.PointedAtType, context);
                case ITypeParameterSymbol typeParameter:
                    return new TypeParameterSymbol(typeParameter, context);
                case IArrayTypeSymbol arrayType:
                {
                    IArrayTypeSymbol currentArrayType = arrayType;
                    while (currentArrayType.ElementType is IArrayTypeSymbol)
                    {
                        currentArrayType = currentArrayType.ElementType as IArrayTypeSymbol;
                    }

                    INamedTypeSymbol rootType;

                    if (currentArrayType.ElementType is INamedTypeSymbol namedSymbol)
                    {
                        rootType = namedSymbol;
                
                        if (rootType.IsExternType())
                            return new ExternTypeSymbol(arrayType, context);

                        if (rootType.IsUdonSharpBehaviour())
                            return new UdonSharpBehaviourTypeSymbol(arrayType, context);

                        if (rootType.TypeKind == TypeKind.Interface)
                            return new UdonSharpBehaviourTypeSymbol(arrayType, context);
                    }
                    else if (currentArrayType.ElementType is ITypeParameterSymbol)
                    {
                        return new TypeParameterSymbol(arrayType, context);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    return new ImportedUdonSharpTypeSymbol(arrayType, context);
                }
                default:
                    throw new System.ArgumentException($"Could not construct type for type symbol {type}");
            }
        }
    }
}
