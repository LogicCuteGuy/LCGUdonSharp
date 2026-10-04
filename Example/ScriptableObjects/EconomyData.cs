using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [CreateAssetMenu(menuName = "LCGUdonSharp/Examples/Economy Data")]
    public sealed class EconomyData : ScriptableObject
    {
        public int price;
        public string currency = "coins";
    }
}
