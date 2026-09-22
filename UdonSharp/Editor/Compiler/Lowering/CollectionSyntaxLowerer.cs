using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharp.Compiler.Lowering
{
    internal sealed class CollectionSyntaxLoweringDiagnostic
    {
        internal SyntaxNode Node { get; }
        internal string Message { get; }

        internal CollectionSyntaxLoweringDiagnostic(SyntaxNode node, string message)
        {
            Node = node;
            Message = message;
        }
    }

    internal sealed class CollectionSyntaxLoweringResult
    {
        internal SyntaxTree Tree { get; }
        internal ImmutableArray<CollectionSyntaxLoweringDiagnostic> Diagnostics { get; }
        internal bool Changed { get; }

        internal CollectionSyntaxLoweringResult(SyntaxTree tree,
            ImmutableArray<CollectionSyntaxLoweringDiagnostic> diagnostics, bool changed)
        {
            Tree = tree;
            Diagnostics = diagnostics;
            Changed = changed;
        }
    }

    /// <summary>
    /// Erases the supported concrete BCL collection surface into VRChat data containers before the
    /// ordinary U# binder sees it. Symbol checks are deliberately exact: user lookalikes, derived
    /// collections, collection interfaces, and arbitrary IEnumerable implementations are untouched.
    /// </summary>
    internal static class CollectionSyntaxLowerer
    {
        internal static CollectionSyntaxLoweringResult Rewrite(SyntaxTree tree,
            Func<ClassDeclarationSyntax, bool> shouldRewriteClass, SemanticModel semanticModel)
        {
            var rewriter = new Rewriter(shouldRewriteClass, semanticModel);
            SyntaxNode root = rewriter.Visit(tree.GetRoot());
            SyntaxTree rewrittenTree = rewriter.Changed
                ? tree.WithRootAndOptions(root, tree.Options)
                : tree;
            return new CollectionSyntaxLoweringResult(rewrittenTree,
                rewriter.Diagnostics.ToImmutableArray(), rewriter.Changed);
        }

        private enum CollectionKind
        {
            List,
            Dictionary,
        }

        private sealed class CollectionDescriptor
        {
            internal CollectionKind Kind;
            internal INamedTypeSymbol SourceType;
            internal ITypeSymbol ElementType;
            internal ITypeSymbol KeyType;
            internal ITypeSymbol ValueType;
            internal int Id;

            internal string Prefix => Kind == CollectionKind.List
                ? $"__lcg_list_{Id}_"
                : $"__lcg_dictionary_{Id}_";
        }

        private sealed class JsonDescriptor
        {
            internal ITypeSymbol Type;
            internal int Id;
            internal string Prefix => $"__lcg_json_{Id}_";
        }

        private sealed class SyncedCollectionField
        {
            internal IFieldSymbol Symbol;
            internal JsonDescriptor Json;
            internal string BackingName => "__lcg_sync_" + Symbol.Name;
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private const string DataListType = "global::VRC.SDK3.Data.DataList";
            private const string DataDictionaryType = "global::VRC.SDK3.Data.DataDictionary";
            private const string DataTokenType = "global::VRC.SDK3.Data.DataToken";

            private readonly Func<ClassDeclarationSyntax, bool> _shouldRewriteClass;
            private readonly SemanticModel _semanticModel;
            private readonly Stack<Dictionary<ITypeSymbol, CollectionDescriptor>> _collectionScopes =
                new Stack<Dictionary<ITypeSymbol, CollectionDescriptor>>();
            private readonly Stack<Dictionary<ITypeSymbol, JsonDescriptor>> _jsonScopes =
                new Stack<Dictionary<ITypeSymbol, JsonDescriptor>>();
            private readonly Stack<List<SyncedCollectionField>> _syncedFieldScopes =
                new Stack<List<SyncedCollectionField>>();
            private bool _inTargetClass;
            private int _temporaryId;

            internal bool Changed { get; private set; }
            internal List<CollectionSyntaxLoweringDiagnostic> Diagnostics { get; } =
                new List<CollectionSyntaxLoweringDiagnostic>();

            internal Rewriter(Func<ClassDeclarationSyntax, bool> shouldRewriteClass,
                SemanticModel semanticModel)
            {
                _shouldRewriteClass = shouldRewriteClass;
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                bool previousTarget = _inTargetClass;
                _inTargetClass = _shouldRewriteClass == null || _shouldRewriteClass(node);
                if (!_inTargetClass)
                {
                    ClassDeclarationSyntax untouched = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                    _inTargetClass = previousTarget;
                    return untouched;
                }

                var descriptors = new Dictionary<ITypeSymbol, CollectionDescriptor>(SymbolEqualityComparer.Default);
                var jsonDescriptors = new Dictionary<ITypeSymbol, JsonDescriptor>(SymbolEqualityComparer.Default);
                var unsupportedShapes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                foreach (SyntaxNode candidate in node.DescendantNodesAndSelf())
                {
                    ITypeSymbol type = null;
                    switch (candidate)
                    {
                        case TypeSyntax typeSyntax:
                            type = _semanticModel.GetTypeInfo(typeSyntax).Type;
                            break;
                        case ExpressionSyntax expression:
                            type = _semanticModel.GetTypeInfo(expression).Type;
                            break;
                    }

                    RegisterCollectionTypes(type, descriptors, candidate);
                    ReportUnsupportedCollectionShape(type, candidate, unsupportedShapes);

                    if (candidate is InvocationExpressionSyntax invocation &&
                        _semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                        IsJsonSerializerMethod(method) && method.TypeArguments.Length == 1)
                    {
                        ITypeSymbol jsonType = method.TypeArguments[0];
                        RegisterCollectionTypes(jsonType, descriptors);
                        if (!jsonDescriptors.ContainsKey(jsonType))
                        {
                            jsonDescriptors.Add(jsonType, new JsonDescriptor
                            {
                                Type = jsonType,
                                Id = jsonDescriptors.Count,
                            });
                        }
                    }
                }

                ValidateCollectionFields(node);
                List<SyncedCollectionField> syncedFields = CollectSyncedCollectionFields(
                    node, descriptors, jsonDescriptors);

                _collectionScopes.Push(descriptors);
                _jsonScopes.Push(jsonDescriptors);
                _syncedFieldScopes.Push(syncedFields);
                ClassDeclarationSyntax visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                if (syncedFields.Count > 0)
                    visited = ComposeSyncedCollectionCallbacks(visited, syncedFields);
                if (descriptors.Count > 0 || jsonDescriptors.Count > 0)
                {
                    var generatedMembers = new List<MemberDeclarationSyntax>();
                    foreach (CollectionDescriptor descriptor in descriptors.Values.OrderBy(value => value.Id))
                        generatedMembers.AddRange(CreateHelpers(descriptor));
                    foreach (JsonDescriptor descriptor in jsonDescriptors.Values.OrderBy(value => value.Id))
                        generatedMembers.AddRange(CreateJsonHelpers(descriptor));
                    visited = visited.AddMembers(generatedMembers.ToArray());
                    Changed = true;
                }
                _syncedFieldScopes.Pop();
                _jsonScopes.Pop();
                _collectionScopes.Pop();
                _inTargetClass = previousTarget;
                return visited;
            }

            private List<SyncedCollectionField> CollectSyncedCollectionFields(ClassDeclarationSyntax node,
                IDictionary<ITypeSymbol, CollectionDescriptor> collections,
                IDictionary<ITypeSymbol, JsonDescriptor> jsonDescriptors)
            {
                var result = new List<SyncedCollectionField>();
                foreach (FieldDeclarationSyntax declaration in node.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                    {
                        if (!(_semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field) ||
                            !HasAttribute(field, "UdonSharp.UdonSyncedAttribute") ||
                            !(field.Type is INamedTypeSymbol named) ||
                            !TryGetCollectionKind(named, out _))
                            continue;

                        RegisterCollectionTypes(field.Type, collections);
                        if (!jsonDescriptors.TryGetValue(field.Type, out JsonDescriptor json))
                        {
                            json = new JsonDescriptor { Type = field.Type, Id = jsonDescriptors.Count };
                            jsonDescriptors.Add(field.Type, json);
                        }

                        bool valid = true;
                        if (!HasManualSyncMode(field.ContainingType))
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(variable,
                                "[UdonSynced] collection fields require [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]."));
                            valid = false;
                        }
                        if (HasAttribute(field, "UdonSharp.FieldChangeCallbackAttribute"))
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(variable,
                                "FieldChangeCallback is not supported on synchronized collection fields."));
                            valid = false;
                        }
                        if (!IsStaticallyJsonSafe(field.Type))
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(variable,
                                "This synchronized collection contains a type that cannot be represented by VRCJson."));
                            valid = false;
                        }

                        if (valid)
                            result.Add(new SyncedCollectionField { Symbol = field, Json = json });
                    }
                }
                return result;
            }

            private void ValidateCollectionFields(ClassDeclarationSyntax node)
            {
                foreach (FieldDeclarationSyntax declaration in node.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                    {
                        if (!(_semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field) ||
                            !(field.Type is INamedTypeSymbol named) || !TryGetCollectionKind(named, out _))
                            continue;

                        bool nonSerialized = HasAttribute(field, "System.NonSerializedAttribute");
                        bool serializeField = HasAttribute(field, "UnityEngine.SerializeField");
                        if (serializeField || field.DeclaredAccessibility == Accessibility.Public && !nonSerialized)
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(variable,
                                "Inspector serialization is not supported for List<T> or Dictionary<TKey,TValue> fields; make the field private or add [NonSerialized]."));
                        }
                        if (named.NullableAnnotation == NullableAnnotation.Annotated)
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(variable,
                                "Nullable collection annotations are not supported by the Udon collection compatibility layer."));
                        }
                    }
                }
            }

            private static bool HasAttribute(ISymbol symbol, string fullName)
            {
                return symbol.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == fullName);
            }

            private static bool HasManualSyncMode(INamedTypeSymbol type)
            {
                for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
                {
                    AttributeData attribute = current.GetAttributes().FirstOrDefault(candidate =>
                        candidate.AttributeClass?.ToDisplayString() == "UdonSharp.UdonBehaviourSyncModeAttribute");
                    if (attribute != null && attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value != null)
                        return Convert.ToInt32(attribute.ConstructorArguments[0].Value) == 4;
                }
                return false;
            }

            private bool IsStaticallyJsonSafe(ITypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Enum || IsNativeTokenType(type))
                    return type.SpecialType != SpecialType.System_Char &&
                           type.SpecialType != SpecialType.System_Decimal;
                if (type is IArrayTypeSymbol array)
                    return IsStaticallyJsonSafe(array.ElementType);
                if (type is INamedTypeSymbol named && TryGetCollectionKind(named, out CollectionKind kind))
                    return kind == CollectionKind.List
                        ? IsStaticallyJsonSafe(named.TypeArguments[0])
                        : IsStaticallyJsonSafe(named.TypeArguments[0]) &&
                          IsStaticallyJsonSafe(named.TypeArguments[1]);
                return false;
            }

            private void RegisterCollectionTypes(ITypeSymbol type,
                IDictionary<ITypeSymbol, CollectionDescriptor> descriptors,
                SyntaxNode diagnosticNode = null)
            {
                if (type == null)
                    return;
                if (type is IArrayTypeSymbol array)
                {
                    RegisterCollectionTypes(array.ElementType, descriptors, diagnosticNode);
                    return;
                }
                if (!(type is INamedTypeSymbol named))
                    return;

                if (TryGetCollectionKind(named, out CollectionKind kind) && !descriptors.ContainsKey(named))
                {
                    var descriptor = new CollectionDescriptor
                    {
                        Kind = kind,
                        SourceType = named,
                        Id = descriptors.Count,
                    };
                    if (kind == CollectionKind.List)
                        descriptor.ElementType = named.TypeArguments[0];
                    else
                    {
                        descriptor.KeyType = named.TypeArguments[0];
                        descriptor.ValueType = named.TypeArguments[1];
                    }
                    descriptors.Add(named, descriptor);
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        if (argument.SpecialType == SpecialType.System_Char ||
                            argument.SpecialType == SpecialType.System_Decimal)
                        {
                            Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(diagnosticNode,
                                $"Collection element type '{argument.ToDisplayString()}' is not supported by DataToken."));
                        }
                    }
                }

                foreach (ITypeSymbol argument in named.TypeArguments)
                    RegisterCollectionTypes(argument, descriptors, diagnosticNode);
            }

            private void ReportUnsupportedCollectionShape(ITypeSymbol type, SyntaxNode node,
                ISet<ITypeSymbol> reported)
            {
                if (!(type is INamedTypeSymbol named) || reported.Contains(named) ||
                    TryGetCollectionKind(named, out _))
                    return;

                INamedTypeSymbol definition = named.OriginalDefinition;
                string ns = definition.ContainingNamespace?.ToDisplayString();
                bool collectionInterface = ns == "System.Collections.Generic" &&
                    ((definition.Name == "IList" && definition.Arity == 1) ||
                     (definition.Name == "IDictionary" && definition.Arity == 2));
                bool derived = false;
                for (INamedTypeSymbol current = named.BaseType; current != null; current = current.BaseType)
                {
                    if (TryGetCollectionKind(current, out _))
                    {
                        derived = true;
                        break;
                    }
                }
                if (!collectionInterface && !derived)
                    return;

                reported.Add(named);
                Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                    $"Collection shape '{named.ToDisplayString()}' is not lowered. Use the exact List<T> or Dictionary<TKey,TValue> type."));
            }

            private static bool TryGetCollectionKind(INamedTypeSymbol type, out CollectionKind kind)
            {
                INamedTypeSymbol definition = type.OriginalDefinition;
                string ns = definition.ContainingNamespace?.ToDisplayString();
                if (ns == "System.Collections.Generic" && definition.Name == "List" && definition.Arity == 1)
                {
                    kind = CollectionKind.List;
                    return true;
                }
                if (ns == "System.Collections.Generic" && definition.Name == "Dictionary" && definition.Arity == 2)
                {
                    kind = CollectionKind.Dictionary;
                    return true;
                }
                kind = default;
                return false;
            }

            private static bool IsJsonSerializerMethod(IMethodSymbol method)
            {
                return method?.ContainingType?.ToDisplayString() == "System.Text.Json.JsonSerializer" &&
                       (method.Name == "Serialize" || method.Name == "Deserialize" ||
                        method.Name == "TrySerialize" || method.Name == "TryDeserialize");
            }

            private static bool IsJsonOptionsType(ITypeSymbol type)
            {
                return type?.ToDisplayString() == "System.Text.Json.JsonSerializerOptions";
            }

            private bool TryGetDescriptor(ITypeSymbol type, out CollectionDescriptor descriptor)
            {
                descriptor = null;
                if (!_inTargetClass || _collectionScopes.Count == 0 || type == null)
                    return false;
                return _collectionScopes.Peek().TryGetValue(type, out descriptor);
            }

            private bool TryGetExpressionDescriptor(ExpressionSyntax expression,
                out CollectionDescriptor descriptor)
            {
                return TryGetDescriptor(_semanticModel.GetTypeInfo(expression).Type, out descriptor);
            }

            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    TryGetDescriptor(_semanticModel.GetTypeInfo(node).Type, out CollectionDescriptor descriptor))
                {
                    Changed = true;
                    return SyntaxFactory.ParseTypeName(descriptor.Kind == CollectionKind.List
                            ? DataListType
                            : DataDictionaryType)
                        .WithTriviaFrom(node);
                }
                return base.VisitGenericName(node);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    IsJsonOptionsType(_semanticModel.GetTypeInfo(node).Type) &&
                    IsTypePosition(node))
                {
                    Changed = true;
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))
                        .WithTriviaFrom(node);
                }
                return base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    IsJsonOptionsType(_semanticModel.GetTypeInfo(node).Type) && IsTypePosition(node))
                {
                    Changed = true;
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))
                        .WithTriviaFrom(node);
                }
                return base.VisitQualifiedName(node);
            }

            public override SyntaxNode VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    IsJsonOptionsType(_semanticModel.GetTypeInfo(node).Type) && IsTypePosition(node))
                {
                    Changed = true;
                    return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))
                        .WithTriviaFrom(node);
                }
                return base.VisitAliasQualifiedName(node);
            }

            private static bool IsTypePosition(TypeSyntax node)
            {
                return node.Parent is VariableDeclarationSyntax || node.Parent is ParameterSyntax ||
                       node.Parent is CastExpressionSyntax || node.Parent is DefaultExpressionSyntax ||
                       node.Parent is PropertyDeclarationSyntax || node.Parent is MethodDeclarationSyntax;
            }

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                FieldDeclarationSyntax visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
                if (!_inTargetClass || _syncedFieldScopes.Count == 0)
                    return visited;

                var syncedNames = new HashSet<string>(_syncedFieldScopes.Peek()
                    .Select(field => field.Symbol.Name), StringComparer.Ordinal);
                if (!node.Declaration.Variables.Any(variable => syncedNames.Contains(variable.Identifier.ValueText)))
                    return visited;

                var lists = new List<AttributeListSyntax>();
                foreach (AttributeListSyntax list in visited.AttributeLists)
                {
                    SeparatedSyntaxList<AttributeSyntax> attributes = list.Attributes;
                    attributes = SyntaxFactory.SeparatedList(attributes.Where(attribute =>
                    {
                        ISymbol symbol = attribute.SyntaxTree == _semanticModel.SyntaxTree
                            ? _semanticModel.GetSymbolInfo(attribute).Symbol
                            : null;
                        return symbol?.ContainingType?.ToDisplayString() != "UdonSharp.UdonSyncedAttribute" &&
                               !attribute.Name.ToString().EndsWith("UdonSynced", StringComparison.Ordinal) &&
                               !attribute.Name.ToString().EndsWith("UdonSyncedAttribute", StringComparison.Ordinal);
                    }));
                    if (attributes.Count > 0)
                        lists.Add(list.WithAttributes(attributes));
                }
                Changed = true;
                return visited.WithAttributeLists(SyntaxFactory.List(lists));
            }

            private ClassDeclarationSyntax ComposeSyncedCollectionCallbacks(ClassDeclarationSyntax node,
                IList<SyncedCollectionField> fields)
            {
                var members = node.Members.ToList();
                foreach (SyncedCollectionField field in fields)
                {
                    string backing = field.BackingName;
                    MemberDeclarationSyntax backingField = SyntaxFactory.ParseMemberDeclaration(
                        $"[global::UdonSharp.UdonSynced] private string {backing} = \"\";");
                    members.Add(backingField);
                }

                var preStatements = new List<StatementSyntax>();
                var deserializeStatements = new List<StatementSyntax>();
                foreach (SyncedCollectionField field in fields)
                {
                    string safeName = field.Symbol.Name.Replace("@", string.Empty);
                    string jsonName = "__lcg_json_value_" + safeName;
                    string errorName = "__lcg_json_error_" + safeName;
                    string decodedName = "__lcg_json_decoded_" + safeName;
                    preStatements.Add(SyntaxFactory.ParseStatement(
                        $"{{ string {jsonName}; string {errorName}; if ({field.Json.Prefix}trySerialize({field.Symbol.Name}, out {jsonName}, out {errorName})) {field.BackingName} = {jsonName}; else global::UnityEngine.Debug.LogError(\"Failed to serialize synchronized collection '{safeName}': \" + {errorName}); }}"));
                    deserializeStatements.Add(SyntaxFactory.ParseStatement(
                        $"if (global::System.String.IsNullOrEmpty({field.BackingName})) {field.Symbol.Name} = null; else {{ {LoweredTypeSource(field.Symbol.Type)} {decodedName}; string {errorName}; if ({field.Json.Prefix}tryDeserialize({field.BackingName}, out {decodedName}, out {errorName})) {field.Symbol.Name} = {decodedName}; else global::UnityEngine.Debug.LogError(\"Failed to deserialize synchronized collection '{safeName}': \" + {errorName}); }}"));
                }

                ComposeCallback(members, "OnPreSerialization", preStatements, append: true);
                ComposeCallback(members, "OnDeserialization", deserializeStatements, append: false);
                Changed = true;
                return node.WithMembers(SyntaxFactory.List(members));
            }

            private static void ComposeCallback(IList<MemberDeclarationSyntax> members, string methodName,
                IList<StatementSyntax> injected, bool append)
            {
                int index = -1;
                MethodDeclarationSyntax method = null;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i] is MethodDeclarationSyntax candidate &&
                        candidate.Identifier.ValueText == methodName &&
                        candidate.ParameterList.Parameters.Count == 0)
                    {
                        index = i;
                        method = candidate;
                        break;
                    }
                }

                if (method == null)
                {
                    members.Add(SyntaxFactory.MethodDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), methodName)
                        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                            SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
                        .WithBody(SyntaxFactory.Block(injected)));
                    return;
                }

                BlockSyntax body = method.Body;
                if (body == null && method.ExpressionBody != null)
                    body = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression));
                body = body ?? SyntaxFactory.Block();
                SyntaxList<StatementSyntax> statements = append
                    ? body.Statements.AddRange(injected)
                    : SyntaxFactory.List(injected.Concat(body.Statements));
                members[index] = method.WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(body.WithStatements(statements));
            }

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    IsJsonOptionsType(_semanticModel.GetTypeInfo(node).Type))
                {
                    Changed = true;
                    ExpressionSyntax indented = SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
                    if (node.Initializer != null)
                    {
                        foreach (ExpressionSyntax expression in node.Initializer.Expressions)
                        {
                            if (expression is AssignmentExpressionSyntax assignment &&
                                assignment.Left.ToString().EndsWith("WriteIndented", StringComparison.Ordinal))
                                indented = (ExpressionSyntax)Visit(assignment.Right);
                            else
                                Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(expression,
                                    "Only JsonSerializerOptions.WriteIndented is supported in Udon."));
                        }
                    }
                    return indented.WithTriviaFrom(node);
                }

                if (!_inTargetClass || node.SyntaxTree != _semanticModel.SyntaxTree ||
                    !TryGetDescriptor(_semanticModel.GetTypeInfo(node).Type, out CollectionDescriptor descriptor))
                    return base.VisitObjectCreationExpression(node);

                Changed = true;
                SeparatedSyntaxList<ArgumentSyntax> arguments = node.ArgumentList?.Arguments ?? default;
                if (node.Initializer != null)
                    return RewriteInitializer(node, descriptor);

                if (arguments.Count == 0)
                    return SyntaxFactory.ParseExpression($"new {(descriptor.Kind == CollectionKind.List ? DataListType : DataDictionaryType)}()")
                        .WithTriviaFrom(node);

                if (arguments.Count == 1)
                {
                    ExpressionSyntax argument = arguments[0].Expression;
                    ITypeSymbol argumentType = _semanticModel.GetTypeInfo(argument).Type;
                    ExpressionSyntax visitedArgument = (ExpressionSyntax)Visit(argument);
                    if (argumentType?.SpecialType == SpecialType.System_Int32)
                        return Call(descriptor.Prefix + "createCapacity", visitedArgument).WithTriviaFrom(node);
                    if (SymbolEqualityComparer.Default.Equals(argumentType, descriptor.SourceType))
                        return Call(descriptor.Prefix + "clone", visitedArgument).WithTriviaFrom(node);
                    if (descriptor.Kind == CollectionKind.List && argumentType is IArrayTypeSymbol array &&
                        SymbolEqualityComparer.Default.Equals(array.ElementType, descriptor.ElementType))
                        return Call(descriptor.Prefix + "fromArray", visitedArgument).WithTriviaFrom(node);
                    if (descriptor.Kind == CollectionKind.Dictionary &&
                        SymbolEqualityComparer.Default.Equals(argumentType, descriptor.SourceType))
                        return Call(descriptor.Prefix + "clone", visitedArgument).WithTriviaFrom(node);
                }

                Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                    $"This {descriptor.SourceType.OriginalDefinition.Name} constructor is not supported in Udon; use the default, capacity, array/list copy, or dictionary copy constructor."));
                return SyntaxFactory.ParseExpression($"new {(descriptor.Kind == CollectionKind.List ? DataListType : DataDictionaryType)}()")
                    .WithTriviaFrom(node);
            }

            private ExpressionSyntax RewriteInitializer(ObjectCreationExpressionSyntax node,
                CollectionDescriptor descriptor)
            {
                if ((node.ArgumentList?.Arguments.Count ?? 0) != 0)
                {
                    Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                        "Collection initializers with constructor arguments are not supported in Udon."));
                    return SyntaxFactory.ParseExpression($"new {(descriptor.Kind == CollectionKind.List ? DataListType : DataDictionaryType)}()");
                }

                if (descriptor.Kind == CollectionKind.List)
                {
                    var packed = node.Initializer.Expressions
                        .Select(expression => Pack((ExpressionSyntax)Visit(expression), descriptor.ElementType));
                    string array = $"new {DataTokenType}[] {{ {string.Join(", ", packed.Select(value => value.ToString()))} }}";
                    return Call(descriptor.Prefix + "fromTokens", SyntaxFactory.ParseExpression(array))
                        .WithTriviaFrom(node);
                }

                var keys = new List<string>();
                var values = new List<string>();
                var overwrite = new List<string>();
                foreach (ExpressionSyntax expression in node.Initializer.Expressions)
                {
                    if (expression is InitializerExpressionSyntax pair && pair.Expressions.Count == 2)
                    {
                        keys.Add(Pack((ExpressionSyntax)Visit(pair.Expressions[0]), descriptor.KeyType).ToString());
                        values.Add(Pack((ExpressionSyntax)Visit(pair.Expressions[1]), descriptor.ValueType).ToString());
                        overwrite.Add("false");
                        continue;
                    }
                    if (expression is AssignmentExpressionSyntax assignment &&
                        assignment.Left is ImplicitElementAccessSyntax indexer &&
                        indexer.ArgumentList.Arguments.Count == 1)
                    {
                        keys.Add(Pack((ExpressionSyntax)Visit(indexer.ArgumentList.Arguments[0].Expression),
                            descriptor.KeyType).ToString());
                        values.Add(Pack((ExpressionSyntax)Visit(assignment.Right), descriptor.ValueType).ToString());
                        overwrite.Add("true");
                        continue;
                    }

                    Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(expression,
                        "Dictionary initializers must contain { key, value } entries or [key] = value entries."));
                }
                string keyArray = $"new {DataTokenType}[] {{ {string.Join(", ", keys)} }}";
                string valueArray = $"new {DataTokenType}[] {{ {string.Join(", ", values)} }}";
                string overwriteArray = $"new bool[] {{ {string.Join(", ", overwrite)} }}";
                return Call(descriptor.Prefix + "fromTokens", SyntaxFactory.ParseExpression(keyArray),
                        SyntaxFactory.ParseExpression(valueArray), SyntaxFactory.ParseExpression(overwriteArray))
                    .WithTriviaFrom(node);
            }

            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                if (_inTargetClass && node.Left is MemberAccessExpressionSyntax jsonOptionProperty &&
                    _semanticModel.GetSymbolInfo(jsonOptionProperty).Symbol is IPropertySymbol optionProperty &&
                    optionProperty.Name == "WriteIndented" && IsJsonOptionsType(optionProperty.ContainingType))
                {
                    Changed = true;
                    return SyntaxFactory.AssignmentExpression(node.Kind(),
                            (ExpressionSyntax)Visit(jsonOptionProperty.Expression),
                            (ExpressionSyntax)Visit(node.Right))
                        .WithTriviaFrom(node);
                }

                if (_inTargetClass && node.Left is ElementAccessExpressionSyntax element &&
                    TryGetExpressionDescriptor(element.Expression, out CollectionDescriptor descriptor))
                {
                    if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                            "Compound assignment to a lowered collection indexer is not supported; assign the computed value explicitly."));
                        return base.VisitAssignmentExpression(node);
                    }

                    Changed = true;
                    ExpressionSyntax receiver = (ExpressionSyntax)Visit(element.Expression);
                    ExpressionSyntax index = (ExpressionSyntax)Visit(element.ArgumentList.Arguments[0].Expression);
                    ExpressionSyntax value = (ExpressionSyntax)Visit(node.Right);
                    return Call(descriptor.Prefix + "set", receiver, index, value).WithTriviaFrom(node);
                }
                return base.VisitAssignmentExpression(node);
            }

            public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
            {
                if (_inTargetClass && TryGetExpressionDescriptor(node.Expression,
                        out CollectionDescriptor descriptor) &&
                    !(node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node))
                {
                    Changed = true;
                    return Call(descriptor.Prefix + "get", (ExpressionSyntax)Visit(node.Expression),
                            (ExpressionSyntax)Visit(node.ArgumentList.Arguments[0].Expression))
                        .WithTriviaFrom(node);
                }
                return base.VisitElementAccessExpression(node);
            }

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    _semanticModel.GetSymbolInfo(node).Symbol is IPropertySymbol jsonOptionProperty &&
                    jsonOptionProperty.Name == "WriteIndented" &&
                    IsJsonOptionsType(jsonOptionProperty.ContainingType))
                {
                    Changed = true;
                    return Visit(node.Expression).WithTriviaFrom(node);
                }

                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    _semanticModel.GetSymbolInfo(node).Symbol is IPropertySymbol property &&
                    TryGetDescriptor(property.ContainingType, out CollectionDescriptor descriptor) &&
                    descriptor.Kind == CollectionKind.Dictionary &&
                    (property.Name == "Keys" || property.Name == "Values"))
                {
                    Changed = true;
                    string method = property.Name == "Keys" ? "GetKeys" : "GetValues";
                    return SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                (ExpressionSyntax)Visit(node.Expression), SyntaxFactory.IdentifierName(method)))
                        .WithTriviaFrom(node);
                }
                return base.VisitMemberAccessExpression(node);
            }

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol jsonMethod &&
                    IsJsonSerializerMethod(jsonMethod) && jsonMethod.TypeArguments.Length == 1 &&
                    _jsonScopes.Count > 0 &&
                    _jsonScopes.Peek().TryGetValue(jsonMethod.TypeArguments[0], out JsonDescriptor jsonDescriptor))
                {
                    Changed = true;
                    ArgumentSyntax[] arguments = node.ArgumentList.Arguments
                        .Select(argument => argument.WithExpression((ExpressionSyntax)Visit(argument.Expression)))
                        .ToArray();
                    string jsonHelper;
                    switch (jsonMethod.Name)
                    {
                        case "Serialize":
                            jsonHelper = arguments.Length == 1 ? "serialize" : "serializeOptions";
                            break;
                        case "Deserialize":
                            jsonHelper = "deserialize";
                            break;
                        case "TrySerialize":
                            jsonHelper = "trySerialize";
                            break;
                        case "TryDeserialize":
                            jsonHelper = "tryDeserialize";
                            break;
                        default:
                            return base.VisitInvocationExpression(node);
                    }

                    return SyntaxFactory.InvocationExpression(
                            SyntaxFactory.IdentifierName(jsonDescriptor.Prefix + jsonHelper),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
                        .WithTriviaFrom(node);
                }

                if (!_inTargetClass || node.SyntaxTree != _semanticModel.SyntaxTree ||
                    !(_semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method) ||
                    !TryGetDescriptor(method.ContainingType, out CollectionDescriptor descriptor) ||
                    !(node.Expression is MemberAccessExpressionSyntax access))
                    return base.VisitInvocationExpression(node);

                ExpressionSyntax receiver = (ExpressionSyntax)Visit(access.Expression);
                var visitedArguments = node.ArgumentList.Arguments
                    .Select(argument => argument.WithExpression((ExpressionSyntax)Visit(argument.Expression)))
                    .ToArray();
                string helper = null;

                if (descriptor.Kind == CollectionKind.List)
                {
                    switch (method.Name)
                    {
                        case "Add": helper = "add"; break;
                        case "Insert": helper = "insert"; break;
                        case "Remove": helper = "remove"; break;
                        case "RemoveAt": helper = "removeAt"; break;
                        case "Clear": helper = "clear"; break;
                        case "Contains": helper = "contains"; break;
                        case "IndexOf": helper = "indexOf"; break;
                        case "ToArray": helper = "toArray"; break;
                        case "AddRange":
                        {
                            ITypeSymbol argumentType = _semanticModel.GetTypeInfo(
                                node.ArgumentList.Arguments[0].Expression).Type;
                            if (argumentType is IArrayTypeSymbol)
                                helper = "addRangeArray";
                            else if (SymbolEqualityComparer.Default.Equals(argumentType, descriptor.SourceType))
                                helper = "addRangeList";
                            else
                                Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                                    "List<T>.AddRange supports arrays and exact List<T> values in Udon."));
                            break;
                        }
                    }
                }
                else
                {
                    switch (method.Name)
                    {
                        case "Add": helper = "add"; break;
                        case "Remove": helper = visitedArguments.Length == 2 ? "removeValue" : "remove"; break;
                        case "Clear": helper = "clear"; break;
                        case "ContainsKey": helper = "containsKey"; break;
                        case "ContainsValue": helper = "containsValue"; break;
                        case "TryGetValue": helper = "tryGetValue"; break;
                    }
                }

                if (helper == null)
                {
                    Diagnostics.Add(new CollectionSyntaxLoweringDiagnostic(node,
                        $"Collection member '{method.Name}' is not supported by the Udon collection compatibility layer."));
                    return base.VisitInvocationExpression(node);
                }

                Changed = true;
                var callArguments = new List<ArgumentSyntax> { SyntaxFactory.Argument(receiver) };
                callArguments.AddRange(visitedArguments);
                return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(descriptor.Prefix + helper),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(callArguments)))
                    .WithTriviaFrom(node);
            }

            public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            {
                if (!_inTargetClass)
                    return base.VisitForEachStatement(node);

                CollectionDescriptor descriptor = null;
                ITypeSymbol itemType = null;
                bool dictionaryPairs = false;
                ExpressionSyntax sourceExpression = node.Expression;

                if (TryGetExpressionDescriptor(node.Expression, out descriptor))
                {
                    if (descriptor.Kind == CollectionKind.List)
                        itemType = descriptor.ElementType;
                    else
                        dictionaryPairs = true;
                }
                else if (node.Expression is MemberAccessExpressionSyntax member &&
                         TryGetExpressionDescriptor(member.Expression, out descriptor) &&
                         descriptor.Kind == CollectionKind.Dictionary &&
                         (member.Name.Identifier.ValueText == "Keys" || member.Name.Identifier.ValueText == "Values"))
                {
                    itemType = member.Name.Identifier.ValueText == "Keys"
                        ? descriptor.KeyType
                        : descriptor.ValueType;
                    string getter = member.Name.Identifier.ValueText == "Keys" ? "GetKeys" : "GetValues";
                    sourceExpression = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            (ExpressionSyntax)Visit(member.Expression), SyntaxFactory.IdentifierName(getter)));
                }
                else
                {
                    return base.VisitForEachStatement(node);
                }

                Changed = true;
                int id = _temporaryId++;
                string collectionName = $"__lcg_foreach_collection_{id}";
                string indexName = $"__lcg_foreach_index_{id}";
                ExpressionSyntax visitedSource = sourceExpression.SyntaxTree == _semanticModel.SyntaxTree
                    ? (ExpressionSyntax)Visit(sourceExpression)
                    : sourceExpression;

                if (!dictionaryPairs)
                {
                    string declarationType = LoweredTypeSource(itemType);
                    string itemRead = ReadToken($"{collectionName}[{indexName}]", itemType);
                    StatementSyntax body = (StatementSyntax)Visit(node.Statement);
                    string code = "{" +
                                  $"{DataListType} {collectionName} = {visitedSource};" +
                                  $"for (int {indexName} = 0; {indexName} < {collectionName}.Count; {indexName}++)" +
                                  "{" + $"{declarationType} {node.Identifier.ValueText} = {itemRead};" +
                                  body + "}" + "}";
                    return SyntaxFactory.ParseStatement(code).WithTriviaFrom(node);
                }

                string keysName = $"__lcg_foreach_keys_{id}";
                string keyName = $"__lcg_foreach_key_{id}";
                string valueName = $"__lcg_foreach_value_{id}";
                StatementSyntax replacedBody = (StatementSyntax)new KeyValuePairUseRewriter(
                    node.Identifier.ValueText, keyName, valueName).Visit(node.Statement);
                replacedBody = (StatementSyntax)Visit(replacedBody);
                string dictionaryCode = "{" +
                    $"{DataDictionaryType} {collectionName} = {visitedSource};" +
                    $"{DataListType} {keysName} = {collectionName}.GetKeys();" +
                    $"for (int {indexName} = 0; {indexName} < {keysName}.Count; {indexName}++)" +
                    "{" +
                    $"{LoweredTypeSource(descriptor.KeyType)} {keyName} = {ReadToken($"{keysName}[{indexName}]", descriptor.KeyType)};" +
                    $"{LoweredTypeSource(descriptor.ValueType)} {valueName} = {descriptor.Prefix}get({collectionName}, {keyName});" +
                    replacedBody + "}" + "}";
                return SyntaxFactory.ParseStatement(dictionaryCode).WithTriviaFrom(node);
            }

            private sealed class KeyValuePairUseRewriter : CSharpSyntaxRewriter
            {
                private readonly string _pairName;
                private readonly string _keyName;
                private readonly string _valueName;

                internal KeyValuePairUseRewriter(string pairName, string keyName, string valueName)
                {
                    _pairName = pairName;
                    _keyName = keyName;
                    _valueName = valueName;
                }

                public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
                {
                    if (node.Expression is IdentifierNameSyntax identifier &&
                        identifier.Identifier.ValueText == _pairName)
                    {
                        if (node.Name.Identifier.ValueText == "Key")
                            return SyntaxFactory.IdentifierName(_keyName).WithTriviaFrom(node);
                        if (node.Name.Identifier.ValueText == "Value")
                            return SyntaxFactory.IdentifierName(_valueName).WithTriviaFrom(node);
                    }
                    return base.VisitMemberAccessExpression(node);
                }
            }

            private IEnumerable<MemberDeclarationSyntax> CreateHelpers(CollectionDescriptor descriptor)
            {
                string source = descriptor.Kind == CollectionKind.List
                    ? CreateListHelpers(descriptor)
                    : CreateDictionaryHelpers(descriptor);
                CompilationUnitSyntax unit = SyntaxFactory.ParseCompilationUnit("class __Generated {" + source + "}");
                var holder = (ClassDeclarationSyntax)unit.Members[0];
                return holder.Members;
            }

            private string CreateListHelpers(CollectionDescriptor descriptor)
            {
                string p = descriptor.Prefix;
                string t = LoweredTypeSource(descriptor.ElementType);
                string read = ReadToken("token", descriptor.ElementType);
                string pack = PackText("value", descriptor.ElementType);
                return $@"
private static {DataListType} {p}createCapacity(int capacity) {{ if (capacity < 0) throw new global::System.ArgumentOutOfRangeException(""capacity""); return new {DataListType}(capacity); }}
private static {DataListType} {p}fromTokens({DataTokenType}[] values) {{ var result = new {DataListType}(values.Length); for (int i = 0; i < values.Length; i++) result.Add(values[i]); return result; }}
private static {DataListType} {p}fromArray({t}[] values) {{ if (values == null) throw new global::System.ArgumentNullException(""values""); var result = new {DataListType}(values.Length); for (int i = 0; i < values.Length; i++) result.Add({PackText("values[i]", descriptor.ElementType)}); return result; }}
private static {DataListType} {p}clone({DataListType} values) {{ if (values == null) throw new global::System.ArgumentNullException(""values""); return values.ShallowClone(); }}
private static {t} {p}get({DataListType} values, int index) {{ if (values == null) throw new global::System.NullReferenceException(); if (index < 0 || index >= values.Count) throw new global::System.ArgumentOutOfRangeException(""index""); {DataTokenType} token = values[index]; return {read}; }}
private static void {p}set({DataListType} values, int index, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); if (index < 0 || index >= values.Count) throw new global::System.ArgumentOutOfRangeException(""index""); values[index] = {pack}; }}
private static void {p}add({DataListType} values, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); values.Add({pack}); }}
private static void {p}addRangeArray({DataListType} values, {t}[] added) {{ if (values == null) throw new global::System.NullReferenceException(); if (added == null) throw new global::System.ArgumentNullException(""added""); for (int i = 0; i < added.Length; i++) values.Add({PackText("added[i]", descriptor.ElementType)}); }}
private static void {p}addRangeList({DataListType} values, {DataListType} added) {{ if (values == null) throw new global::System.NullReferenceException(); if (added == null) throw new global::System.ArgumentNullException(""added""); values.AddRange(added); }}
private static void {p}insert({DataListType} values, int index, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); if (index < 0 || index > values.Count) throw new global::System.ArgumentOutOfRangeException(""index""); values.Insert(index, {pack}); }}
private static bool {p}remove({DataListType} values, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); return values.Remove({pack}); }}
private static void {p}removeAt({DataListType} values, int index) {{ if (values == null) throw new global::System.NullReferenceException(); if (index < 0 || index >= values.Count) throw new global::System.ArgumentOutOfRangeException(""index""); values.RemoveAt(index); }}
private static void {p}clear({DataListType} values) {{ if (values == null) throw new global::System.NullReferenceException(); values.Clear(); }}
private static bool {p}contains({DataListType} values, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); return values.Contains({pack}); }}
private static int {p}indexOf({DataListType} values, {t} value) {{ if (values == null) throw new global::System.NullReferenceException(); return values.IndexOf({pack}); }}
private static {t}[] {p}toArray({DataListType} values) {{ if (values == null) throw new global::System.NullReferenceException(); {t}[] result = new {t}[values.Count]; for (int i = 0; i < values.Count; i++) {{ {DataTokenType} token = values[i]; result[i] = {read}; }} return result; }}
";
            }

            private string CreateDictionaryHelpers(CollectionDescriptor descriptor)
            {
                string p = descriptor.Prefix;
                string k = LoweredTypeSource(descriptor.KeyType);
                string v = LoweredTypeSource(descriptor.ValueType);
                string packedKey = PackText("key", descriptor.KeyType);
                string packedValue = PackText("value", descriptor.ValueType);
                string readValue = ReadToken("token", descriptor.ValueType);
                string nullKeyGuard = IsReferenceLike(descriptor.KeyType)
                    ? "if ((object)key == null) throw new global::System.ArgumentNullException(\"key\");"
                    : string.Empty;
                string packedNullKeyGuard = IsReferenceLike(descriptor.KeyType)
                    ? $"if (keys[i].TokenType == global::VRC.SDK3.Data.TokenType.Null || (keys[i].TokenType == global::VRC.SDK3.Data.TokenType.Reference && keys[i].Reference == null)) throw new global::System.ArgumentNullException(\"key\");"
                    : string.Empty;
                return $@"
private static {DataDictionaryType} {p}createCapacity(int capacity) {{ if (capacity < 0) throw new global::System.ArgumentOutOfRangeException(""capacity""); return new {DataDictionaryType}(capacity); }}
private static {DataDictionaryType} {p}fromTokens({DataTokenType}[] keys, {DataTokenType}[] values, bool[] overwrite) {{ var result = new {DataDictionaryType}(keys.Length); for (int i = 0; i < keys.Length; i++) {{ {packedNullKeyGuard} if (overwrite[i]) result.SetValue(keys[i], values[i]); else {{ if (result.ContainsKey(keys[i])) throw new global::System.ArgumentException(""An item with the same key has already been added.""); result.Add(keys[i], values[i]); }} }} return result; }}
private static {DataDictionaryType} {p}clone({DataDictionaryType} values) {{ if (values == null) throw new global::System.ArgumentNullException(""values""); return values.ShallowClone(); }}
private static {v} {p}get({DataDictionaryType} values, {k} key) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} {DataTokenType} token; if (!values.TryGetValue({packedKey}, out token)) throw new global::System.Collections.Generic.KeyNotFoundException(); return {readValue}; }}
private static void {p}set({DataDictionaryType} values, {k} key, {v} value) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} values.SetValue({packedKey}, {packedValue}); }}
private static void {p}add({DataDictionaryType} values, {k} key, {v} value) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} {DataTokenType} packed = {packedKey}; if (values.ContainsKey(packed)) throw new global::System.ArgumentException(""An item with the same key has already been added.""); values.Add(packed, {packedValue}); }}
private static bool {p}remove({DataDictionaryType} values, {k} key) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} return values.Remove({packedKey}); }}
private static bool {p}removeValue({DataDictionaryType} values, {k} key, out {v} value) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} {DataTokenType} token; bool removed = values.Remove({packedKey}, out token); value = removed ? {readValue} : default({v}); return removed; }}
private static void {p}clear({DataDictionaryType} values) {{ if (values == null) throw new global::System.NullReferenceException(); values.Clear(); }}
private static bool {p}containsKey({DataDictionaryType} values, {k} key) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} return values.ContainsKey({packedKey}); }}
private static bool {p}containsValue({DataDictionaryType} values, {v} value) {{ if (values == null) throw new global::System.NullReferenceException(); return values.ContainsValue({packedValue}); }}
private static bool {p}tryGetValue({DataDictionaryType} values, {k} key, out {v} value) {{ if (values == null) throw new global::System.NullReferenceException(); {nullKeyGuard} {DataTokenType} token; bool found = values.TryGetValue({packedKey}, out token); value = found ? {readValue} : default({v}); return found; }}
";
            }

            private IEnumerable<MemberDeclarationSyntax> CreateJsonHelpers(JsonDescriptor descriptor)
            {
                var types = new List<ITypeSymbol>();
                CollectJsonTypes(descriptor.Type, types);
                var source = new StringBuilder();
                string p = descriptor.Prefix;
                string rootType = LoweredTypeSource(descriptor.Type);
                bool rootContainer = IsJsonContainer(descriptor.Type);

                source.AppendLine($@"
private static bool {p}trySerializeCore({rootType} value, bool indented, out string json, out string error)
{{
    {DataTokenType} token;
    if (!{p}encode0(value, out token, out error)) {{ json = null; return false; }}
    {(rootContainer ? $"if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ json = \"null\"; error = null; return true; }}" : string.Empty)}
    {(rootContainer ? string.Empty : $"var wrapper = new {DataListType}(1); wrapper.Add(token); token = new {DataTokenType}(wrapper);")}
    {DataTokenType} result;
    if (!global::VRC.SDK3.Data.VRCJson.TrySerializeToJson(token, indented ? global::VRC.SDK3.Data.JsonExportType.Beautify : global::VRC.SDK3.Data.JsonExportType.Minify, out result))
    {{ json = null; error = result.ToString(); return false; }}
    json = result.String;
    {(rootContainer ? string.Empty : "json = json.Substring(1, json.Length - 2);")}
    error = null;
    return true;
}}
private static string {p}serialize({rootType} value) {{ string json; string error; if (!{p}trySerializeCore(value, false, out json, out error)) throw new global::System.Text.Json.JsonException(error); return json; }}
private static string {p}serializeOptions({rootType} value, bool writeIndented) {{ string json; string error; if (!{p}trySerializeCore(value, writeIndented, out json, out error)) throw new global::System.Text.Json.JsonException(error); return json; }}
private static bool {p}trySerialize({rootType} value, out string json, out string error) {{ return {p}trySerializeCore(value, false, out json, out error); }}
private static bool {p}tryDeserialize(string json, out {rootType} value, out string error)
{{
    {DataTokenType} token;
    {(rootContainer ? $"if (json != null && json.Trim() == \"null\") {{ value = null; error = null; return true; }}" : string.Empty)}
    string input = json;
    {(rootContainer ? string.Empty : "input = \"[\" + json + \"]\";")}
    if (!global::VRC.SDK3.Data.VRCJson.TryDeserializeFromJson(input, out token)) {{ value = default({rootType}); error = token.ToString(); return false; }}
    {(rootContainer ? string.Empty : "if (token.TokenType != global::VRC.SDK3.Data.TokenType.DataList || token.DataList.Count != 1) { value = default(" + rootType + "); error = \"JSON value wrapper was invalid.\"; return false; } token = token.DataList[0];")}
    return {p}decode0(token, out value, out error);
}}
private static {rootType} {p}deserialize(string json) {{ {rootType} value; string error; if (!{p}tryDeserialize(json, out value, out error)) throw new global::System.Text.Json.JsonException(error); return value; }}
");

                for (int i = 0; i < types.Count; i++)
                {
                    source.AppendLine(CreateJsonEncodeMethod(p, i, types, types[i]));
                    source.AppendLine(CreateJsonDecodeMethod(p, i, types, types[i]));
                }

                CompilationUnitSyntax unit = SyntaxFactory.ParseCompilationUnit("class __Generated {" + source + "}");
                var holder = (ClassDeclarationSyntax)unit.Members[0];
                return holder.Members;
            }

            private void CollectJsonTypes(ITypeSymbol type, IList<ITypeSymbol> types)
            {
                if (types.Any(existing => SymbolEqualityComparer.Default.Equals(existing, type)))
                    return;
                types.Add(type);
                if (type is IArrayTypeSymbol array)
                {
                    CollectJsonTypes(array.ElementType, types);
                    return;
                }
                if (!(type is INamedTypeSymbol named) || !TryGetCollectionKind(named, out CollectionKind kind))
                    return;
                if (kind == CollectionKind.List)
                    CollectJsonTypes(named.TypeArguments[0], types);
                else
                {
                    CollectJsonTypes(named.TypeArguments[0], types);
                    CollectJsonTypes(named.TypeArguments[1], types);
                }
            }

            private bool IsJsonContainer(ITypeSymbol type)
            {
                return type is IArrayTypeSymbol ||
                       type is INamedTypeSymbol named && TryGetCollectionKind(named, out _);
            }

            private int JsonTypeIndex(IList<ITypeSymbol> types, ITypeSymbol type)
            {
                for (int i = 0; i < types.Count; i++)
                    if (SymbolEqualityComparer.Default.Equals(types[i], type))
                        return i;
                return -1;
            }

            private string CreateJsonEncodeMethod(string p, int index, IList<ITypeSymbol> types, ITypeSymbol type)
            {
                string target = LoweredTypeSource(type);
                string header = $"private static bool {p}encode{index}({target} value, out {DataTokenType} token, out string error)";
                if (type is IArrayTypeSymbol array)
                {
                    int child = JsonTypeIndex(types, array.ElementType);
                    return $@"{header} {{ if (value == null) {{ token = default({DataTokenType}); error = null; return true; }} var list = new {DataListType}(value.Length); for (int i = 0; i < value.Length; i++) {{ {DataTokenType} item; if (!{p}encode{child}(value[i], out item, out error)) {{ token = default({DataTokenType}); return false; }} list.Add(item); }} token = new {DataTokenType}(list); error = null; return true; }}";
                }

                if (type is INamedTypeSymbol named && TryGetCollectionKind(named, out CollectionKind kind))
                {
                    if (kind == CollectionKind.List)
                    {
                        int child = JsonTypeIndex(types, named.TypeArguments[0]);
                        return $@"{header} {{ if (value == null) {{ token = default({DataTokenType}); error = null; return true; }} var list = new {DataListType}(value.Count); for (int i = 0; i < value.Count; i++) {{ {DataTokenType} item; if (!{p}encode{child}({ReadToken("value[i]", named.TypeArguments[0])}, out item, out error)) {{ token = default({DataTokenType}); return false; }} list.Add(item); }} token = new {DataTokenType}(list); error = null; return true; }}";
                    }

                    ITypeSymbol keyType = named.TypeArguments[0];
                    ITypeSymbol valueType = named.TypeArguments[1];
                    int keyChild = JsonTypeIndex(types, keyType);
                    int valueChild = JsonTypeIndex(types, valueType);
                    bool stringKeys = keyType.SpecialType == SpecialType.System_String;
                    string keyRead = ReadToken("keys[i]", keyType);
                    string valueRead = ReadToken("rawValue", valueType);
                    if (stringKeys)
                    {
                        return $@"{header} {{ if (value == null) {{ token = default({DataTokenType}); error = null; return true; }} var result = new {DataDictionaryType}(); {DataListType} keys = value.GetKeys(); for (int i = 0; i < keys.Count; i++) {{ string key = {keyRead}; {DataTokenType} rawValue; value.TryGetValue(keys[i], out rawValue); {DataTokenType} encoded; if (!{p}encode{valueChild}({valueRead}, out encoded, out error)) {{ token = default({DataTokenType}); return false; }} result.Add(new {DataTokenType}(key), encoded); }} token = new {DataTokenType}(result); error = null; return true; }}";
                    }
                    return $@"{header} {{ if (value == null) {{ token = default({DataTokenType}); error = null; return true; }} var entries = new {DataListType}(); {DataListType} keys = value.GetKeys(); for (int i = 0; i < keys.Count; i++) {{ {DataTokenType} rawValue; value.TryGetValue(keys[i], out rawValue); {DataTokenType} encodedKey; {DataTokenType} encodedValue; if (!{p}encode{keyChild}({keyRead}, out encodedKey, out error) || !{p}encode{valueChild}({valueRead}, out encodedValue, out error)) {{ token = default({DataTokenType}); return false; }} var pair = new {DataListType}(2); pair.Add(encodedKey); pair.Add(encodedValue); entries.Add(new {DataTokenType}(pair)); }} var envelope = new {DataDictionaryType}(); envelope.Add(new {DataTokenType}(""$lcgDictionary""), new {DataTokenType}(1)); envelope.Add(new {DataTokenType}(""entries""), new {DataTokenType}(entries)); token = new {DataTokenType}(envelope); error = null; return true; }}";
                }

                if (type.TypeKind == TypeKind.Enum)
                {
                    SpecialType underlying = ((INamedTypeSymbol)type).EnumUnderlyingType.SpecialType;
                    return $@"{header} {{ token = new {DataTokenType}(({NumericTypeSource(underlying)})value); error = null; return true; }}";
                }
                if (IsNativeTokenType(type))
                    return $@"{header} {{ token = new {DataTokenType}(value); error = null; return true; }}";
                return $@"{header} {{ if ((object)value == null) {{ token = default({DataTokenType}); error = null; return true; }} token = default({DataTokenType}); error = ""Object references cannot be serialized to VRCJson.""; return false; }}";
            }

            private string CreateJsonDecodeMethod(string p, int index, IList<ITypeSymbol> types, ITypeSymbol type)
            {
                string target = LoweredTypeSource(type);
                string header = $"private static bool {p}decode{index}({DataTokenType} token, out {target} value, out string error)";
                if (type is IArrayTypeSymbol array)
                {
                    int child = JsonTypeIndex(types, array.ElementType);
                    string childType = LoweredTypeSource(array.ElementType);
                    return $@"{header} {{ if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ value = null; error = null; return true; }} if (token.TokenType != global::VRC.SDK3.Data.TokenType.DataList) {{ value = null; error = ""Expected a JSON array.""; return false; }} var list = token.DataList; value = new {childType}[list.Count]; for (int i = 0; i < list.Count; i++) if (!{p}decode{child}(list[i], out value[i], out error)) return false; error = null; return true; }}";
                }

                if (type is INamedTypeSymbol named && TryGetCollectionKind(named, out CollectionKind kind))
                {
                    if (kind == CollectionKind.List)
                    {
                        int child = JsonTypeIndex(types, named.TypeArguments[0]);
                        string childType = LoweredTypeSource(named.TypeArguments[0]);
                        return $@"{header} {{ if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ value = null; error = null; return true; }} if (token.TokenType != global::VRC.SDK3.Data.TokenType.DataList) {{ value = null; error = ""Expected a JSON array.""; return false; }} var source = token.DataList; value = new {DataListType}(source.Count); for (int i = 0; i < source.Count; i++) {{ {childType} item; if (!{p}decode{child}(source[i], out item, out error)) {{ value = null; return false; }} value.Add({PackText("item", named.TypeArguments[0])}); }} error = null; return true; }}";
                    }

                    ITypeSymbol keyType = named.TypeArguments[0];
                    ITypeSymbol valueType = named.TypeArguments[1];
                    int keyChild = JsonTypeIndex(types, keyType);
                    int valueChild = JsonTypeIndex(types, valueType);
                    string keyTarget = LoweredTypeSource(keyType);
                    string valueTarget = LoweredTypeSource(valueType);
                    if (keyType.SpecialType == SpecialType.System_String)
                    {
                        return $@"{header} {{ if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ value = null; error = null; return true; }} if (token.TokenType != global::VRC.SDK3.Data.TokenType.DataDictionary) {{ value = null; error = ""Expected a JSON object.""; return false; }} var source = token.DataDictionary; value = new {DataDictionaryType}(source.Count); var keys = source.GetKeys(); for (int i = 0; i < keys.Count; i++) {{ {valueTarget} item; {DataTokenType} raw; source.TryGetValue(keys[i], out raw); if (!{p}decode{valueChild}(raw, out item, out error)) {{ value = null; return false; }} value.Add(keys[i], {PackText("item", valueType)}); }} error = null; return true; }}";
                    }
                    return $@"{header} {{ if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ value = null; error = null; return true; }} if (token.TokenType != global::VRC.SDK3.Data.TokenType.DataDictionary) {{ value = null; error = ""Expected a tagged dictionary object.""; return false; }} var envelope = token.DataDictionary; {DataTokenType} version; {DataTokenType} entryToken; if (!envelope.TryGetValue(new {DataTokenType}(""$lcgDictionary""), out version) || version.TokenType != global::VRC.SDK3.Data.TokenType.Double || version.Double != 1d || !envelope.TryGetValue(new {DataTokenType}(""entries""), out entryToken) || entryToken.TokenType != global::VRC.SDK3.Data.TokenType.DataList) {{ value = null; error = ""Invalid $lcgDictionary envelope.""; return false; }} var entries = entryToken.DataList; value = new {DataDictionaryType}(entries.Count); for (int i = 0; i < entries.Count; i++) {{ if (entries[i].TokenType != global::VRC.SDK3.Data.TokenType.DataList || entries[i].DataList.Count != 2) {{ value = null; error = ""Invalid dictionary entry.""; return false; }} var pair = entries[i].DataList; {keyTarget} key; {valueTarget} item; if (!{p}decode{keyChild}(pair[0], out key, out error) || !{p}decode{valueChild}(pair[1], out item, out error)) {{ value = null; return false; }} {DataTokenType} packedKey = {PackText("key", keyType)}; if (value.ContainsKey(packedKey)) {{ value = null; error = ""Duplicate dictionary key.""; return false; }} value.Add(packedKey, {PackText("item", valueType)}); }} error = null; return true; }}";
                }

                if (type.SpecialType == SpecialType.System_String)
                    return $@"{header} {{ if (token.TokenType == global::VRC.SDK3.Data.TokenType.Null) {{ value = null; error = null; return true; }} if (token.TokenType != global::VRC.SDK3.Data.TokenType.String) {{ value = null; error = ""Expected a JSON string.""; return false; }} value = token.String; error = null; return true; }}";
                if (type.SpecialType == SpecialType.System_Boolean)
                    return $@"{header} {{ if (token.TokenType != global::VRC.SDK3.Data.TokenType.Boolean) {{ value = false; error = ""Expected a JSON boolean.""; return false; }} value = token.Boolean; error = null; return true; }}";
                if (IsNumericType(type) || type.TypeKind == TypeKind.Enum)
                    return CreateJsonNumericDecodeMethod(header, target, type);
                return $@"{header} {{ value = default({target}); error = ""JSON object references are not supported.""; return false; }}";
            }

            private static bool IsNumericType(ITypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_SByte: case SpecialType.System_Byte:
                    case SpecialType.System_Int16: case SpecialType.System_UInt16:
                    case SpecialType.System_Int32: case SpecialType.System_UInt32:
                    case SpecialType.System_Int64: case SpecialType.System_UInt64:
                    case SpecialType.System_Single: case SpecialType.System_Double:
                        return true;
                    default: return false;
                }
            }

            private string CreateJsonNumericDecodeMethod(string header, string target, ITypeSymbol type)
            {
                SpecialType numericType = type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType
                    ? enumType.EnumUnderlyingType.SpecialType
                    : type.SpecialType;
                bool integral = numericType != SpecialType.System_Single && numericType != SpecialType.System_Double;
                string range = NumericRangeCondition(numericType);
                string cast = type.TypeKind == TypeKind.Enum
                    ? $"({target})({NumericTypeSource(numericType)})number"
                    : $"({target})number";
                return $@"{header} {{ if (token.TokenType != global::VRC.SDK3.Data.TokenType.Double) {{ value = default({target}); error = ""Expected a JSON number.""; return false; }} double number = token.Double; if (double.IsNaN(number) || double.IsInfinity(number){(integral ? " || number != global::System.Math.Truncate(number)" : string.Empty)}{range}) {{ value = default({target}); error = ""JSON number is not finite, integral, or in range for {target}.""; return false; }} value = {cast}; error = null; return true; }}";
            }

            private static string NumericRangeCondition(SpecialType type)
            {
                switch (type)
                {
                    case SpecialType.System_SByte: return " || number < -128d || number > 127d";
                    case SpecialType.System_Byte: return " || number < 0d || number > 255d";
                    case SpecialType.System_Int16: return " || number < -32768d || number > 32767d";
                    case SpecialType.System_UInt16: return " || number < 0d || number > 65535d";
                    case SpecialType.System_Int32: return " || number < -2147483648d || number > 2147483647d";
                    case SpecialType.System_UInt32: return " || number < 0d || number > 4294967295d";
                    case SpecialType.System_Int64: return " || number < -9223372036854775808d || number >= 9223372036854775808d";
                    case SpecialType.System_UInt64: return " || number < 0d || number >= 18446744073709551616d";
                    case SpecialType.System_Single: return " || number < -3.4028234663852886E+38d || number > 3.4028234663852886E+38d";
                    default: return string.Empty;
                }
            }

            private static string NumericTypeSource(SpecialType type)
            {
                switch (type)
                {
                    case SpecialType.System_SByte: return "sbyte";
                    case SpecialType.System_Byte: return "byte";
                    case SpecialType.System_Int16: return "short";
                    case SpecialType.System_UInt16: return "ushort";
                    case SpecialType.System_UInt32: return "uint";
                    case SpecialType.System_Int64: return "long";
                    case SpecialType.System_UInt64: return "ulong";
                    default: return "int";
                }
            }

            private string LoweredTypeSource(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return LoweredTypeSource(array.ElementType) + "[]";
                if (type is INamedTypeSymbol named && TryGetDescriptor(named, out CollectionDescriptor descriptor))
                    return descriptor.Kind == CollectionKind.List ? DataListType : DataDictionaryType;
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            private ExpressionSyntax Pack(ExpressionSyntax expression, ITypeSymbol type)
            {
                return SyntaxFactory.ParseExpression(PackText(expression.ToString(), type));
            }

            private string PackText(string expression, ITypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Enum)
                {
                    SpecialType underlying = ((INamedTypeSymbol)type).EnumUnderlyingType.SpecialType;
                    return $"new {DataTokenType}(({NumericTypeSource(underlying)})({expression}))";
                }
                if (type is INamedTypeSymbol named && TryGetDescriptor(named, out _))
                    return $"new {DataTokenType}({expression})";
                if (IsNativeTokenType(type))
                    return $"new {DataTokenType}({expression})";
                return $"new {DataTokenType}((object)({expression}))";
            }

            private string ReadToken(string tokenExpression, ITypeSymbol type)
            {
                string target = LoweredTypeSource(type);
                if (type.TypeKind == TypeKind.Enum)
                {
                    SpecialType underlying = ((INamedTypeSymbol)type).EnumUnderlyingType.SpecialType;
                    return $"({target})({NumericTypeSource(underlying)})({tokenExpression})";
                }
                if (type is INamedTypeSymbol named && TryGetDescriptor(named, out _))
                    return $"({target})({tokenExpression})";
                if (IsNativeTokenType(type))
                    return $"({target})({tokenExpression})";
                return $"({target})({tokenExpression}).Reference";
            }

            private static bool IsNativeTokenType(ITypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_String:
                        return true;
                    default:
                        return false;
                }
            }

            private static bool IsReferenceLike(ITypeSymbol type)
            {
                return type.IsReferenceType || type.TypeKind == TypeKind.Array;
            }

            private static InvocationExpressionSyntax Call(string methodName,
                params ExpressionSyntax[] expressions)
            {
                return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(methodName),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                        expressions.Select(SyntaxFactory.Argument))));
            }
        }
    }
}
