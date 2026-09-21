using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.UdonSharpInterfaceExample
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class InterfaceExampleRunner : UdonSharpBehaviour
    {
        [SerializeField] private AddNumberOperation addOperation;
        [SerializeField] private MultiplyNumberOperation multiplyOperation;
        [SerializeField] private int input = 10;
        [SerializeField] private bool runOnStart = true;

        private void Start()
        {
            if (runOnStart)
            {
                RunExample();
            }
        }

        public override void Interact()
        {
            RunExample();
        }

        public void RunExample()
        {
            if (addOperation == null || multiplyOperation == null)
            {
                Debug.LogError("[Interface Example] Assign both operation behaviours.");
                return;
            }

            // Concrete component fields are serializable in Unity. Runtime work uses the interface.
            INumberOperation firstOperation = addOperation;
            INumberOperation secondOperation = multiplyOperation;

            int added = firstOperation.Apply(input);
            int multiplied = secondOperation.Apply(input);

            Debug.Log("[Interface Example] Add result: " + added);
            Debug.Log("[Interface Example] Multiply result: " + multiplied);
            Debug.Log("[Interface Example] Property results: " +
                      firstOperation.LastResult + ", " + secondOperation.LastResult);
        }
    }
}
