using UdonSharp;
using UnityEngine;
using VRC.Economy;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncAvailableProductsExample : UdonSharpBehaviour
    {
        private IProduct[] _products;

        public override async void Interact()
        {
            await VRCAsync.ListAvailableProductsAsync();
            Debug.Log("[Async economy] Available product count: " + _products.Length);
        }

        public override void OnListAvailableProducts(IProduct[] products)
        {
            _products = products;
            Debug.Log("[Legacy economy callback] Available products received.");
        }
    }
}
