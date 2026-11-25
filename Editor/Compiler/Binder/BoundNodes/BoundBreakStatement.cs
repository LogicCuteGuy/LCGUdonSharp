using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Emit;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundBreakStatement : BoundStatement
    {
        public BoundBreakStatement(SyntaxNode node)
            :base(node)
        {
        }

        public override void Emit(EmitContext context)
        {
            context.EmitBreak();
        }
    }
}
