using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    public interface IConcreteValueSource<T>
    {
        T ReadValue();
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class OpenGenericsExample : UdonSharpBehaviour, IConcreteValueSource<int>
    {
        [SerializeField] private int value = 42;

        public int ReadValue()
        {
            // Supported: the interface is closed over the concrete int type.
            return value;
        }

        public void RunExample()
        {
            IConcreteValueSource<int> source = this;
            Debug.Log("[Generic Restrictions] Closed interface value: " + source.ReadValue());
        }

        public override void Interact()
        {
            RunExample();
        }

        // Rejected: an open runtime type has no single Udon representation.
        // System.Type openType = typeof(IConcreteValueSource<>);
    }
}
