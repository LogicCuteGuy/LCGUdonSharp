using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [CreateAssetMenu(menuName = "LCGUdonSharp/Examples/Spell Definition")]
    public sealed class SpellDefinition : ItemDefinition
    {
        public int power;
        public int manaCost;
    }
}
