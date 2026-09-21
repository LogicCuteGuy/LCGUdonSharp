using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    public interface IReadableValue<T>
    {
        T ReadValue();
    }

    public interface ILeftValue<T> : IReadableValue<T>
    {
        void Add(T value);
    }

    public interface IRightValue<T> : IReadableValue<T>
    {
        void ResetValue();
    }

    // The shared IReadableValue<T> contract is deduplicated at the diamond.
    public interface IDiamondValue<T> : ILeftValue<T>, IRightValue<T>
    {
    }

    public static class ClosedGenericMath<T>
    {
        public static T Identity(T value)
        {
            return value;
        }
    }

    public abstract class ValueBehaviourBase : UdonSharpBehaviour
    {
        protected int currentValue;

        protected int ClampPositive(int value)
        {
            return value < 0 ? 0 : value;
        }
    }

    public abstract class IntegerValueBehaviour : ValueBehaviourBase
    {
        protected void Store(int value)
        {
            currentValue = ClampPositive(value);
        }
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GenericHierarchyExample : IntegerValueBehaviour, IDiamondValue<int>
    {
        [SerializeField] private int startValue = 5;

        public override void Interact()
        {
            Store(ClosedGenericMath<int>.Identity(startValue));

            IDiamondValue<int> value = this;
            value.Add(2);
            int result = value.ReadValue();
            value.ResetValue();

            Debug.Log("[generics/hierarchy] result=" + result + ", reset=" + value.ReadValue());
        }

        public int ReadValue()
        {
            return currentValue;
        }

        public void Add(int value)
        {
            currentValue += value;
        }

        public void ResetValue()
        {
            currentValue = 0;
        }
    }
}
