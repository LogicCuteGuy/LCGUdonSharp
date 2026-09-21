using UdonSharp;
using UnityEngine;
using VRC.Economy;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncProductOwnersExample : UdonSharpBehaviour
    {
        [SerializeField] private UdonProduct product;

        private string[] _owners;

        public override async void Interact()
        {
            await VRCAsync.ListProductOwnersAsync(product);
            Debug.Log("[Async economy] Product owner count: " + _owners.Length);
        }

        public override void OnListProductOwners(IProduct callbackProduct, string[] owners)
        {
            _owners = owners;
            Debug.Log("[Legacy economy callback] Product owners received for " + callbackProduct.Name);
        }
    }
}
