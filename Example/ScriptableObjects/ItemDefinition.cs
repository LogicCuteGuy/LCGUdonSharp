using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    public abstract class ItemDefinition : ScriptableObject
    {
        public string displayName;
        public Color displayColor;
        public EconomyData economy;
        public ItemDefinition[] upgrades;
    }
}
