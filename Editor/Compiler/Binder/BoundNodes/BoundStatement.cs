
using Microsoft.CodeAnalysis;

namespace UdonSharp.LCGCompiler.Binder
{
    internal abstract class BoundStatement : BoundNode
    {
        protected BoundStatement(SyntaxNode node)
            :base(node)
        {
            
        }
    }
}
