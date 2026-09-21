using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    public interface IReadableExample
    {
        int ReadValue();
    }

    public interface IResettableExample
    {
        void ResetValue();
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class MultipleConcreteBasesExample : UdonSharpBehaviour, IReadableExample, IResettableExample
    {
        [SerializeField] private InterfaceMembersExample composedCounter;

        public int ReadValue()
        {
            return composedCounter != null ? composedCounter.Value : 0;
        }

        public void ResetValue()
        {
            // Composition delegates work to another concrete behaviour.
            if (composedCounter != null)
                composedCounter.ResetValue();
        }

        public override void Interact()
        {
            Debug.Log("[Generic Restrictions] Composed value: " + ReadValue());
            ResetValue();
        }

        // Rejected by C#/Roslyn before Udon lowering:
        // public class InvalidCombined : FirstBase, SecondBase { }
    }
}
