using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class RefOutExample : UdonSharpBehaviour
    {
        [SerializeField] private int seed = 3;
        [SerializeField] private int target = 7;

        private int[] values = new int[2];

        public override void Interact()
        {
            values[0] = seed;
            values[1] = seed + 10;

            Swap(ref values[0], ref values[1]);
            AddUntil(ref values[0], target, out int recursiveCalls);
            DoubleValue(values[1], out int doubled);
            Increment(ref seed);

            Debug.Log("[ref/out] first=" + values[0] +
                      ", second=" + values[1] +
                      ", doubled=" + doubled +
                      ", recursive calls=" + recursiveCalls +
                      ", field=" + seed);
        }

        private void Swap(ref int left, ref int right)
        {
            int temporary = left;
            left = right;
            right = temporary;
        }

        private void Increment(ref int value)
        {
            value++;
        }

        private void DoubleValue(int input, out int result)
        {
            result = input * 2;
        }

        private void AddUntil(ref int value, int limit, out int calls)
        {
            if (value >= limit)
            {
                calls = 0;
                return;
            }

            value++;
            AddUntil(ref value, limit, out calls);
            calls++;
        }
    }
}
