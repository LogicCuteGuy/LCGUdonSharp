using System;
using Microsoft.CodeAnalysis;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Core;
using UdonSharp.Serialization;

namespace UdonSharp.Compiler.Binder
{
    internal sealed class BoundScriptableObjectTypeExpression : BoundExpression
    {
        private readonly TypeSymbol targetType;
        private readonly bool isTest;
        private readonly bool throwOnFailure;
        private readonly BoundAccessExpression designation;
        public override TypeSymbol ValueType { get; }

        public BoundScriptableObjectTypeExpression(AbstractPhaseContext context, SyntaxNode node,
            BoundExpression source, TypeSymbol target, bool isTest, bool throwOnFailure = false,
            BoundAccessExpression designation = null) : base(node, source)
        {
            if (!source.ValueType.IsScriptableObjectData || !target.IsScriptableObjectData)
                throw new CompilerException("ScriptableObject type tests and casts require custom data types on both sides.", node?.GetLocation());
            targetType = target;
            this.isTest = isTest;
            this.throwOnFailure = throwOnFailure;
            this.designation = designation;
            ValueType = isTest ? context.GetTypeSymbol(SpecialType.System_Boolean) : target;
        }

        public override Value EmitValue(EmitContext context)
        {
            using (context.InterruptAssignmentScope())
            {
                // Evaluate the receiver exactly once, including method/array receivers.
                var source = context.EmitValue(SourceExpression);
                var result = context.CreateInternalValue(ValueType);
                context.Module.AddCopy(context.GetConstantValue(ValueType, isTest ? (object)false : null), result);
                var end = context.Module.CreateLabel();
                var nonNull = context.Module.CreateLabel();
                var objectType = context.GetTypeSymbol(SpecialType.System_Object);
                var boolType = context.GetTypeSymbol(SpecialType.System_Boolean);
                var equals = new ExternSynthesizedMethodSymbol(context,
                    "SystemObject.__Equals__SystemObject_SystemObject__SystemBoolean",
                    new[] { objectType, objectType }, boolType, true);
                var isNull = context.EmitValue(BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode,
                    equals, null, new[] { BoundAccessExpression.BindAccess(source),
                        BoundAccessExpression.BindAccess(context.GetConstantValue(objectType, null)) }));
                context.Module.AddJumpIfFalse(nonNull, isNull);
                context.Module.AddJump(end);
                context.Module.LabelJump(nonNull);

                var get = new ExternSynthesizedMethodSymbol(context,
                    "SystemObjectArray.__Get__SystemInt32__SystemObject",
                    new[] { context.GetTypeSymbol(SpecialType.System_Int32) }, objectType, false);
                var tag = context.EmitValue(BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode,
                    get, BoundAccessExpression.BindAccess(source), new[] {
                        BoundAccessExpression.BindAccess(context.GetConstantValue(context.GetTypeSymbol(SpecialType.System_Int32), 0)) }));
                targetType.TryGetSystemType(out Type systemType);
                var stringType = context.GetTypeSymbol(SpecialType.System_String);
                var contains = new ExternSynthesizedMethodSymbol(context,
                    "SystemString.__Contains__SystemString__SystemBoolean", new[] { stringType }, boolType, false);
                var matches = context.EmitValue(BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode,
                    contains, BoundAccessExpression.BindAccess(context.CastValue(tag, stringType, true)), new[] {
                        BoundAccessExpression.BindAccess(context.GetConstantValue(stringType, ScriptableObjectDataLayout.GetTypeToken(systemType))) }));
                if (isTest)
                {
                    context.Module.AddCopy(matches, result);
                    if (designation != null)
                    {
                        context.Module.AddJumpIfFalse(end, matches);
                        context.EmitSet(designation, BoundAccessExpression.BindAccess(context.CastValue(source, targetType, true)));
                    }
                }
                else
                {
                    var failed = context.Module.CreateLabel();
                    context.Module.AddJumpIfFalse(failed, matches);
                    context.Module.AddCopy(source, result);
                    context.Module.AddJump(end);
                    context.Module.LabelJump(failed);
                    if (throwOnFailure)
                    {
                        context.SetExceptionState((int)UdonExceptionKind.InvalidCast,
                            context.GetConstantValue(stringType, "ScriptableObject snapshot is not compatible with " + systemType.FullName),
                            context.GetConstantValue(context.GetTypeSymbol(SpecialType.System_Int32), 0),
                            context.GetConstantValue(stringType, "ScriptableObject-cast"));
                        context.EmitExceptionPropagation();
                    }
                }
                context.Module.LabelJump(end);
                return result;
            }
        }
    }
}
