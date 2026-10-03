using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    // Keep this object outside LCGNetworkZone: this is instance-wide native sync.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class NativeNetworkLamp : UdonSharpBehaviour
    {
        public Light lamp;
        public TMP_Text status;
        [UdonSynced, SerializeField] private bool lightOn;
        private float nextToggleTime;

        private void Start() { ApplyState(); }

        public override void Interact()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            if (Networking.IsOwner(gameObject)) ToggleAsOwner();
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_OwnerToggle));
        }

        // Everyone in the instance may request a toggle. The existing owner writes.
        [NetworkCallable(4)]
        public void _OwnerToggle()
        {
            if (!NetworkCalling.InNetworkCall ||
                !Utilities.IsValid(NetworkCalling.CallingPlayer)) return;
            ToggleAsOwner();
        }

        private void ToggleAsOwner()
        {
            if (!Networking.IsOwner(gameObject) || Time.time < nextToggleTime) return;
            nextToggleTime = Time.time + 0.25f;
            lightOn = !lightOn;
            ApplyState();
            RequestSerialization();
        }

        public override void OnDeserialization() { ApplyState(); }
        public override void OnOwnershipTransferred(VRCPlayerApi player) { ApplyState(); }

        private void ApplyState()
        {
            // Disable the Light component, keeping the interactable object alive.
            if (lamp != null) lamp.enabled = lightOn;
            if (status != null) status.text = "Native lamp: " + (lightOn ? "ON" : "OFF");
        }
    }
}
