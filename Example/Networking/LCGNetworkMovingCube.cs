using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    // Add VRC_ObjectSync to this same GameObject, below the zone in the hierarchy.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGNetworkMovingCube : UdonSharpBehaviour
    {
        public LCGNetworkZone zone;
        public float moveDistance = 0.5f;
        private float nextMoveTime;

        public override void Interact()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (zone == null || !Utilities.IsValid(local) || !zone.Contains(local) ||
                Time.time < nextMoveTime) return;
            nextMoveTime = Time.time + 0.25f;

            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return;

            // Move along the zone's X axis and wrap inside the sample's play area.
            Vector3 position = zone.transform.InverseTransformPoint(transform.position);
            position.x += Mathf.Clamp(moveDistance, 0.05f, 1f);
            if (position.x > 2f) position.x = -2f;
            transform.position = zone.transform.TransformPoint(position);
            LCGNetwork.RequestObjectSync(gameObject);
        }
    }
}
