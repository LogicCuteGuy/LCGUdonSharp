using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    public static class GenericBehaviourTools<T>
    {
        public static T Identity(T value)
        {
            return value;
        }
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GenericBehavioursExample : UdonSharpBehaviour
    {
        [SerializeField] private int value = 7;

        public int ReadValue()
        {
            // Supported: generic logic lives in a static helper specialized to int.
            return GenericBehaviourTools<int>.Identity(value);
        }

        public override void Interact()
        {
            Debug.Log("[Generic Restrictions] Static generic helper value: " + ReadValue());
        }

        // Rejected, including abstract or asset-less variants:
        // public class GenericMeter<T> : UdonSharpBehaviour { }
    }
}
