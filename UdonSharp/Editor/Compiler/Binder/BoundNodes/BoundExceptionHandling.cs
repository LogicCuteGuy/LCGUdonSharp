using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Core;

namespace UdonSharp.Compiler.Binder
{
    internal enum ExceptionPayloadMember
    {
        Message,
        Kind,
        Code,
        Operation,
    }

    internal sealed class BoundCatchClause
    {
        public SyntaxNode SyntaxNode { get; }
        public BoundStatement Body { get; set; }
        public int[] MatchingKinds { get; }
        public bool CatchesAll => MatchingKinds == null;

        private Value _kind;
        private Value _code;
        private Value _message;
        private Value _operation;

        public BoundCatchClause(SyntaxNode syntaxNode, int[] matchingKinds)
        {
            SyntaxNode = syntaxNode;
            MatchingKinds = matchingKinds;
        }

        public void Snapshot(EmitContext context)
        {
            _kind = context.CreateInternalValue(context.GetTypeSymbol(SpecialType.System_Int32));
            _code = context.CreateInternalValue(context.GetTypeSymbol(SpecialType.System_Int32));
            _message = context.CreateInternalValue(context.GetTypeSymbol(SpecialType.System_String));
            _operation = context.CreateInternalValue(context.GetTypeSymbol(SpecialType.System_String));

            context.Module.AddCopy(context.ExceptionKindValue, _kind);
            context.Module.AddCopy(context.ExceptionCodeValue, _code);
            context.Module.AddCopy(context.ExceptionMessageValue, _message);
            context.Module.AddCopy(context.ExceptionOperationValue, _operation);
            context.ClearExceptionState();
        }

        public Value GetPayloadValue(ExceptionPayloadMember member)
        {
            switch (member)
            {
                case ExceptionPayloadMember.Message: return _message;
                case ExceptionPayloadMember.Kind: return _kind;
                case ExceptionPayloadMember.Code: return _code;
                case ExceptionPayloadMember.Operation: return _operation;
                default: throw new ArgumentOutOfRangeException(nameof(member));
            }
        }

        public void Restore(EmitContext context)
        {
            context.Module.AddCopy(context.GetConstantValue(context.GetTypeSymbol(SpecialType.System_Boolean), true), context.ExceptionPendingValue);
            context.Module.AddCopy(_kind, context.ExceptionKindValue);
            context.Module.AddCopy(_code, context.ExceptionCodeValue);
            context.Module.AddCopy(_message, context.ExceptionMessageValue);
            context.Module.AddCopy(_operation, context.ExceptionOperationValue);
        }
    }

    internal sealed class BoundExceptionPayloadAccessExpression : BoundAccessExpression
    {
        private readonly BoundCatchClause _catchClause;
        private readonly ExceptionPayloadMember _member;
        private readonly TypeSymbol _valueType;

        public BoundExceptionPayloadAccessExpression(SyntaxNode node, BoundCatchClause catchClause,
            ExceptionPayloadMember member, TypeSymbol valueType) : base(node, null)
        {
            _catchClause = catchClause;
            _member = member;
            _valueType = valueType;
        }

        public override TypeSymbol ValueType => _valueType;
        public override Value EmitValue(EmitContext context)
        {
            Value payloadValue = _catchClause.GetPayloadValue(_member);
            if (_member != ExceptionPayloadMember.Kind)
                return payloadValue;

            Value typedKind = context.CreateInternalValue(_valueType);
            context.Module.AddCopy(payloadValue, typedKind);
            return typedKind;
        }

        public override Value EmitSet(EmitContext context, BoundExpression valueExpression)
        {
            throw new CompilerException("Catch variables are read-only compiler pseudo-values.", SyntaxNode.GetLocation());
        }
    }

    internal sealed class BoundThrowStatement : BoundStatement
    {
        private readonly int _kind;
        private readonly BoundExpression _message;
        private readonly BoundExpression _code;
        private readonly BoundExpression _operation;
        private readonly BoundCatchClause _rethrowSource;

        public BoundThrowStatement(SyntaxNode node, int kind, BoundExpression message, BoundExpression code,
            BoundExpression operation) : base(node)
        {
            _kind = kind;
            _message = message;
            _code = code;
            _operation = operation;
        }

        public BoundThrowStatement(SyntaxNode node, BoundCatchClause rethrowSource) : base(node)
        {
            _rethrowSource = rethrowSource;
        }

        public override void Emit(EmitContext context)
        {
            if (_rethrowSource != null)
            {
                _rethrowSource.Restore(context);
            }
            else
            {
                context.SetExceptionState(
                    _kind,
                    context.EmitValue(_message),
                    context.EmitValue(_code),
                    context.EmitValue(_operation));
            }

            context.EmitExceptionPropagation();
        }
    }

    internal sealed class BoundTryStatement : BoundStatement
    {
        private readonly BoundStatement _tryBody;
        private readonly IReadOnlyList<BoundCatchClause> _catches;
        private readonly BoundStatement _finallyBody;

        public BoundTryStatement(SyntaxNode node, BoundStatement tryBody, IReadOnlyList<BoundCatchClause> catches,
            BoundStatement finallyBody) : base(node)
        {
            _tryBody = tryBody;
            _catches = catches;
            _finallyBody = finallyBody;
        }

        private static Value EmitKindEquals(EmitContext context, int kind)
        {
            TypeSymbol intType = context.GetTypeSymbol(SpecialType.System_Int32);
            return context.EmitValue(BoundInvocationExpression.CreateBoundInvocation(context, null,
                new ExternSynthesizedOperatorSymbol(BuiltinOperatorType.Equality, intType, context), null,
                new BoundExpression[]
                {
                    BoundAccessExpression.BindAccess(context.ExceptionKindValue),
                    BoundAccessExpression.BindAccess(context.GetConstantValue(intType, kind)),
                }));
        }

        private void EmitFinally(EmitContext context)
        {
            if (_finallyBody != null)
            {
                context.EnterGuardedRegion();
                context.Emit(_finallyBody);
                context.ExitGuardedRegion();
            }
        }

        public override void Emit(EmitContext context)
        {
            JumpLabel dispatchLabel = context.Module.CreateLabel();
            JumpLabel endLabel = context.Module.CreateLabel();

            if (_finallyBody != null)
                context.PushFinally(_finallyBody);
            context.PushExceptionHandler(dispatchLabel);
            context.EnterGuardedRegion();
            context.Emit(_tryBody);
            context.ExitGuardedRegion();
            context.PopExceptionHandler();
            if (_finallyBody != null)
                context.PopFinally();

            EmitFinally(context);
            context.Module.AddJump(endLabel);
            context.Module.LabelJump(dispatchLabel);

            foreach (BoundCatchClause catchClause in _catches)
            {
                JumpLabel nextCatch = context.Module.CreateLabel();
                JumpLabel matchedCatch = context.Module.CreateLabel();

                if (!catchClause.CatchesAll)
                {
                    foreach (int kind in catchClause.MatchingKinds)
                    {
                        JumpLabel failedKind = context.Module.CreateLabel();
                        context.Module.AddJumpIfFalse(failedKind, EmitKindEquals(context, kind));
                        context.Module.AddJump(matchedCatch);
                        context.Module.LabelJump(failedKind);
                    }

                    context.Module.AddJump(nextCatch);
                    context.Module.LabelJump(matchedCatch);
                }

                catchClause.Snapshot(context);
                if (_finallyBody != null)
                    context.PushFinally(_finallyBody);
                context.EnterGuardedRegion();
                context.Emit(catchClause.Body);
                context.ExitGuardedRegion();
                if (_finallyBody != null)
                    context.PopFinally();
                EmitFinally(context);
                context.Module.AddJump(endLabel);
                context.Module.LabelJump(nextCatch);
            }

            if (_finallyBody != null)
            {
                context.PushFinally(_finallyBody);
                context.EmitExceptionPropagation();
                context.PopFinally();
            }
            else
            {
                context.EmitExceptionPropagation();
            }

            context.Module.LabelJump(endLabel);
        }
    }
}
