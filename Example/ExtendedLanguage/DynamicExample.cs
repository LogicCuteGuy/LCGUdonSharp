using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DynamicExample : UdonSharpBehaviour
    {
        [SerializeField] private int input = 41;

        public override void Interact()
        {
            // Flow proves this is always the exact numeric type int. Build-time lowering changes the local
            // to int before Udon binding, so no DLR object exists at runtime.
            dynamic concreteValue = input;
            int answer = concreteValue + 1;
            Debug.Log("[dynamic] answer=" + answer);
        }

        // Rejected at build time because the runtime type cannot be proven:
        // public int Dispatch(dynamic unknown) => unknown.Calculate();
    }
}
