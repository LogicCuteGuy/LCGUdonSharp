using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    public interface ICounterContract
    {
        int Value { get; }
        int Increment();
    }

    // Rejected interface shape (kept commented so this example compiles):
    // public interface IInvalidCounterContract
    // {
    //     const int Version = 1;
    //     event System.Action Changed;
    //     int this[int index] { get; }
    //     static int StaticValue { get { return 0; } }
    //     static void ResetAll() { }
    //     T Convert<T>(T value);
    //     int DefaultValue() { return 1; }
    //     class NestedType { }
    // }

    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class InterfaceMembersExample : UdonSharpBehaviour, ICounterContract
    {
        [SerializeField] private int value;

        public int Value => value;

        public int Increment()
        {
            return ++value;
        }

        public void ResetValue()
        {
            value = 0;
        }

        public void RunExample()
        {
            ICounterContract counter = this;
            Debug.Log("[Generic Restrictions] Counter value: " + counter.Increment());
        }

        public override void Interact()
        {
            RunExample();
        }

        // The rejected declarations are shown above IInvalidCounterContract.
    }
}
