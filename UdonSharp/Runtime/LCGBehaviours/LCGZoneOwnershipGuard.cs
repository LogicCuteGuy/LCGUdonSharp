using JetBrains.Annotations;
using UnityEngine;
using VRC.SDKBase;

namespace UdonSharp
{
    [PublicAPI]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class LCGZoneOwnershipGuard : UdonSharpBehaviour
    {
        [SerializeField, HideInInspector] private LCGNetworkZone zone;

        internal void Configure(LCGNetworkZone protectedZone)
        {
            zone = protectedZone;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Automatic reassignment bypasses OnOwnershipRequest. Let only the
            // newly assigned local owner return objects to a valid zone member.
            if (zone != null && Utilities.IsValid(player) && player.isLocal)
                zone.RequestOwnershipRepair();
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            if (zone == null || !zone.CanTakeOwnership(requestedOwner))
                return false;
            // The current owner can hand an object back after leaving the
            // zone. Outsiders still cannot claim it or assign an outside owner.
            return zone.CanTakeOwnership(requestingPlayer) ||
                   (Utilities.IsValid(requestingPlayer) && Networking.GetOwner(gameObject) == requestingPlayer);
        }
    }
}
