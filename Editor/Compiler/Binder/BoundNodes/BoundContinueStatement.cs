using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Emit;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundContinueStatement : BoundStatement
    {
        public BoundContinueStatement(SyntaxNode node)
            :base(node)
        {
        }

        public override void Emit(EmitContext context)
        {
            context.EmitContinue();
        }
    }
}
