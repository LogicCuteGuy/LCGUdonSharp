
using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Symbols;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundStaticUserMethodInvocation : BoundUserMethodInvocationExpression
    {
        public BoundStaticUserMethodInvocation(SyntaxNode node, MethodSymbol method, BoundExpression[] parameterExpressions)
            :base(node, method, null, parameterExpressions)
        {
        }
    }
}
