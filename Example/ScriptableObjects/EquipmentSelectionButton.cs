using UdonSharp;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class EquipmentSelectionButton : UdonSharpBehaviour
    {
        public ScriptableObjectEquipmentExample shop;
        public override void Interact()
        {
            if (shop != null) shop.SelectNext();
        }
    }
}
