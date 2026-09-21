using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.UdonSharpInterfaceExample
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class MultiplyNumberOperation : UdonSharpBehaviour, INumberOperation
    {
        [SerializeField] private int multiplier = 3;

        private int _lastResult;

        public int LastResult => _lastResult;

        public int Apply(int value)
        {
            _lastResult = value * multiplier;
            return _lastResult;
        }
    }
}
