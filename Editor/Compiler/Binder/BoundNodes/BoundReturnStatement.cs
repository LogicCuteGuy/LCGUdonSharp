
using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Emit;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundReturnStatement : BoundStatement
    {
        private BoundExpression ReturnExpression { get; }
        
        public BoundReturnStatement(SyntaxNode node, BoundExpression returnExpression)
            :base(node)
        {
            ReturnExpression = returnExpression;
        }

        public override void Emit(EmitContext context)
        {
            context.EmitReturn(ReturnExpression);
        }
    }
}
