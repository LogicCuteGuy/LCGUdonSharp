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

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return zone != null && zone.CanTakeOwnership(requestingPlayer) && zone.CanTakeOwnership(requestedOwner);
        }
    }
}
