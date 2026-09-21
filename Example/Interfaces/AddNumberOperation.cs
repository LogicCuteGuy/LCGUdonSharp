using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.UdonSharpInterfaceExample
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AddNumberOperation : UdonSharpBehaviour, INumberOperation
    {
        [SerializeField] private int amount = 5;

        private int _lastResult;

        public int LastResult => _lastResult;

        public int Apply(int value)
        {
            _lastResult = value + amount;
            return _lastResult;
        }
    }
}
