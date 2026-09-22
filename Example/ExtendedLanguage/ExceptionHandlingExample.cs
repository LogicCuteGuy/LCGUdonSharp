using System;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ExceptionHandlingExample : UdonSharpBehaviour
    {
        private const string LogPrefix = "[try/catch] ";

        public int result;
        public int finallyCount;
        public string message;
        public int code;
        public string operation;
        public UdonExceptionKind kind;
        public bool argumentCaught;
        public bool arrayCaught;
        public bool stringCaught;
        public bool divideCaught;
        public bool moduloCaught;
        public bool nullCaught;
        public bool negativeWriteCaught;
        public bool rethrowCaught;
        public bool finallyOverrideCaught;
        public int sideEffectCount;

        public override void Interact()
        {
            result = 0;
            finallyCount = 0;
            sideEffectCount = 0;
            argumentCaught = false;
            arrayCaught = false;
            stringCaught = false;
            divideCaught = false;
            moduloCaught = false;
            nullCaught = false;
            negativeWriteCaught = false;
            rethrowCaught = false;
            finallyOverrideCaught = false;

            try
            {
                ThrowArgumentNull();
                result = -1;
            }
            catch (ArgumentException exception)
            {
                message = exception.Message;
                argumentCaught = true;
                result = 10;
                Debug.Log(LogPrefix + "Caught ArgumentException: " + exception.Message);
            }
            finally
            {
                finallyCount++;
                result++;
                Debug.Log(LogPrefix + "The first finally block ran.");
            }

            try
            {
                throw new UdonException(message: "explicit payload", kind: UdonExceptionKind.InvalidOperation,
                    operation: "demo", code: 17);
            }
            catch (UdonException exception)
            {
                message = exception.Message;
                kind = exception.Kind;
                code = exception.Code;
                operation = exception.Operation;
                result += exception.Code;
                Debug.Log(LogPrefix + "Caught UdonException: " + exception.Message);
                Debug.Log(LogPrefix + "Kind=" + (int)exception.Kind + ", Code=" + exception.Code +
                    ", Operation=" + exception.Operation);
            }

            int[] values = { 1 };
            try { result += values[2]; }
            catch (IndexOutOfRangeException)
            {
                arrayCaught = true;
                result += 100;
                Debug.Log(LogPrefix + "Caught an array upper-bound read.");
            }

            string text = "ok";
            try { result += text[3]; }
            catch (IndexOutOfRangeException)
            {
                stringCaught = true;
                result += 1000;
                Debug.Log(LogPrefix + "Caught a string upper-bound read.");
            }

            int divisor = 0;
            try { result += 10 / divisor; }
            catch (DivideByZeroException)
            {
                divideCaught = true;
                result += 10000;
                Debug.Log(LogPrefix + "Caught integral division by zero.");
            }

            try { result += 10 % divisor; }
            catch (DivideByZeroException)
            {
                moduloCaught = true;
                result += 1000000000;
                Debug.Log(LogPrefix + "Caught integral modulo by zero.");
            }

            string missing = null;
            try { result += missing.Length; }
            catch (NullReferenceException)
            {
                nullCaught = true;
                result += 10000000;
                Debug.Log(LogPrefix + "Caught a null receiver.");
            }

            try { values[NextBadIndex()] = 4; }
            catch (IndexOutOfRangeException)
            {
                negativeWriteCaught = true;
                result += 100000000;
                Debug.Log(LogPrefix + "Caught a negative array write index.");
            }

            try
            {
                try { throw new NotSupportedException("rethrow payload"); }
                catch (NotSupportedException) { throw; }
            }
            catch (NotSupportedException)
            {
                rethrowCaught = true;
                result += 100000;
                Debug.Log(LogPrefix + "Caught a rethrown exception.");
            }

            try
            {
                try { throw new ArgumentException("superseded payload"); }
                finally { throw new InvalidOperationException("finally payload"); }
            }
            catch (InvalidOperationException)
            {
                finallyOverrideCaught = true;
                result += 1000000;
                Debug.Log(LogPrefix + "The exception from finally replaced the earlier exception.");
            }

            result += ReturnThroughFinally();
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    if (i == 0)
                        continue;
                    break;
                }
                finally
                {
                    finallyCount += 100;
                }
            }

            Debug.Log(LogPrefix + "Completed. Result=" + result + ", FinallyCount=" + finallyCount);
        }

        private void ThrowArgumentNull()
        {
            throw new ArgumentNullException("compiler-managed argument failure");
        }

        private int NextBadIndex()
        {
            sideEffectCount++;
            return -1;
        }

        private int ReturnThroughFinally()
        {
            try { return 7; }
            finally { finallyCount += 10; }
        }

        public void ThrowUncaught()
        {
            throw new UdonException(UdonExceptionKind.InvalidOperation,
                "uncaught compiler-managed failure", 91, "uncaught-demo");
        }
    }
}
