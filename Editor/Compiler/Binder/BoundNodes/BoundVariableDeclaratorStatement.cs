
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UdonSharp.LCGCompiler.Emit;
using UdonSharp.LCGCompiler.Symbols;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundVariableDeclaratorStatement : BoundStatement
    {
        public Symbol UserSymbol { get; }
        public BoundExpression Initializer { get; }

        public BoundVariableDeclaratorStatement(VariableDeclaratorSyntax node, Symbol userSymbol, BoundExpression initializer)
            : base(node)
        {
            UserSymbol = userSymbol;
            Initializer = initializer;
        }

        public override void Emit(EmitContext context)
        {
            if (UserSymbol is LocalSymbol localSymbol && localSymbol.IsConst)
                return;

            Value userValue = context.GetUserValue(UserSymbol);

            if (Initializer == null) return;
            
            context.EmitValueAssignment(userValue, Initializer);
        }
    }
}
