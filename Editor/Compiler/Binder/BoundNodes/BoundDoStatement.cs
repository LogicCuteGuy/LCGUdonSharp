
using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Assembly;
using UdonSharp.LCGCompiler.Emit;

namespace UdonSharp.LCGCompiler.Binder
{
    internal sealed class BoundDoStatement : BoundStatement
    {
        private BoundExpression Condition { get; }
        private BoundStatement Body { get; }
        
        public BoundDoStatement(SyntaxNode node, BoundExpression condition, BoundStatement body)
            :base(node)
        {
            Condition = condition;
            Body = body;
        }

        public override void Emit(EmitContext context)
        {
            JumpLabel continueLabel = context.PushContinueLabel();
            JumpLabel breakLabel = context.PushBreakLabel();
            
            context.Module.LabelJump(continueLabel);

            context.Emit(Body);
            
            context.Module.AddJumpIfFalse(breakLabel, context.EmitValue(Condition));

            context.Module.AddJump(continueLabel);

            context.Module.LabelJump(breakLabel);
            
            context.PopBreakLabel();
            context.PopContinueLabel();
        }
    }
}
