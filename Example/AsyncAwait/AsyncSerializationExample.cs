using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AsyncSerializationExample : UdonSharpBehaviour
    {
        [UdonSynced] private int _counter;
        private bool _lastSendSucceeded;

        public override async void Interact()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            _counter++;
            await VRCAsync.RequestSerializationAsync();
            Debug.Log("[Async serialization] Post-serialization callback ran. Success: " + _lastSendSucceeded);
        }

        public override void OnPreSerialization()
        {
            Debug.Log("[Legacy serialization callback] Preparing counter " + _counter);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            _lastSendSucceeded = result.success;
            Debug.Log("[Legacy serialization callback] Sent " + result.byteCount + " bytes.");
        }
    }
}
