using UdonSharp;
using UnityEngine;
using VRC.Economy;
using VRC.SDKBase;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncPurchasesExample : UdonSharpBehaviour
    {
        private IProduct[] _products;

        public override async void Interact()
        {
            await VRCAsync.ListPurchasesAsync(Networking.LocalPlayer);
            Debug.Log("[Async economy] Local purchase count: " + _products.Length);
        }

        public override void OnListPurchases(IProduct[] products, VRCPlayerApi player)
        {
            _products = products;
            Debug.Log("[Legacy economy callback] Purchases received for " + player.displayName);
        }
    }
}
