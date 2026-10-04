
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

        public BoundCastExpression(SyntaxNode node, BoundExpression sourceExpression, TypeSymbol targetType, bool isExplicit, bool isCompilerGenerated = false)
            : base(node, sourceExpression)
        {
            if (targetType is TypeParameterSymbol)
                throw new InvalidOperationException("Cannot cast to generic parameter types");

            bool sourceData = sourceExpression.ValueType.IsScriptableObjectData ||
                (sourceExpression.ValueType.IsArray && sourceExpression.ValueType.ElementType.IsScriptableObjectData);
            bool targetData = targetType.IsScriptableObjectData ||
                (targetType.IsArray && targetType.ElementType.IsScriptableObjectData);
            if (!isCompilerGenerated && (sourceData || targetData) && sourceExpression.ValueType != targetType &&
                !(sourceExpression.IsConstant && sourceExpression.ConstantValue.Value == null) &&
                !(sourceExpression.ValueType.IsScriptableObjectData && targetType.IsScriptableObjectData))
                throw new UdonSharp.Core.CompilerException($"ScriptableObject data casts require custom data types; casts to object/native assets and array covariance are not supported ({sourceExpression.ValueType} -> {targetType}).", node?.GetLocation());
            
            TargetType = targetType;
            IsExplicit = isExplicit;
        }

        public override Value EmitValue(EmitContext context)
        {
            if (SourceExpression.ValueType.IsScriptableObjectData && TargetType.IsScriptableObjectData &&
                !context.CompileContext.RoslynCompilation.ClassifyConversion(SourceExpression.ValueType.RoslynSymbol,
                    TargetType.RoslynSymbol).IsImplicit)
                return context.EmitValue(new BoundScriptableObjectTypeExpression(context, SyntaxNode,
                    SourceExpression, TargetType, false, true));
            return context.CastValue(context.EmitValue(SourceExpression), TargetType, IsExplicit);
        }
    }
}
