using System;
using Microsoft.CodeAnalysis;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Core;
using UdonSharp.Serialization;

namespace UdonSharp.Compiler.Binder
{
    internal sealed class BoundScriptableObjectFieldAccessExpression : BoundFieldAccessExpression
    {
        private readonly FieldSymbol field;
        private readonly int index;
        public override TypeSymbol ValueType => field.Type;

        public BoundScriptableObjectFieldAccessExpression(AbstractPhaseContext context, SyntaxNode node,
            FieldSymbol field, BoundExpression source) : base(node, source)
        {
            this.field = field;
            if (source == null || !source.ValueType.TryGetSystemType(out Type type))
                throw new CompilerException("Cannot resolve the ScriptableObject data asset type.", node?.GetLocation());
            try
            {
                var fields = ScriptableObjectDataLayout.GetFields(type);
                field.ContainingType.TryGetSystemType(out Type declaringType);
                index = Array.FindIndex(fields, f => f.Name == field.Name && f.DeclaringType == declaringType);
            }
            catch (System.NotSupportedException exception)
            {
                throw new CompilerException(exception.Message, node?.GetLocation());
            }
            if (index < 0)
                throw new CompilerException($"ScriptableObject field '{field.Name}' is not a serialized data field.", node?.GetLocation());
        }

        public override Value EmitSet(EmitContext context, BoundExpression valueExpression) =>
            throw new CompilerException($"ScriptableObject data field '{field.Name}' is read-only. Copy it into gameplay state before modifying it.", SyntaxNode?.GetLocation());

        public override Value EmitValue(EmitContext context)
        {
            using (context.InterruptAssignmentScope())
            {
                var source = context.EmitValue(SourceExpression);
                context.EmitNullGuard(source, $"ScriptableObject:{field.Name}");
                var objectType = context.GetTypeSymbol(SpecialType.System_Object);
                var get = new ExternSynthesizedMethodSymbol(context,
                    "SystemObjectArray.__Get__SystemInt32__SystemObject",
                    new[] { context.GetTypeSymbol(SpecialType.System_Int32) }, objectType, false);
                var invocation = BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode, get,
                    BindAccess(source), new BoundExpression[] {
                        BindAccess(context.GetConstantValue(context.GetTypeSymbol(SpecialType.System_Int32), index + 1)) });
                var value = context.CastValue(context.EmitValue(invocation), field.Type, true);
                if (!field.Type.IsArray) return value;

                // Return a copy, so modifying an array through a local alias cannot
                // mutate the baked snapshot. Null arrays remain null.
                var equals = new ExternSynthesizedMethodSymbol(context,
                    "SystemObject.__Equals__SystemObject_SystemObject__SystemBoolean",
                    new[] { objectType, objectType }, context.GetTypeSymbol(SpecialType.System_Boolean), true);
                var isNull = BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode, equals, null,
                    new BoundExpression[] { BindAccess(value), BindAccess(context.GetConstantValue(objectType, null)) });
                var clone = new ExternSynthesizedMethodSymbol(context, "SystemArray.__Clone__SystemObject",
                    Array.Empty<TypeSymbol>(), objectType, false);
                var cloneCall = BoundInvocationExpression.CreateBoundInvocation(context, SyntaxNode, clone,
                    BindAccess(value), Array.Empty<BoundExpression>());
                return context.EmitValue(new BoundConditionalExpression(null, field.Type, isNull,
                    BindAccess(value), new BoundCastExpression(SyntaxNode, cloneCall, field.Type, true, isCompilerGenerated: true)));
            }
        }
    }
}
