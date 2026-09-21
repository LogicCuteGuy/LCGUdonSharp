using System.Linq;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LinqClosureExample : UdonSharpBehaviour
    {
        [SerializeField] private int[] values = { 1, 2, 3, 4, 5, 6 };
        [SerializeField] private int minimum = 3;
        [SerializeField] private int scale = 10;

        public override void Interact()
        {
            // Both lambdas capture method/behaviour state. The compiler removes the
            // delegates and LINQ objects, then emits array loops and an exact-sized array.
            int localScale = scale;
            int[] result = values
                .Where(value => value >= minimum)
                .Select(value => value * localScale)
                .ToArray();

            for (int index = 0; index < result.Length; index++)
                Debug.Log("[LINQ closure] result[" + index + "]=" + result[index]);
        }
    }
}
