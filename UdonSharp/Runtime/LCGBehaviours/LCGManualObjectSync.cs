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
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class LCGManualObjectSync : UdonSharpBehaviour
    {
        [SerializeField, HideInInspector] private LCGRuntime runtime;
        [SerializeField, HideInInspector] private LCGNetworkZone zone;
        [SerializeField, HideInInspector] private int receiverId;
        [SerializeField, HideInInspector] private Vector3 defaultPosition;
        [SerializeField, HideInInspector] private Quaternion defaultRotation;
        [SerializeField, HideInInspector] private bool defaultActive;
        [HideInInspector] public VRCPlayerApi __lcgPacketSender;
        private bool pickupMotionActive;
        private bool pickupHeld;
        private float nextPickupSyncTime;
        private Rigidbody cachedBody;
        private bool remoteStateActive;
        private bool hasRemotePhysics;
        private Vector3 interpolationStartPosition;
        private Quaternion interpolationStartRotation;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Vector3 targetVelocity;
        private Vector3 targetAngularVelocity;
        private bool targetUseGravity;
        private bool targetIsKinematic;
        private float receivedAt;
        private float interpolationDuration = 0.1f;

        private Rigidbody GetBody()
        {
            if (cachedBody == null)
                cachedBody = GetComponent<Rigidbody>();
            return cachedBody;
        }

        public override void OnPickup()
        {
#if LCG_NETWORK_DIAGNOSTICS
            Debug.Log("[LCG sync] Pickup " + gameObject.name + ": owner=" + Networking.IsOwner(gameObject) +
                ", insideZone=" + (zone != null && zone.Contains(Networking.LocalPlayer)));
#endif
            pickupHeld = true;
            pickupMotionActive = true;
            nextPickupSyncTime = 0f;
        }

        public override void OnDrop()
        {
            pickupHeld = false;
            // Send on the next LateUpdate, after pickup physics has been restored.
            pickupMotionActive = true;
            nextPickupSyncTime = 0f;
        }

        private void LateUpdate()
        {
            if (remoteStateActive && !Networking.IsOwner(gameObject))
            {
                if (zone == null || !zone.Contains(Networking.LocalPlayer))
                    return;
                float elapsed = Time.realtimeSinceStartup - receivedAt;
                float t = Mathf.Clamp01(elapsed / interpolationDuration);
                // Bounded prediction bridges lower crowd-adaptive sample rates
                // without letting a missing sender move the object forever.
                Vector3 prediction = targetVelocity * Mathf.Clamp(elapsed - interpolationDuration, 0f, 0.15f);
                transform.SetPositionAndRotation(Vector3.Lerp(interpolationStartPosition, targetPosition, t) + prediction,
                    Quaternion.Slerp(interpolationStartRotation, targetRotation, t));
                return;
            }
            if (!pickupMotionActive)
                return;
            if (!Networking.IsOwner(gameObject))
            {
                pickupMotionActive = false;
                pickupHeld = false;
                return;
            }
            if (Time.time < nextPickupSyncTime)
                return;

            nextPickupSyncTime = Time.time + 0.1f;
            RequestObjectSync();
            Rigidbody body = GetBody();
            if (!pickupHeld && (body == null || body.isKinematic || body.IsSleeping()))
                pickupMotionActive = false;
        }

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
            if (Networking.IsOwner(gameObject))
                SendState(NetworkEventTarget.Self, player, true);
            else if (player.isLocal)
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
            Rigidbody body = GetBody();
            float now = Time.realtimeSinceStartup;
            interpolationDuration = remoteStateActive ? Mathf.Clamp(now - receivedAt, 0.05f, 0.35f) : 0.1f;
            receivedAt = now;
            bool snap = discontinuity || !remoteStateActive;
            targetPosition = position;
            targetRotation = rotation;
            targetVelocity = velocity;
            targetAngularVelocity = angularVelocity;
            targetUseGravity = useGravity;
            targetIsKinematic = isKinematic;
            interpolationStartPosition = snap ? position : transform.position;
            interpolationStartRotation = snap ? rotation : transform.rotation;
            remoteStateActive = !Networking.IsOwner(gameObject);
            hasRemotePhysics = remoteStateActive;
            if (snap)
                transform.SetPositionAndRotation(position, rotation);

            if (body == null)
                return;
            body.useGravity = useGravity;
            // Remote physics must not fight the interpolated authoritative pose.
            body.isKinematic = remoteStateActive || isKinematic;
            if (!body.isKinematic)
            {
                body.velocity = velocity;
                body.angularVelocity = angularVelocity;
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal && zone != null && zone.Contains(player))
            {
                Rigidbody body = GetBody();
                if (remoteStateActive)
                    transform.SetPositionAndRotation(targetPosition, targetRotation);
                if (hasRemotePhysics && body != null)
                {
                    body.isKinematic = targetIsKinematic;
                    body.useGravity = targetUseGravity;
                    if (!body.isKinematic)
                    {
                        body.velocity = targetVelocity;
                        body.angularVelocity = targetAngularVelocity;
                    }
                }
                remoteStateActive = false;
                hasRemotePhysics = false;
                pickupMotionActive = body != null && !body.isKinematic;
                SendState(NetworkEventTarget.Others, null, true);
            }
        }

        private void SendState(NetworkEventTarget target, VRCPlayerApi player, bool discontinuity)
        {
            Rigidbody body = GetBody();
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
            remoteStateActive = false;
            transform.localPosition = defaultPosition;
            transform.localRotation = defaultRotation;
            targetPosition = transform.position;
            targetRotation = transform.rotation;
            targetVelocity = Vector3.zero;
            targetAngularVelocity = Vector3.zero;
            gameObject.SetActive(defaultActive);
            Rigidbody body = GetBody();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
