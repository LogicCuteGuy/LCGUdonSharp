
using System;
using Microsoft.CodeAnalysis;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;

namespace UdonSharp.Compiler.Binder
{
    internal sealed class BoundCastExpression : BoundExpression
    {
        private TypeSymbol TargetType { get; }
        
        private bool IsExplicit { get; }

        public override TypeSymbol ValueType => TargetType;

        public BoundCastExpression(SyntaxNode node, BoundExpression sourceExpression, TypeSymbol targetType, bool isExplicit)
            : base(node, sourceExpression)
        {
            if (targetType is TypeParameterSymbol)
                throw new InvalidOperationException("Cannot cast to generic parameter types");

            bool sourceData = sourceExpression.ValueType.IsScriptableObjectData ||
                (sourceExpression.ValueType.IsArray && sourceExpression.ValueType.ElementType.IsScriptableObjectData);
            bool targetData = targetType.IsScriptableObjectData ||
                (targetType.IsArray && targetType.ElementType.IsScriptableObjectData);
            if ((sourceData || targetData) && sourceExpression.ValueType != targetType &&
                !(sourceExpression.IsConstant && sourceExpression.ConstantValue.Value == null))
                throw new UdonSharp.Core.CompilerException("ScriptableObject data casts and polymorphic references are not supported. Keep the exact asset type.", node?.GetLocation());
            
            TargetType = targetType;
            IsExplicit = isExplicit;
        }

        public override Value EmitValue(EmitContext context)
        {
            return context.CastValue(context.EmitValue(SourceExpression), TargetType, IsExplicit);
        }
    }
}
