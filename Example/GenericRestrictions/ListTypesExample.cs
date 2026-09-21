using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ListTypesExample : UdonSharpBehaviour
    {
        [SerializeField] private int[] values = new int[32];
        private int valueCount;

        public bool TryAdd(int value)
        {
            if (values == null || valueCount < 0 || valueCount >= values.Length)
            {
                if (valueCount < 0)
                    valueCount = 0;
                return false;
            }

            values[valueCount++] = value;
            return true;
        }

        public int GetValue(int index)
        {
            if (values == null || index < 0 || index >= valueCount || index >= values.Length)
                return 0;

            return values[index];
        }

        public override void Interact()
        {
            bool added = TryAdd(valueCount + 1);
            Debug.Log("[Generic Restrictions] Array append succeeded: " + added + ", count: " + valueCount);
        }

        // Rejected: use the array and explicit count above instead.
        // private System.Collections.Generic.List<int> valuesList;
    }
}
