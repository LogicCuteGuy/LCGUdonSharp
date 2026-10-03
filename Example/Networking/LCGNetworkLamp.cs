using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGNetworkLamp : UdonSharpBehaviour
    {
        public Light lamp;
        public TMP_Text status;
        public LCGNetworkZone zone;
        public bool logMessages;
        // The transport supplies the verified sender for received packet methods.
        [System.NonSerialized] public VRCPlayerApi __lcgPacketSender;

        [LCGPacket(Authority = LCGPacketAuthority.ObjectOwner,
            Callback = nameof(OnLightChanged))]
        [SerializeField] private bool lightOn;
        private float nextToggleTime;
        private float nextPingTime;

        private void Start() { ApplyState(); }
        private void OnEnable() { ApplyState(); }

        public override void Interact()
        {
            if (!IsLocalMember()) return;
            if (Networking.IsOwner(gameObject)) ToggleAsOwner();
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestToggle));
        }

        [LCGPacket(Authority = LCGPacketAuthority.Any)]
        public void _RequestToggle()
        {
            if (zone == null || !Utilities.IsValid(__lcgPacketSender) ||
                !zone.Contains(__lcgPacketSender)) return;
            ToggleAsOwner();
        }

        private void ToggleAsOwner()
        {
            if (!IsLocalMember() || !Networking.IsOwner(gameObject) ||
                Time.time < nextToggleTime) return;
            nextToggleTime = Time.time + 0.25f;
            lightOn = !lightOn; // Compiler queues the latest field value automatically.
            ApplyState();       // Owner applies immediately; receivers use the callback.
        }

        public void OnLightChanged(VRCPlayerApi sender) { ApplyState(); }
        public override void OnOwnershipTransferred(VRCPlayerApi player) { ApplyState(); }

        // UI Button: UdonBehaviour.SendCustomEvent("_PingFirstOtherMember").
        public void _PingFirstOtherMember()
        {
            if (!IsLocalMember() || Time.time < nextPingTime) return;
            for (int i = 0; i < zone.OccupantCount; i++)
            {
                VRCPlayerApi target = zone.GetOccupant(i);
                if (!Utilities.IsValid(target) || target.isLocal) continue;
                nextPingTime = Time.time + 1f;
                SendLCGNetworkEvent(target, nameof(_ReceivePing), "Hello from LCG zone");
                return;
            }
        }

        [LCGPacket(Authority = LCGPacketAuthority.Any)]
        public void _ReceivePing(string message)
        {
            if (!IsLocalMember() || !Utilities.IsValid(__lcgPacketSender) ||
                !zone.Contains(__lcgPacketSender) || message == null || message.Length > 80) return;
            if (logMessages) Debug.Log("[LCG lamp] " + __lcgPacketSender.displayName + ": " + message);
        }

        private bool IsLocalMember()
        {
            return zone != null && Utilities.IsValid(Networking.LocalPlayer) &&
                zone.Contains(Networking.LocalPlayer);
        }

        private void ApplyState()
        {
            if (lamp != null) lamp.enabled = lightOn;
            if (status != null) status.text = "LCG lamp: " + (lightOn ? "ON" : "OFF");
        }
    }
}
