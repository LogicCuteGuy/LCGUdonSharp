using JetBrains.Annotations;
using UnityEngine;
using VRC.Udon.Common.Interfaces;
using VRC.SDKBase;

namespace UdonSharp
{
    [PublicAPI]
    public static class LCGNetwork
    {
        public static void RequestObjectSync(GameObject target)
        {
            if (target == null)
                return;
            LCGManualObjectSync sync = target.GetComponent<LCGManualObjectSync>();
            if (sync != null)
                sync.RequestObjectSync();
        }
    }

    /// <summary>
    /// Event-driven replacement for VRCObjectSync below an LCG network zone.
    /// </summary>
    [PublicAPI]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class LCGManualObjectSync : UdonSharpBehaviour
    {
        [SerializeField, HideInInspector] private LCGRuntime runtime;
        [SerializeField, HideInInspector] private LCGNetworkZone zone;
        [SerializeField, HideInInspector] private int receiverId;
        [SerializeField, HideInInspector] private Vector3 defaultPosition;
        [SerializeField, HideInInspector] private Quaternion defaultRotation;
        [SerializeField, HideInInspector] private bool defaultActive;
        [HideInInspector] public VRCPlayerApi __lcgPacketSender;

        internal void Configure(LCGRuntime sceneRuntime, LCGNetworkZone networkZone, int id)
        {
            runtime = sceneRuntime;
            zone = networkZone;
            receiverId = id;
            defaultPosition = transform.localPosition;
            defaultRotation = transform.localRotation;
            defaultActive = gameObject.activeSelf;
        }

        public void RequestObjectSync()
        {
            if (zone == null || !zone.Contains(Networking.LocalPlayer) || !Networking.IsOwner(gameObject))
                return;

            SendState(NetworkEventTarget.Others, null, false);
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (zone != null && zone.Contains(Networking.LocalPlayer) && Networking.IsOwner(gameObject))
                SendState(NetworkEventTarget.Others, null, true);
        }

        internal void SendSnapshot(VRCPlayerApi player)
        {
            if (runtime == null || !Utilities.IsValid(player) || zone == null || !zone.Contains(player))
                return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestSnapshot));
        }

        [LCGPacket(Authority = LCGPacketAuthority.Any)]
        public void RequestSnapshot()
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(__lcgPacketSender))
                return;
            SendState(NetworkEventTarget.Self, __lcgPacketSender, true);
        }

        [LCGPacket(Authority = LCGPacketAuthority.ObjectOwner)]
        public void ApplyObjectState(Vector3 position, Quaternion rotation, Vector3 velocity,
            Vector3 angularVelocity, bool useGravity, bool isKinematic, bool discontinuity)
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (discontinuity)
                transform.SetPositionAndRotation(position, rotation);
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }

            if (body == null)
                return;
            body.velocity = velocity;
            body.angularVelocity = angularVelocity;
            body.useGravity = useGravity;
            body.isKinematic = isKinematic;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal && zone != null && zone.Contains(player))
                SendState(NetworkEventTarget.Others, null, true);
        }

        private void SendState(NetworkEventTarget target, VRCPlayerApi player, bool discontinuity)
        {
            Rigidbody body = GetComponent<Rigidbody>();
            Vector3 velocity = body != null ? body.velocity : Vector3.zero;
            Vector3 angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            bool useGravity = body != null && body.useGravity;
            bool isKinematic = body != null && body.isKinematic;
            if (Utilities.IsValid(player))
                SendLCGNetworkEvent(player, nameof(ApplyObjectState), transform.position, transform.rotation,
                    velocity, angularVelocity, useGravity, isKinematic, discontinuity);
            else
                SendCustomNetworkEvent(target, nameof(ApplyObjectState), transform.position, transform.rotation,
                    velocity, angularVelocity, useGravity, isKinematic, discontinuity);
        }

        internal void RestoreDefaults()
        {
            transform.localPosition = defaultPosition;
            transform.localRotation = defaultRotation;
            gameObject.SetActive(defaultActive);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
