
using System;
using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Assembly;
using UdonSharp.LCGCompiler.Emit;
using UdonSharp.LCGCompiler.Symbols;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundBitwiseNotExpression : BoundInvocationExpression
    {
        public override TypeSymbol ValueType => SourceExpression.ValueType;

        public BoundBitwiseNotExpression(SyntaxNode node, BoundExpression sourceExpression)
            : base(node, null, sourceExpression, new BoundExpression[] {})
        {
        }

        public override Value EmitValue(EmitContext context)
        {
            Type operandType = ValueType.UdonType.SystemType;

            object allBits;

            if (UdonSharpUtils.IsSignedType(operandType))
            {
                allBits = Convert.ChangeType(-1, operandType);
            } else
            {
                allBits = operandType.GetField("MaxValue").GetValue(null);
            }

            BoundAccessExpression allBitsExpr = BoundAccessExpression.BindAccess(context.GetConstantValue(ValueType, allBits));

            Value returnValue = context.GetReturnValue(ValueType);

            using (context.InterruptAssignmentScope())
            {
                BoundAccessExpression operandVal = BoundAccessExpression.BindAccess(context.EmitValue(SourceExpression));
                
                context.EmitValueAssignment(returnValue,
                    CreateBoundInvocation(context, null,
                        new ExternSynthesizedOperatorSymbol(BuiltinOperatorType.LogicalXor, ValueType, context), null,
                        new BoundExpression[] { operandVal, allBitsExpr }));
            }

            return returnValue;
        }
    }
}
