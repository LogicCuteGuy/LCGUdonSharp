using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GenericHeapObjectsExample : UdonSharpBehaviour
    {
        [SerializeField] private int[] itemIds = new int[4];
        [SerializeField] private string[] itemNames = new string[4];
        [SerializeField] private VRCUrl[] itemUrls = new VRCUrl[4];

        public void SetItem(int index, int id, string itemName, VRCUrl itemUrl)
        {
            if (itemIds == null || itemNames == null || itemUrls == null ||
                index < 0 || index >= itemIds.Length ||
                index >= itemNames.Length || index >= itemUrls.Length)
                return;

            itemIds[index] = id;
            itemNames[index] = itemName;
            itemUrls[index] = itemUrl;
        }

        public override void Interact()
        {
            Debug.Log("[Generic Restrictions] Array-backed item capacity: " + itemIds.Length);
        }

        // Rejected: generic reference types cannot become Udon heap objects.
        // private Box<int> box;
        // private Box<int> CreateBox() { return new Box<int>(); }
    }
}
