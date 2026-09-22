
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Linq;
using System.Text;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Core;
using UdonSharp.Localization;
using UnityEngine;
using NotSupportedException = UdonSharp.Core.NotSupportedException;

namespace UdonSharp.Compiler.Binder
{
    internal class BinderSyntaxVisitor : CSharpSyntaxVisitor<BoundNode>
    {
        private Symbol OwningSymbol { get; }
        private BindContext Context { get; }
        private SemanticModel SymbolLookupModel { get; }
        private sealed class CatchVariableInfo
        {
            public BoundCatchClause Clause;
            public bool IsUdonException;
        }

        private readonly Dictionary<ISymbol, CatchVariableInfo> _catchVariables =
            new Dictionary<ISymbol, CatchVariableInfo>(SymbolEqualityComparer.Default);
        private readonly Stack<BoundCatchClause> _activeCatchClauses = new Stack<BoundCatchClause>();
        private int _protectedBindDepth;

        public BinderSyntaxVisitor(Symbol owningSymbol, BindContext context)
        {
            OwningSymbol = owningSymbol;
            Context = context;

            SymbolLookupModel = context.CompileContext.GetSemanticModel(owningSymbol.RoslynSymbol.DeclaringSyntaxReferences.First().SyntaxTree);
        }

        private void UpdateSyntaxNode(SyntaxNode node)
        {
            Context.CurrentNode = node;
        }

        public override BoundNode Visit(SyntaxNode node)
        {
            UpdateSyntaxNode(node);

            if (node.Kind() == SyntaxKind.BaseExpression)
                return null;

            // Strip unary plus operator
            if (node.Kind() == SyntaxKind.UnaryPlusExpression)
                return Visit((node as PrefixUnaryExpressionSyntax)?.Operand);
            
            Symbol nodeSymbol = GetSymbol(node);
            if (nodeSymbol is TypeSymbol)
                return null;
            
            return base.Visit(node);
        }

        public override BoundNode VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            return VisitExpression(node.Expression);
        }

        public override BoundNode VisitEmptyStatement(EmptyStatementSyntax node)
        {
            return new BoundBlock(node);
        }

        public override BoundNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            throw new System.NotImplementedException();
        }

        private BoundAccessExpression VisitAccessExpression(SyntaxNode node)
        {
            if (node.Kind() == SyntaxKind.ElementAccessExpression)
                return (BoundAccessExpression)Visit(node);
            
            if (node.Kind() != SyntaxKind.IdentifierName &&
                node.Kind() != SyntaxKind.SimpleMemberAccessExpression &&
                node.Kind() != SyntaxKind.ThisExpression) return null;

            if (node is MemberAccessExpressionSyntax catchAccess &&
                SymbolLookupModel.GetSymbolInfo(catchAccess.Expression).Symbol is ISymbol catchVariable &&
                _catchVariables.TryGetValue(catchVariable, out CatchVariableInfo catchInfo))
            {
                ExceptionPayloadMember payloadMember;
                TypeSymbol payloadType;
                switch (catchAccess.Name.Identifier.ValueText)
                {
                    case "Message":
                        payloadMember = ExceptionPayloadMember.Message;
                        payloadType = Context.GetTypeSymbol(SpecialType.System_String);
                        break;
                    case "Kind" when catchInfo.IsUdonException:
                        payloadMember = ExceptionPayloadMember.Kind;
                        payloadType = Context.GetTypeSymbol(typeof(UdonExceptionKind));
                        break;
                    case "Code" when catchInfo.IsUdonException:
                        payloadMember = ExceptionPayloadMember.Code;
                        payloadType = Context.GetTypeSymbol(SpecialType.System_Int32);
                        break;
                    case "Operation" when catchInfo.IsUdonException:
                        payloadMember = ExceptionPayloadMember.Operation;
                        payloadType = Context.GetTypeSymbol(SpecialType.System_String);
                        break;
                    default:
                        throw new CompilerException($"Catch variable member '{catchAccess.Name.Identifier.ValueText}' is not supported. Standard exceptions expose Message; UdonException also exposes Kind, Code, and Operation.", catchAccess.GetLocation());
                }

                return new BoundExceptionPayloadAccessExpression(catchAccess, catchInfo.Clause, payloadMember, payloadType);
            }

            if (node is IdentifierNameSyntax catchIdentifier &&
                SymbolLookupModel.GetSymbolInfo(catchIdentifier).Symbol is ISymbol identifierSymbol &&
                _catchVariables.ContainsKey(identifierSymbol))
            {
                throw new CompilerException("Catch variables are compiler pseudo-values and may only be used to read their supported payload properties.", catchIdentifier.GetLocation());
            }

            Symbol nodeSymbol = GetSymbol(node);

            if (_protectedBindDepth > 0 && nodeSymbol is PropertySymbol protectedProperty &&
                OwningSymbol is MethodSymbol protectedOwningMethod)
            {
                protectedOwningMethod.AddProtectedDependency(protectedProperty.GetMethod);
                protectedOwningMethod.AddProtectedDependency(protectedProperty.SetMethod);
            }

            if (nodeSymbol.RoslynSymbol.Kind == SymbolKind.NamedType)
                return null;
            BoundExpression lhsExpression = null;
            if (node is MemberAccessExpressionSyntax accessExpressionSyntax)
            {
                lhsExpression = VisitExpression(accessExpressionSyntax.Expression);

                if (accessExpressionSyntax.Expression.Kind() != SyntaxKind.ThisExpression)
                {
                    if (lhsExpression == null && !nodeSymbol.IsStatic)
                        lhsExpression = BoundAccessExpression.BindThisAccess(OwningSymbol.ContainingType);
                    
                    BoundAccessExpression access = BoundAccessExpression.BindAccess(Context, node, nodeSymbol, lhsExpression);
                    if (accessExpressionSyntax.Expression.Kind() == SyntaxKind.BaseExpression)
                        access.MarkForcedBaseCall();

                    return access;
                }
            }
            
            if (!nodeSymbol.IsStatic)
                lhsExpression = BoundAccessExpression.BindThisAccess(OwningSymbol.ContainingType);

            return BoundAccessExpression.BindAccess(Context, node, nodeSymbol, lhsExpression);
        }

        private BoundExpression VisitExpression(SyntaxNode node)
        {
            BoundExpression accessExpression = VisitAccessExpression(node);
            if (accessExpression != null)
                return accessExpression;
            
            BoundExpression expression = (BoundExpression)Visit(node);
            if (_protectedBindDepth > 0 && expression is BoundInvocationExpression invocation &&
                OwningSymbol is MethodSymbol owningMethod)
            {
                owningMethod.AddProtectedDependency(invocation.Method);
            }

            return expression;
        }

        private static bool TryImplicitConstantConversion(ref BoundExpression boundExpression, TypeSymbol targetType)
        {
            if (!boundExpression.IsConstant) return false;
            if (boundExpression.ValueType == targetType) return false;

            var targetSystemType = targetType.UdonType.SystemType;
            
            object constantValue = boundExpression.ConstantValue.Value;
            
            // if (targetSystemType == typeof(string))
            // {
            //     IConstantValue constant = new ConstantValue<string>(constantValue?.ToString() ?? "");
            //
            //     boundExpression = new BoundConstantExpression(constant, targetType, boundExpression.SyntaxNode);
            // }
            
            var sourceSystemType = boundExpression.ValueType.UdonType.SystemType;
            
            if (ConstantExpressionOptimizer.CanDoConstantConversion(sourceSystemType) &&
                ConstantExpressionOptimizer.CanDoConstantConversion(targetSystemType))
            {
                IConstantValue constant = (IConstantValue)Activator.CreateInstance(typeof(ConstantValue<>).MakeGenericType(targetSystemType), ConstantExpressionOptimizer.FoldConstantConversion(targetSystemType, constantValue));
                boundExpression = new BoundConstantExpression(constant, targetType, boundExpression.SyntaxNode);

                return true;
            }

            if (boundExpression.ValueType.IsEnum && 
                boundExpression.ValueType.IsExtern &&
                UdonSharpUtils.IsIntegerType(targetSystemType))
            {
                boundExpression = new BoundConstantExpression(ConstantExpressionOptimizer.FoldConstantConversion(targetSystemType, constantValue), targetType);

                return true;
            }
            
            return false;
        }
        
        public BoundExpression VisitExpression(SyntaxNode node, TypeSymbol expectedType, bool explicitCast = false)
        {
            BoundExpression boundExpression = VisitExpression(node);

            if (expectedType == null)
                return boundExpression;

            return ConvertExpression(node, boundExpression, expectedType, explicitCast);
        }

        private static BoundExpression ConvertExpression(SyntaxNode node, BoundExpression sourceExpression, TypeSymbol expectedType, bool explicitCast = false)
        {
            if (expectedType == sourceExpression.ValueType)
                return sourceExpression;

            if (TryImplicitConstantConversion(ref sourceExpression, expectedType))
                return sourceExpression;

            return new BoundCastExpression(node, sourceExpression, expectedType, explicitCast);
        }

        private BoundExpression VisitExpression(SyntaxNode node, System.Type expectedType, bool explicitCast = false)
        {
            return VisitExpression(node, Context.GetTypeSymbol(expectedType), explicitCast);
        }

        private BoundStatement VisitStatement(SyntaxNode node)
        {
            return (BoundStatement) Visit(node);
        }

        private Symbol GetSymbol(SyntaxNode node)
        {
            ISymbol symbol = SymbolLookupModel.GetSymbolInfo(node).Symbol;

            if (symbol == null || symbol.Kind == SymbolKind.Namespace)
                return null;
            
            return Context.GetSymbol(symbol);
        }

        private TypeSymbol GetTypeSymbol(SyntaxNode node)
        {
            return Context.GetTypeSymbol(SymbolLookupModel.GetTypeInfo(node).Type);
        }

        private Symbol GetDeclaredSymbol(SyntaxNode node)
        {
            return Context.GetSymbol(SymbolLookupModel.GetDeclaredSymbol(node));
        }

        public override BoundNode DefaultVisit(SyntaxNode node)
        {
            throw new NotSupportedException(LocStr.CE_NodeNotSupported, node.GetLocation(), node.Kind());
        }

        // This will only be visited from within a method declaration so it means it only gets hit if there's a local method declaration which is not supported.
        public override BoundNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            throw new NotSupportedException(LocStr.CE_LocalMethodsNotSupported, node.GetLocation());
        }

        public override BoundNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            throw new NotSupportedException(LocStr.CE_LocalMethodsNotSupported);
        }
        
        public override BoundNode VisitTryStatement(TryStatementSyntax node)
        {
            if (node.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
                throw new CompilerException("await inside try/catch/finally is not supported by synchronous compiler-managed exception handling.", node.GetLocation());

            if (OwningSymbol is MethodSymbol owningMethod)
                owningMethod.MarkProtectedRegion();

            _protectedBindDepth++;
            BoundStatement tryBody = VisitStatement(node.Block);
            var catches = new List<BoundCatchClause>();

            foreach (CatchClauseSyntax catchSyntax in node.Catches)
            {
                if (catchSyntax.Filter != null)
                    throw new CompilerException("Catch filters are not supported by compiler-managed exception handling.", catchSyntax.Filter.GetLocation());

                int[] matchingKinds = GetCatchKinds(catchSyntax);
                var catchClause = new BoundCatchClause(catchSyntax, matchingKinds);
                ISymbol catchVariable = catchSyntax.Declaration == null
                    ? null
                    : SymbolLookupModel.GetDeclaredSymbol(catchSyntax.Declaration);

                if (catchVariable != null)
                {
                    ITypeSymbol catchType = SymbolLookupModel.GetTypeInfo(catchSyntax.Declaration.Type).Type;
                    _catchVariables.Add(catchVariable, new CatchVariableInfo
                    {
                        Clause = catchClause,
                        IsUdonException = catchType?.ToDisplayString() == "UdonSharp.UdonException",
                    });
                }

                _activeCatchClauses.Push(catchClause);
                catchClause.Body = VisitStatement(catchSyntax.Block);
                _activeCatchClauses.Pop();

                if (catchVariable != null)
                    _catchVariables.Remove(catchVariable);

                catches.Add(catchClause);
            }

            BoundStatement finallyBody = node.Finally == null ? null : VisitStatement(node.Finally.Block);
            _protectedBindDepth--;
            return new BoundTryStatement(node, tryBody, catches, finallyBody);
        }

        public override BoundNode VisitCatchClause(CatchClauseSyntax node)
        {
            throw new InvalidOperationException("Catch clauses are bound by VisitTryStatement.");
        }

        public override BoundNode VisitFinallyClause(FinallyClauseSyntax node)
        {
            return Visit(node.Block);
        }

        public override BoundNode VisitThrowStatement(ThrowStatementSyntax node)
        {
            if (OwningSymbol is MethodSymbol owningMethod)
                owningMethod.MarkExplicitThrow();

            if (node.Expression == null)
            {
                if (_activeCatchClauses.Count == 0)
                    throw new CompilerException("throw; is only valid inside a catch clause.", node.GetLocation());

                return new BoundThrowStatement(node, _activeCatchClauses.Peek());
            }

            if (!(node.Expression is ObjectCreationExpressionSyntax creation))
                throw new CompilerException("Only 'throw new' with an approved exception constructor is supported.", node.Expression.GetLocation());

            return BindThrowCreation(node, creation);
        }

        public override BoundNode VisitThrowExpression(ThrowExpressionSyntax node)
        {
            throw new CompilerException("Throw expressions are not supported; use a throw statement.", node.GetLocation());
        }

        private int[] GetCatchKinds(CatchClauseSyntax node)
        {
            if (node.Declaration == null)
                return null;

            string typeName = SymbolLookupModel.GetTypeInfo(node.Declaration.Type).Type?.ToDisplayString();
            switch (typeName)
            {
                case "UdonSharp.UdonException":
                case "System.Exception":
                    return null;
                case "System.NullReferenceException":
                    return new[] { (int)UdonExceptionKind.NullReference };
                case "System.IndexOutOfRangeException":
                    return new[] { (int)UdonExceptionKind.IndexOutOfRange };
                case "System.DivideByZeroException":
                    return new[] { (int)UdonExceptionKind.DivideByZero };
                case "System.InvalidOperationException":
                    return new[] { (int)UdonExceptionKind.InvalidOperation };
                case "System.ArgumentException":
                    return new[] { (int)UdonExceptionKind.Argument, (int)UdonExceptionKind.ArgumentNull, (int)UdonExceptionKind.ArgumentOutOfRange };
                case "System.ArgumentNullException":
                    return new[] { (int)UdonExceptionKind.ArgumentNull };
                case "System.ArgumentOutOfRangeException":
                    return new[] { (int)UdonExceptionKind.ArgumentOutOfRange };
                case "System.NotSupportedException":
                    return new[] { (int)UdonExceptionKind.NotSupported };
                default:
                    throw new CompilerException($"Exception type '{typeName}' is not supported by compiler-managed catches.", node.Declaration.Type.GetLocation());
            }
        }

        private BoundNode BindThrowCreation(ThrowStatementSyntax throwSyntax, ObjectCreationExpressionSyntax creation)
        {
            string typeName = SymbolLookupModel.GetTypeInfo(creation).Type?.ToDisplayString();
            IObjectCreationOperation creationOperation = SymbolLookupModel.GetOperation(creation) as IObjectCreationOperation;
            IArgumentOperation[] suppliedArguments = creationOperation?.Arguments
                .Where(argument => !argument.IsImplicit)
                .ToArray() ?? Array.Empty<IArgumentOperation>();
            TypeSymbol stringType = Context.GetTypeSymbol(SpecialType.System_String);
            TypeSymbol intType = Context.GetTypeSymbol(SpecialType.System_Int32);
            BoundExpression message;
            BoundExpression code = new BoundConstantExpression(0, intType, creation);
            // Select the value overload: a bare null selects IConstantValue and
            // leaves the constant wrapper null, which crashes during emission.
            BoundExpression operation = new BoundConstantExpression((object)null, stringType, creation);
            int kind;

            if (typeName == "UdonSharp.UdonException")
            {
                IArgumentOperation kindArgument = suppliedArguments.FirstOrDefault(argument => argument.Parameter?.Name == "kind");
                IArgumentOperation messageArgument = suppliedArguments.FirstOrDefault(argument => argument.Parameter?.Name == "message");
                if (kindArgument == null || messageArgument == null || suppliedArguments.Length > 4)
                    throw new CompilerException("UdonException must use (kind, message, code = 0, operation = null).", creation.GetLocation());

                ExpressionSyntax kindExpression = (ExpressionSyntax)kindArgument.Value.Syntax;
                Optional<object> kindConstant = SymbolLookupModel.GetConstantValue(kindExpression);
                if (!kindConstant.HasValue)
                    throw new CompilerException("UdonException kind must be a compile-time constant.", kindExpression.GetLocation());

                kind = Convert.ToInt32(kindConstant.Value);
                message = VisitExpression((ExpressionSyntax)messageArgument.Value.Syntax, stringType);
                IArgumentOperation codeArgument = suppliedArguments.FirstOrDefault(argument => argument.Parameter?.Name == "code");
                IArgumentOperation operationArgument = suppliedArguments.FirstOrDefault(argument => argument.Parameter?.Name == "operation");
                if (codeArgument != null)
                    code = VisitExpression((ExpressionSyntax)codeArgument.Value.Syntax, intType);
                if (operationArgument != null)
                    operation = VisitExpression((ExpressionSyntax)operationArgument.Value.Syntax, stringType);
            }
            else
            {
                kind = GetThrownKind(typeName, creation);
                if (suppliedArguments.Length > 1)
                    throw new CompilerException("Approved standard exceptions support only parameterless and string constructors.", creation.GetLocation());

                message = suppliedArguments.Length == 1
                    ? VisitExpression((ExpressionSyntax)suppliedArguments[0].Value.Syntax, stringType)
                    : new BoundConstantExpression(typeName, stringType, creation);
            }

            return new BoundThrowStatement(throwSyntax, kind, message, code, operation);
        }

        private static int GetThrownKind(string typeName, SyntaxNode node)
        {
            switch (typeName)
            {
                case "System.Exception": return (int)UdonExceptionKind.Explicit;
                case "System.NullReferenceException": return (int)UdonExceptionKind.NullReference;
                case "System.IndexOutOfRangeException": return (int)UdonExceptionKind.IndexOutOfRange;
                case "System.DivideByZeroException": return (int)UdonExceptionKind.DivideByZero;
                case "System.InvalidOperationException": return (int)UdonExceptionKind.InvalidOperation;
                case "System.ArgumentException": return (int)UdonExceptionKind.Argument;
                case "System.ArgumentNullException": return (int)UdonExceptionKind.ArgumentNull;
                case "System.ArgumentOutOfRangeException": return (int)UdonExceptionKind.ArgumentOutOfRange;
                case "System.NotSupportedException": return (int)UdonExceptionKind.NotSupported;
                default:
                    throw new CompilerException($"Exception type '{typeName}' is not supported by compiler-managed throw.", node.GetLocation());
            }
        }

        public override BoundNode VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            return VisitExpression(node.Expression);
        }

        public override BoundNode VisitBlock(BlockSyntax node)
        {
            if (node.Statements.Count == 0)
                return new BoundBlock(node);

            BoundStatement[] boundStatements = new BoundStatement[node.Statements.Count];

            int statementCount = node.Statements.Count;
            for (int i = 0; i < statementCount; ++i)
            {
                BoundNode statement = Visit(node.Statements[i]);
                boundStatements[i] = (BoundStatement)statement;
            }

            return new BoundBlock(node, boundStatements);
        }

        public override BoundNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            return new BoundExpressionStatement(node.Expression, (BoundExpression)Visit(node.Expression));
        }

        private static IConstantValue GetDefaultValue(TypeSymbol type)
        {
            IConstantValue constantValue;
            
            if (type.IsValueType)
            {
                constantValue = (IConstantValue) Activator.CreateInstance(
                    typeof(ConstantValue<>).MakeGenericType(type.UdonType.SystemType),
                    Activator.CreateInstance(type.UdonType.SystemType, null));
            }
            else
            {
                constantValue = (IConstantValue) Activator.CreateInstance(typeof(ConstantValue<>).MakeGenericType(type.UdonType.SystemType), new object[] {null});
            }

            return constantValue;
        }

        public override BoundNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            IConstantValue constantValue;
            TypeSymbol constantType;

            switch (node.Kind())
            {
                case SyntaxKind.NumericLiteralExpression:
                    Type type = node.Token.Value.GetType();
                    constantType = Context.GetTypeSymbol(type);
                    constantValue = (IConstantValue)Activator.CreateInstance(typeof(ConstantValue<>).MakeGenericType(type), node.Token.Value);
                    break;
                case SyntaxKind.StringLiteralExpression:
                    constantType = Context.GetTypeSymbol(SpecialType.System_String);
                    constantValue = new ConstantValue<string>((string)node.Token.Value);
                    break;
                case SyntaxKind.CharacterLiteralExpression:
                    constantType = Context.GetTypeSymbol(SpecialType.System_Char);
                    constantValue = new ConstantValue<char>((char)node.Token.Value);
                    break;
                case SyntaxKind.TrueLiteralExpression:
                    constantType = Context.GetTypeSymbol(SpecialType.System_Boolean);
                    constantValue = new ConstantValue<bool>(true);
                    break;
                case SyntaxKind.FalseLiteralExpression:
                    constantType = Context.GetTypeSymbol(SpecialType.System_Boolean);
                    constantValue = new ConstantValue<bool>(false);
                    break;
                case SyntaxKind.NullLiteralExpression:
                    constantType = Context.GetTypeSymbol(SpecialType.System_Object);
                    constantValue = new ConstantValue<object>(null);
                    break;
                case SyntaxKind.DefaultLiteralExpression:
                    constantType = GetTypeSymbol(node);
                    constantValue = GetDefaultValue(constantType);
                    break;
                default:
                    return base.VisitLiteralExpression(node);
            }

            return new BoundConstantExpression(constantValue, constantType, node);
        }

        public override BoundNode VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            TypeSymbol constantType = GetTypeSymbol(node);
            return new BoundConstantExpression(GetDefaultValue(constantType), constantType, node);
        }

        public override BoundNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            ITypeSymbol roslynType = SymbolLookupModel.GetTypeInfo(node.Type).Type;
            string violation = GenericRestrictionPolicy.GetViolation(roslynType, GenericUseSite.RuntimeType);
            if (violation != null)
                throw new CompilerException(violation, node.GetLocation());

            TypeSymbol type = GetTypeSymbol(node.Type);

            if (!type.IsExtern)
                throw new NotSupportedException("Cannot use typeof on user-defined types", node.GetLocation());

            return new BoundConstantExpression(type.UdonType.SystemType, Context.GetTypeSymbol(typeof(Type)));
        }
        
        private BoundExpression HandleNameOfExpression(InvocationExpressionSyntax node)
        {
            SyntaxNode currentNode = node.ArgumentList.Arguments[0].Expression;
            string currentName = "";

            while (currentNode != null)
            {
                switch (currentNode.Kind())
                {
                    case SyntaxKind.SimpleMemberAccessExpression:
                        MemberAccessExpressionSyntax memberNode = (MemberAccessExpressionSyntax)currentNode;
                        currentName = memberNode.Name.Identifier.ValueText;
                        currentNode = memberNode.Name;
                        break;
                    case SyntaxKind.IdentifierName:
                        IdentifierNameSyntax identifierName = (IdentifierNameSyntax)currentNode;
                        currentName = identifierName.Identifier.ValueText;
                        currentNode = null;
                        break;
                    default:
                        currentNode = null;
                        break;
                }
            }

            if (currentName == "")
                throw new ArgumentException("Expression does not have a name");

            return new BoundConstantExpression(currentName, Context.GetTypeSymbol(SpecialType.System_String));
        }
        
        public override BoundNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            MethodSymbol methodSymbol = (MethodSymbol)GetSymbol(node);

            // Check if the symbol is null because you can technically have methods named nameof since it is not reserved
            if (methodSymbol == null &&
                node.Expression is IdentifierNameSyntax nameSyntax &&
                nameSyntax.Identifier.Text == "nameof")
                return HandleNameOfExpression(node);

            ValidateByRefArguments(node);

            BoundExpression instanceExpression = null;
            
            if (node.Expression is MemberAccessExpressionSyntax accessExpressionSyntax)
                instanceExpression = VisitExpression(accessExpressionSyntax.Expression);

            // Implicit this on member functions for this behaviour
            if (instanceExpression == null && 
                !methodSymbol.IsStatic && 
                methodSymbol.IsExtern)
            {
                instanceExpression = BoundAccessExpression.BindThisAccess(OwningSymbol.ContainingType);
            }

            BoundExpression[] boundArguments = new BoundExpression[methodSymbol.Parameters.Length];
            var argumentsList = node.ArgumentList.Arguments;

            int startIdx = 0;

            if (instanceExpression != null && methodSymbol.RoslynSymbol.IsExtensionMethod)
            {
                boundArguments[0] = instanceExpression;
                instanceExpression = null;
                startIdx = 1;
            }

            bool hasParams = false;
            int handledArgsCount = startIdx;
            ArgumentSyntax paramsNamedArg = null;

            for (int i = startIdx; i < boundArguments.Length; ++i)
            {
                if (methodSymbol.Parameters[i].IsParams)
                {
                    hasParams = true;
                    paramsNamedArg = argumentsList.FirstOrDefault(x => x.NameColon?.Name.Identifier.ValueText == methodSymbol.Parameters[i].Name);

                    break;
                }

                ArgumentSyntax argument;

                if (i - startIdx >= argumentsList.Count)
                {
                    argument = null;
                }
                else if (argumentsList[i - startIdx].NameColon != null)
                {
                    argument = argumentsList.FirstOrDefault(x => x.NameColon?.Name.Identifier.ValueText == methodSymbol.Parameters[i].Name);
                }
                else
                {
                    argument = argumentsList[i - startIdx];
                }

                if (argument == null) // Default argument handling
                {
                    boundArguments[i] = new BoundConstantExpression(methodSymbol.Parameters[i].DefaultValue, methodSymbol.Parameters[i].Type, node);
                    continue;
                }

                boundArguments[i] = VisitExpression(argument.Expression, methodSymbol.Parameters[i].Type);
                handledArgsCount++;
            }

            if (hasParams)
            {
                int paramCount;

                BoundExpression[] paramExpressions;

                if (paramsNamedArg != null)
                {
                    paramCount = 1;
                    paramExpressions = new BoundExpression[paramCount];

                    paramExpressions[0] = VisitExpression(paramsNamedArg.Expression);
                }
                else
                {
                    paramCount = argumentsList.Count - handledArgsCount;
                    paramExpressions = new BoundExpression[paramCount];

                    int idx = 0;
                    for (int i = handledArgsCount; i < argumentsList.Count; ++i)
                    {
                        paramExpressions[idx++] = VisitExpression(argumentsList[i].Expression);
                    }
                }

                void SetParamsArray()
                {
                    TypeSymbol paramType = methodSymbol.Parameters.Last().Type;
                    boundArguments[boundArguments.Length - 1] = new BoundConstArrayCreationExpression(node, paramType,
                        paramExpressions.Select(e => ConvertExpression(node, e, paramType.ElementType)).ToArray());
                }

                void SetDirectParam()
                {
                    boundArguments[boundArguments.Length - 1] = paramExpressions[0];
                }
                
                if (paramCount != 1)
                {
                    SetParamsArray();
                }
                else if (paramExpressions[0].ValueType == methodSymbol.Parameters.Last().Type)
                {
                    SetDirectParam();
                }
                else
                {
                    Conversion conversion = Context.CompileContext.RoslynCompilation.ClassifyConversion(paramExpressions[0].ValueType.RoslynSymbol, methodSymbol.Parameters.Last().Type.RoslynSymbol);

                    if (conversion.IsImplicit) // Covariant array param conversion
                        SetDirectParam();
                    else
                        SetParamsArray();
                }
            }

            var invocation = BoundInvocationExpression.CreateBoundInvocation(Context, node, methodSymbol, instanceExpression, boundArguments);
            
            if ((instanceExpression == null || instanceExpression.IsThis) && node.Expression is MemberAccessExpressionSyntax accessExpressionSyntax2 && 
                accessExpressionSyntax2.Expression.Kind() == SyntaxKind.BaseExpression)
                invocation.MarkForcedBaseCall();

            return invocation;
        }

        private void ValidateByRefArguments(InvocationExpressionSyntax node)
        {
            if (!(SymbolLookupModel.GetOperation(node) is IInvocationOperation operation))
                return;

            HashSet<string> byRefLocations = new HashSet<string>(StringComparer.Ordinal);
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                RefKind refKind = argument.Parameter?.RefKind ?? RefKind.None;
                if (refKind == RefKind.In)
                    throw new CompilerException("U# does not support 'in' parameters", argument.Syntax.GetLocation());
                if (refKind != RefKind.Ref && refKind != RefKind.Out)
                    continue;

                string locationKey = GetByRefLocationKey(argument.Value.Syntax);
                if (!byRefLocations.Add(locationKey))
                    throw new CompilerException(
                        "The same storage location cannot be passed to multiple ref/out parameters because copy-back order would be ambiguous",
                        argument.Syntax.GetLocation());
            }
        }

        private string GetByRefLocationKey(SyntaxNode syntax)
        {
            if (syntax is ElementAccessExpressionSyntax elementAccess)
            {
                string arrayKey = GetByRefLocationKey(elementAccess.Expression);
                var constantIndices = elementAccess.ArgumentList.Arguments
                    .Select(argument => SymbolLookupModel.GetConstantValue(argument.Expression))
                    .ToArray();
                if (constantIndices.All(value => value.HasValue))
                    return arrayKey + "[" + string.Join(",", constantIndices.Select(value => value.Value)) + "]";

                return arrayKey + "[*]";
            }

            if (syntax is MemberAccessExpressionSyntax memberAccess)
            {
                ISymbol member = SymbolLookupModel.GetSymbolInfo(memberAccess).Symbol;
                ITypeSymbol receiverType = SymbolLookupModel.GetTypeInfo(memberAccess.Expression).Type;
                if (member != null && receiverType?.IsValueType == true)
                    return GetByRefLocationKey(memberAccess.Expression) + "." +
                           GetSymbolLocationKey(member);
                if (member != null)
                    return GetSymbolLocationKey(member);
            }

            ISymbol symbol = SymbolLookupModel.GetSymbolInfo(syntax).Symbol;
            return symbol != null
                ? GetSymbolLocationKey(symbol)
                : syntax.WithoutTrivia().ToString();
        }

        private static string GetSymbolLocationKey(ISymbol symbol)
        {
            // ParameterSymbol.ToDisplayString() may contain only its type, causing two distinct
            // parameters of the same type to be mistaken for one aliased storage location.
            string owner = symbol.ContainingSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ??
                           string.Empty;
            return symbol.Kind + "|" + owner + "|" + symbol.Name;
        }

        public override BoundNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            return new BoundLocalDeclarationStatement(node, (BoundVariableDeclarationStatement)VisitVariableDeclaration(node.Declaration));
        }

        public BoundExpression VisitVariableInitializer(ExpressionSyntax expressionSyntax, TypeSymbol type)
        {
            if (expressionSyntax == null)
                return null;

            if (expressionSyntax.Kind() == SyntaxKind.ArrayInitializerExpression)
            {
                InitializerExpressionSyntax initializerExpressionSyntax = (InitializerExpressionSyntax)expressionSyntax;
                
                var initializerExpressions = initializerExpressionSyntax.Expressions;
                BoundExpression[] elementCounts =
                {
                    new BoundConstantExpression(new ConstantValue<int>(initializerExpressions.Count),
                        Context.GetTypeSymbol(SpecialType.System_Int32), expressionSyntax)
                };
                BoundExpression[] initializers = new BoundExpression[initializerExpressions.Count];

                TypeSymbol elementType = type.ElementType;

                for (int i = 0; i < initializers.Length; ++i)
                    initializers[i] = VisitExpression(initializerExpressions[i], elementType);

                return new BoundArrayCreationExpression(expressionSyntax, Context, type, elementCounts, initializers);
            }

            return VisitExpression(expressionSyntax, type);
        }

        public override BoundNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            BoundVariableDeclaratorStatement[] boundDeclarations =
                new BoundVariableDeclaratorStatement[node.Variables.Count];

            int idx = 0;
            
            foreach (var declaration in node.Variables)
            {
                Symbol declaredSymbol = GetDeclaredSymbol(declaration);
                TypeSymbol declarationType;
                switch (declaredSymbol)
                {
                    case FieldSymbol fieldSymbol:
                        declarationType = fieldSymbol.Type;
                        break;
                    case LocalSymbol localSymbol:
                        declarationType = localSymbol.Type;
                        break;
                    default:
                        throw new InvalidOperationException("Invalid variable declaration");
                }
                
                boundDeclarations[idx++] = new BoundVariableDeclaratorStatement(declaration, declaredSymbol, VisitVariableInitializer(declaration.Initializer?.Value, declarationType));
            }

            return new BoundVariableDeclarationStatement(node, boundDeclarations);
        }

        public override BoundNode VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            return BoundAccessExpression.BindAccess(Context, node, GetDeclaredSymbol(node.Designation), null);
        }

        public override BoundNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            BoundAccessExpression assignmentTarget = VisitAccessExpression(node.Left);

            if (node.Kind() != SyntaxKind.SimpleAssignmentExpression)
            {
                MethodSymbol operatorSymbol = (MethodSymbol)GetSymbol(node);

                BoundExpression rhsExpression = VisitExpression(node.Right);

                if (operatorSymbol is ExternBuiltinOperatorSymbol builtinOperatorSymbol)
                {
                    if (builtinOperatorSymbol.ReturnType == Context.GetTypeSymbol(SpecialType.System_String) &&
                        builtinOperatorSymbol.OperatorType == BuiltinOperatorType.Addition &&
                        builtinOperatorSymbol.Parameters[0].Type != builtinOperatorSymbol.Parameters[1].Type)
                    {
                        if (rhsExpression.IsConstant && builtinOperatorSymbol.Parameters[1].Type == Context.GetTypeSymbol(SpecialType.System_Object))
                        {
                            rhsExpression = new BoundConstantExpression(rhsExpression.ConstantValue.Value.ToString(),
                                Context.GetTypeSymbol(SpecialType.System_String));
                            operatorSymbol = new ExternSynthesizedOperatorSymbol(BuiltinOperatorType.Addition,
                                Context.GetTypeSymbol(SpecialType.System_String), Context);
                        }
                        else
                        {
                            operatorSymbol = new ExternBuiltinOperatorSymbol(builtinOperatorSymbol.RoslynSymbol, Context);
                        }
                    }
                    else
                    {
                        operatorSymbol = new ExternSynthesizedOperatorSymbol(builtinOperatorSymbol.OperatorType, assignmentTarget.ValueType, Context);
                    }
                }

                return BoundInvocationExpression.CreateBoundInvocation(Context, node, operatorSymbol, null,
                    new[] { assignmentTarget, ConvertExpression(node, rhsExpression, operatorSymbol.Parameters[1].Type) });
            }
            else
            {
                // FieldSymbol or PropertySymbol
                Symbol operatorSymbol = GetSymbol(node.Left);

                BoundExpression lhsExpression;
                // Map to the left type this is actually being invoked on (most derived)
                if (node.Left is MemberAccessExpressionSyntax accessExpressionSyntax)
                    lhsExpression = VisitExpression(accessExpressionSyntax.Expression);
                else
                    lhsExpression = VisitExpression(node.Left);

                BoundExpression rhsExpression = VisitExpression(node.Right, assignmentTarget.ValueType);

                return new BoundAssignmentExpression(node, Context, operatorSymbol, assignmentTarget, lhsExpression, rhsExpression);
            }
        }

        public override BoundNode VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            BoundExpression conditionExpression =
                VisitExpression(node.Condition, Context.GetTypeSymbol(SpecialType.System_Boolean));

            TypeSymbol conditionResultType = GetTypeSymbol(node);
            
            BoundExpression trueExpression = VisitExpression(node.WhenTrue, conditionResultType);
            BoundExpression falseExpression = VisitExpression(node.WhenFalse, conditionResultType);

            return new BoundConditionalExpression(node, conditionResultType, conditionExpression, trueExpression, falseExpression);
        }

        public override BoundNode VisitCastExpression(CastExpressionSyntax node)
        {
            TypeSymbol castType = GetTypeSymbol(node.Type);

            return VisitExpression(node.Expression, castType, true);
        }

        private BoundExpression HandleShortCircuitOperator(BinaryExpressionSyntax node)
        {
            TypeSymbol booleanType = Context.GetTypeSymbol(SpecialType.System_Boolean);
            BoundExpression lhs = VisitExpression(node.Left, booleanType);
            BoundExpression rhs = VisitExpression(node.Right, booleanType);
            
            BoundExpression constantResult = ConstantExpressionOptimizer.FoldConstantBinaryExpression(Context, node, null, lhs, rhs);
            if (constantResult != null)
                return constantResult;

            return new BoundShortCircuitOperatorExpression(node,
                node.Kind() == SyntaxKind.LogicalAndExpression
                    ? BuiltinOperatorType.LogicalAnd
                    : BuiltinOperatorType.LogicalOr, lhs, rhs, Context);
        }

        private BoundExpression HandleNullCoalescingExpression(BinaryExpressionSyntax node)
        {
            TypeSymbol expressionResultType = GetTypeSymbol(node);
            BoundExpression lhs = VisitExpression(node.Left, expressionResultType);

            // Handling for C# 8.0 allowing null coalesce on generics that may be value types if we ever upgrade to C# 8 functionality
            if (lhs.ValueType.IsValueType)
                return lhs;

            BoundExpression rhs = VisitExpression(node.Right, expressionResultType);

            return new BoundCoalesceExpression(node, lhs, rhs);
        }

        private BoundExpression HandleNullEqualsExpression(BinaryExpressionSyntax node)
        {
            TypeSymbol booleanType = Context.GetTypeSymbol(SpecialType.System_Boolean);
            IConstantValue booleanValue;

            switch (node.Kind())
            {
                case SyntaxKind.EqualsExpression:
                    booleanValue = new ConstantValue<bool>(true);
                    break;
                case SyntaxKind.NotEqualsExpression:
                    booleanValue = new ConstantValue<bool>(false);
                    break;
                default:
                    throw new InvalidOperationException("Invalid equals expression");
            }

            return new BoundConstantExpression(booleanValue, booleanType, node);
        }
        
        public override BoundNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.Kind() == SyntaxKind.LogicalOrExpression ||
                node.Kind() == SyntaxKind.LogicalAndExpression)
                return HandleShortCircuitOperator(node);

            if (node.Kind() == SyntaxKind.CoalesceExpression)
                return HandleNullCoalescingExpression(node);

            MethodSymbol binaryMethodSymbol = (MethodSymbol)GetSymbol(node);

            if (binaryMethodSymbol == null &&
                (node.Kind() == SyntaxKind.EqualsExpression ||
                 node.Kind() == SyntaxKind.NotEqualsExpression))
                return HandleNullEqualsExpression(node);

            BoundExpression lhs = VisitExpression(node.Left, binaryMethodSymbol.Parameters[0].Type);
            BoundExpression rhs = VisitExpression(node.Right, binaryMethodSymbol.Parameters[1].Type);
            
            BoundExpression constantResult = ConstantExpressionOptimizer.FoldConstantBinaryExpression(Context, node, binaryMethodSymbol, lhs, rhs);
            if (constantResult != null)
                return constantResult;

            return BoundInvocationExpression.CreateBoundInvocation(Context, node, binaryMethodSymbol, null, new[] {lhs, rhs});
        }

        public override BoundNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            MethodSymbol unaryMethodSymbol = (MethodSymbol)GetSymbol(node);

            BoundExpression expression = VisitExpression(node.Operand, unaryMethodSymbol.Parameters[0].Type);

            // + operator is a no-op on builtins so ignore it until we allow user operator overloads
            if (node.OperatorToken.Kind() == SyntaxKind.PlusToken)
                return expression;
            
            BoundExpression constantResult = ConstantExpressionOptimizer.FoldConstantUnaryPrefixExpression(Context, node, unaryMethodSymbol, expression);
            if (constantResult != null)
                return constantResult;
            
            if (node.Kind() == SyntaxKind.PreIncrementExpression ||
                node.Kind() == SyntaxKind.PreDecrementExpression)
            {
                return new BoundInvocationExpression.BoundPrefixOperatorExpression(Context, node,
                    (BoundAccessExpression) expression, unaryMethodSymbol);
            }
            
            return BoundInvocationExpression.CreateBoundInvocation(Context, node, unaryMethodSymbol, null, new[] {expression});
        }

        public override BoundNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            MethodSymbol unaryMethodSymbol = (MethodSymbol)GetSymbol(node);

            return new BoundInvocationExpression.BoundPostfixOperatorExpression(Context, node,
                (BoundAccessExpression)VisitExpression(node.Operand, unaryMethodSymbol.Parameters[0].Type), unaryMethodSymbol);
        }

        public override BoundNode VisitBreakStatement(BreakStatementSyntax node)
        {
            return new BoundBreakStatement(node);
        }

        public override BoundNode VisitContinueStatement(ContinueStatementSyntax node)
        {
            return new BoundContinueStatement(node);
        }

        public override BoundNode VisitReturnStatement(ReturnStatementSyntax node)
        {
            BoundExpression returnExpression = node.Expression != null ? VisitExpression(node.Expression, Context.GetCurrentReturnType()) : null;
            return new BoundReturnStatement(node, returnExpression);
        }

        public override BoundNode VisitIfStatement(IfStatementSyntax node)
        {
            BoundExpression conditionExpression = VisitExpression(node.Condition, typeof(bool));

            // Eliminate the branch if it's over a constant value
            if (conditionExpression.IsConstant)
            {
                bool constantValue = ((ConstantValue<bool>)conditionExpression.ConstantValue).Value;

                if (constantValue)
                    return VisitStatement(node.Statement);
                
                return node.Else != null ? VisitStatement(node.Else) : new BoundBlock(node);
            }
            
            BoundStatement bodyStatement = VisitStatement(node.Statement);
            BoundStatement elseStatement = node.Else != null ? VisitStatement(node.Else) : null;

            // If the condition has a unary negation, just remove it and use the inner, then flip the conditional
            TypeSymbol boolTypeSymbol = Context.GetTypeSymbol(SpecialType.System_Boolean);
            while (conditionExpression is BoundInvocationExpression conditionInvocation && 
                   conditionInvocation.Method != null &&
                   conditionInvocation.Method.IsOperator && 
                   conditionInvocation.Method.Name == "op_LogicalNot" && 
                   conditionInvocation.Method.ReturnType == boolTypeSymbol)
            {
                conditionExpression = conditionInvocation.ParameterExpressions[0];
                (elseStatement, bodyStatement) = (bodyStatement, elseStatement);
            }

            return new BoundIfStatement(node, conditionExpression, bodyStatement, elseStatement);
        }

        public override BoundNode VisitElseClause(ElseClauseSyntax node)
        {
            return Visit(node.Statement);
        }

        public override BoundNode VisitSwitchStatement(SwitchStatementSyntax node)
        {
            TypeSymbol switchType = GetTypeSymbol(node.Expression);
            
            // Convert switches over enums to ints to prevent a ton of .Equals calls and allow easy jump table optimizations for enums
            if (switchType.IsEnum && switchType.IsExtern)
                switchType = Context.GetTypeSymbol(((INamedTypeSymbol)switchType.RoslynSymbol).EnumUnderlyingType);

            // If switch type is on object, we don't want to convert any of the case label values, and we don't need to cast string values
            if (switchType == Context.GetTypeSymbol(SpecialType.System_Object) || switchType == Context.GetTypeSymbol(SpecialType.System_String))
                switchType = null;
            
            BoundExpression switchExpression = VisitExpression(node.Expression, switchType);
            
            List<(List<BoundExpression>, List<BoundStatement>)> switchSectionList = new List<(List<BoundExpression>, List<BoundStatement>)>();

            int defaultSection = -1;
            
            for (int i = 0; i < node.Sections.Count; ++i)
            {
                var section = node.Sections[i];
                List<BoundExpression> boundLabels = new List<BoundExpression>();
                foreach (SwitchLabelSyntax sectionLabel in section.Labels)
                {
                    if (sectionLabel is CaseSwitchLabelSyntax caseSwitchLabelSyntax)
                    {
                        BoundExpression labelExpression = VisitExpression(caseSwitchLabelSyntax.Value, switchType);
                        if (!labelExpression.IsConstant)
                            throw new CompilerException("Switch label is not a constant value");
                        
                        boundLabels.Add(labelExpression);
                    }
                    else if (sectionLabel is DefaultSwitchLabelSyntax)
                        defaultSection = i;
                }

                List<BoundStatement> statements = new List<BoundStatement>();
                foreach (StatementSyntax statement in section.Statements)
                    statements.Add(VisitStatement(statement));
                
                switchSectionList.Add((boundLabels, statements));
            }

            return new BoundSwitchStatement(node, switchExpression, switchSectionList, defaultSection);
        }

        public override BoundNode VisitForStatement(ForStatementSyntax node)
        {
            BoundVariableDeclarationStatement declaration = null;
            if (node.Declaration != null)
                declaration = (BoundVariableDeclarationStatement)VisitVariableDeclaration(node.Declaration);

            BoundExpression[] initializers = new BoundExpression[node.Initializers.Count];

            for (int i = 0; i < initializers.Length; ++i)
                initializers[i] = VisitExpression(node.Initializers[i]);

            BoundExpression conditionExpression = null;
            if (node.Condition != null)
                conditionExpression = VisitExpression(node.Condition, typeof(bool));

            var incrementors = new BoundExpression[node.Incrementors.Count];
            for (int i = 0; i < incrementors.Length; ++i)
                incrementors[i] = VisitExpression(node.Incrementors[i]);

            var body = VisitStatement(node.Statement);

            return new BoundForStatement(node, declaration, initializers, conditionExpression, incrementors, body);
        }

        public override BoundNode VisitWhileStatement(WhileStatementSyntax node)
        {
            return new BoundWhileStatement(node, VisitExpression(node.Condition, typeof(bool)), VisitStatement(node.Statement));
        }

        public override BoundNode VisitDoStatement(DoStatementSyntax node)
        {
            return new BoundDoStatement(node, VisitExpression(node.Condition, typeof(bool)), VisitStatement(node.Statement));
        }

        public override BoundNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            BoundExpression iteratorExpression = VisitExpression(node.Expression);
            Symbol iteratorVal = GetDeclaredSymbol(node);
            BoundStatement foreachStatement = VisitStatement(node.Statement);

            if (iteratorExpression.ValueType == Context.GetTypeSymbol(SpecialType.System_String))
                return new BoundForEachCharStatement(node, iteratorExpression, iteratorVal, foreachStatement);

            if (iteratorExpression.ValueType == Context.GetTypeSymbol(typeof(Transform)))
                return new BoundForEachChildTransformStatement(node, iteratorExpression, iteratorVal, foreachStatement);

            return new BoundForEachStatement(node, iteratorExpression, iteratorVal, foreachStatement);
        }

        public override BoundNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (node.Initializer != null)
                throw new NotSupportedException(LocStr.CE_InitializerListsNotSupported, node);

            ITypeSymbol createdType = SymbolLookupModel.GetTypeInfo(node).Type;
            string violation = GenericRestrictionPolicy.GetViolation(createdType, GenericUseSite.ObjectCreation);
            if (violation != null)
                throw new CompilerException(violation, node.GetLocation());

            MethodSymbol constructorSymbol = (MethodSymbol)GetSymbol(node);

            BoundExpression[] boundArguments = new BoundExpression[node.ArgumentList.Arguments.Count];

            bool isConstant = true;

            for (int i = 0; i < boundArguments.Length; ++i)
            {
                boundArguments[i] = VisitExpression(node.ArgumentList.Arguments[i].Expression, constructorSymbol.Parameters[i].Type);
                isConstant &= boundArguments[i].IsConstant;
            }
            
            // Constant folding on struct creation when possible
            // Also implicitly handles parameterless constructors on value types, which Udon does not expose constructors for
            if (isConstant && constructorSymbol.IsExtern && constructorSymbol.ContainingType.IsValueType)
            {
                var constArgs = boundArguments.Select(e => e.ConstantValue.Value).ToArray();
                
                object constantValue = Activator.CreateInstance(constructorSymbol.ContainingType.UdonType.SystemType, constArgs);

                IConstantValue constantStore = (IConstantValue)Activator.CreateInstance(typeof(ConstantValue<>).MakeGenericType(constantValue.GetType()), constantValue);

                return new BoundConstantExpression(constantStore, constructorSymbol.ContainingType, node);
            }

            return BoundInvocationExpression.CreateBoundInvocation(Context, node, constructorSymbol, null, boundArguments);
        }

        public override BoundNode VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            if (node.Type.RankSpecifiers[0].Sizes.Count != 1)
                throw new NotSupportedException(
                    "Multidimensional arrays are not yet supported by UdonSharp, consider using jagged arrays instead.",
                    node.GetLocation());
            
            // Almost certainly needs to be revisited for jagged arrays
            TypeSymbol arrayType = GetTypeSymbol(node.Type);

            BoundExpression[] elementCounts = new BoundExpression[1];
            var initializerExpressions = node.Initializer?.Expressions ?? new SeparatedSyntaxList<ExpressionSyntax>();

            if (node.Type.RankSpecifiers[0].Sizes[0] is OmittedArraySizeExpressionSyntax)
                elementCounts[0] = new BoundConstantExpression(new ConstantValue<int>(initializerExpressions.Count), Context.GetTypeSymbol(SpecialType.System_Int32), node);
            else
                elementCounts[0] = VisitExpression(node.Type.RankSpecifiers[0].Sizes[0], Context.GetTypeSymbol(SpecialType.System_Int32));

            BoundExpression[] initializers = null;

            if (node.Initializer != null)
            {
                TypeSymbol elementType = arrayType.ElementType;
                initializers = new BoundExpression[initializerExpressions.Count];

                for (int i = 0; i < initializers.Length; ++i)
                    initializers[i] = VisitExpression(initializerExpressions[i], elementType);
            }
            
            return new BoundArrayCreationExpression(node, Context, arrayType, elementCounts, initializers);
        }

        public override BoundNode VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            if (node.Commas.Count != 0)
                throw new NotSupportedException(
                    "Multidimensional arrays are not yet supported by UdonSharp, consider using jagged arrays instead.",
                    node.GetLocation());
            
            // Almost certainly needs to be revisited for jagged arrays
            TypeSymbol arrayType = GetTypeSymbol(node);

            var initializerExpressions = node.Initializer.Expressions;
            BoundExpression[] elementCounts = {new BoundConstantExpression(new ConstantValue<int>(initializerExpressions.Count), Context.GetTypeSymbol(SpecialType.System_Int32), node)};
            BoundExpression[] initializers = new BoundExpression[initializerExpressions.Count];

            TypeSymbol elementType = arrayType.ElementType;

            for (int i = 0; i < initializers.Length; ++i)
                initializers[i] = VisitExpression(initializerExpressions[i], elementType);
            
            return new BoundArrayCreationExpression(node, Context, arrayType, elementCounts, initializers);
        }

        public override BoundNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            BoundExpression accessExpression = VisitExpression(node.Expression);

            // if (node.ArgumentList.Arguments.Count != 1)
            //     throw new NotSupportedException("UdonSharp does not currently support multidimensional arrays", node.GetLocation());

            if (accessExpression.ValueType == Context.GetTypeSymbol(SpecialType.System_String))
                return new BoundStringAccessExpression(Context, node, accessExpression, VisitExpression(node.ArgumentList.Arguments[0].Expression, Context.GetTypeSymbol(SpecialType.System_Int32)));
            
            PropertySymbol accessorSymbol = GetSymbol(node) as PropertySymbol;
            
            BoundExpression[] indexers = new BoundExpression[node.ArgumentList.Arguments.Count];
            
            // There's some extern/user defined indexer, so use that
            if (accessorSymbol != null)
            {
                for (int i = 0; i < indexers.Length; ++i)
                    indexers[i] = VisitExpression(node.ArgumentList.Arguments[i].Expression, accessorSymbol.Parameters[i].Type);
                
                return BoundAccessExpression.BindElementAccess(Context, node, accessorSymbol, accessExpression, indexers);
            }

            for (int i = 0; i < indexers.Length; ++i)
                indexers[i] = VisitExpression(node.ArgumentList.Arguments[i].Expression, typeof(int));

            return BoundAccessExpression.BindElementAccess(Context, node, accessExpression, indexers);
        }

        public override BoundNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            List<BoundExpression> interpolationExpressions = new List<BoundExpression>();

            StringBuilder interpolationStr = new StringBuilder();
            
            foreach (var interpolationNode in node.Contents)
            {
                if (interpolationNode is InterpolatedStringTextSyntax stringContent)
                {
                    interpolationStr.Append(stringContent.TextToken.ValueText);
                }
                else if (interpolationNode is InterpolationSyntax interpolatedExpression)
                {
                    interpolationStr.Append("{");
                    interpolationStr.Append(interpolationExpressions.Count);
                    
                    interpolationExpressions.Add(VisitExpression(interpolatedExpression.Expression));

                    if (interpolatedExpression.AlignmentClause != null)
                    {
                        interpolationStr.Append(",");

                        if (!(VisitExpression(interpolatedExpression.AlignmentClause.Value) is BoundConstantExpression constantExpression))
                            throw new NotSupportedException("Alignment clause must be a constant expression", node.GetLocation());

                        interpolationStr.Append(constantExpression.ConstantValue.Value);
                    }

                    if (interpolatedExpression.FormatClause != null)
                    {
                        interpolationStr.Append(":");
                        interpolationStr.Append(interpolatedExpression.FormatClause.FormatStringToken.ValueText);
                    }

                    interpolationStr.Append("}");
                }
            }

            if (interpolationExpressions.Count == 0)
                return new BoundConstantExpression(new ConstantValue<string>(string.Format(interpolationStr.ToString())),
                    Context.GetTypeSymbol(SpecialType.System_String), node);

            return new BoundInterpolatedStringExpression(node, interpolationStr.ToString(), interpolationExpressions.ToArray(), Context);
        }
    }
}
