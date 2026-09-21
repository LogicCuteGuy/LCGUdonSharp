using JetBrains.Annotations;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace UdonSharp
{
    /// <summary>
    /// Per-player mailbox. VRChat clones this PlayerObject and owns the clone for that player.
    /// </summary>
    [PublicAPI]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class LCGRuntimePlayer : UdonSharpBehaviour
    {
        [SerializeField] private LCGRuntime runtime;
#if LCG_NETWORK_DIAGNOSTICS
        private bool loggedFrame;
#endif

        internal void Configure(LCGRuntime sceneRuntime)
        {
            runtime = sceneRuntime;
        }

        [NetworkCallable(100)]
        public void ReceiveFrame(byte[] frame)
        {
#if LCG_NETWORK_DIAGNOSTICS
            if (!loggedFrame)
            {
                loggedFrame = true;
                Debug.Log("[LCG sync] Mailbox received: runtime=" + (runtime != null) +
                    ", owner=" + Networking.IsOwner(gameObject) +
                    ", validSender=" + Utilities.IsValid(NetworkCalling.CallingPlayer));
            }
#endif
            if (runtime == null || !Networking.IsOwner(gameObject))
                return;

            runtime.ReceiveFrame(frame, NetworkCalling.CallingPlayer);
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return false;
        }
    }
}
