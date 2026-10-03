using JetBrains.Annotations;
using UnityEngine;
using VRC.SDKBase;

namespace UdonSharp
{
    [PublicAPI]
    [RequireComponent(typeof(Collider))]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class LCGNetworkZone : UdonSharpBehaviour
    {
        public LCGZoneExitMode exitMode = LCGZoneExitMode.Freeze;
        [Tooltip("Keep unsupported native Udon variable sync unchanged instead of failing the build. " +
                 "Those variables still broadcast to the whole instance and are not zone-scoped.")]
        public bool allowNativeSyncPassthrough;
        [SerializeField, HideInInspector] private int zoneId;
        [SerializeField, HideInInspector] private int epoch;
        [SerializeField, HideInInspector] private LCGRuntime runtime;
        [SerializeField, HideInInspector] private GameObject[] protectedObjects = new GameObject[0];
        [SerializeField, HideInInspector] private GameObject[] exitControlledObjects = new GameObject[0];

        private VRCPlayerApi[] occupants = new VRCPlayerApi[0];
        private double[] enteredAt = new double[0];
        private int ownershipRepairAttempts;
        private bool ownershipRepairScheduled;
        private int snapshotAttempts;
        private bool snapshotScheduled;
        private float snapshotRetryDelay;

        public int ZoneId => zoneId;
        public int Epoch => epoch;
        public LCGZoneExitMode ExitMode => exitMode;
        public int OccupantCount => occupants.Length;

        public VRCPlayerApi GetOccupant(int index)
        {
            return index >= 0 && index < occupants.Length ? occupants[index] : null;
        }

        internal void Configure(int id, LCGRuntime sceneRuntime, GameObject[] ownedObjects,
            GameObject[] controlledObjects)
        {
            zoneId = id;
            runtime = sceneRuntime;
            protectedObjects = ownedObjects ?? new GameObject[0];
            exitControlledObjects = controlledObjects ?? new GameObject[0];
        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || Contains(player))
                return;

            AddOccupant(player);
            epoch++;
            // Only the existing object's owner hands it to the first entrant.
            // Other clients must not race to SetOwner from their trigger view.
            RequestOwnershipRepair();
            if (player.isLocal && exitMode == LCGZoneExitMode.DisableChildren)
                SetExitControlledObjectsActive(true);
            // Each client can observe entry at a different time. The entrant asks
            // for a snapshot, and owners also push one when they observe entry.
            if (runtime != null)
                runtime.RequestZoneSnapshot(zoneId, player);
            if (player.isLocal)
                RequestLocalSnapshotRecovery();
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            int index = IndexOf(player);
            if (index < 0)
                return;

            bool wasProtectedOwner = IsProtectedOwner(player);
            RemoveOccupant(index);
            epoch++;

            if (player.isLocal && wasProtectedOwner && occupants.Length > 0)
                TransferOwnedObjects(player, GetLongestPresentOccupant());

            if (player.isLocal)
            {
                snapshotAttempts = 0;
                ApplyExitMode();
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            OnPlayerTriggerExit(player);
            // VRChat already reassigns disconnected owners. Only the newly
            // assigned owner may repair an assignment outside this zone.
            RequestOwnershipRepair();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal)
                RequestOwnershipRepair();
        }

        public void RequestOwnershipRepair()
        {
            if (GetLongestPresentOccupant() == null)
                return;
            TransferProtectedOwnership(GetLongestPresentOccupant());
            // OnPlayerLeft may precede the automatic ownership assignment.
            // Ownership callbacks also restart this finite recovery window.
            ownershipRepairAttempts = 8;
            if (!ownershipRepairScheduled)
            {
                ownershipRepairScheduled = true;
                SendCustomEventDelayedSeconds(nameof(__lcgRepairOwnership), 0.25f);
            }
        }

        public void __lcgRepairOwnership()
        {
            ownershipRepairScheduled = false;
            if (ownershipRepairAttempts <= 0)
                return;
            ownershipRepairAttempts--;
            TransferProtectedOwnership(GetLongestPresentOccupant());
            if (ownershipRepairAttempts > 0 && GetLongestPresentOccupant() != null)
            {
                ownershipRepairScheduled = true;
                SendCustomEventDelayedSeconds(nameof(__lcgRepairOwnership), 0.25f);
            }
        }

        public void RequestLocalSnapshotRecovery()
        {
            if (runtime == null || !Contains(Networking.LocalPlayer))
                return;
            // Entry can precede mailbox restore or the owner's remote trigger.
            // Retry current state only while inside, with bounded backoff.
            snapshotAttempts = 5;
            snapshotRetryDelay = 0.5f;
            if (!snapshotScheduled)
            {
                snapshotScheduled = true;
                SendCustomEventDelayedSeconds(nameof(__lcgRetrySnapshot), snapshotRetryDelay);
            }
        }

        public void __lcgRetrySnapshot()
        {
            snapshotScheduled = false;
            if (snapshotAttempts <= 0 || runtime == null || !Contains(Networking.LocalPlayer))
            {
                snapshotAttempts = 0;
                return;
            }
            snapshotAttempts--;
            runtime.RequestZoneSnapshot(zoneId, Networking.LocalPlayer);
            if (snapshotAttempts > 0)
            {
                snapshotRetryDelay = Mathf.Min(snapshotRetryDelay * 2f, 4f);
                snapshotScheduled = true;
                SendCustomEventDelayedSeconds(nameof(__lcgRetrySnapshot), snapshotRetryDelay);
            }
        }

        public bool Contains(VRCPlayerApi player)
        {
            return Utilities.IsValid(player) && IndexOf(player) >= 0;
        }

        public bool CanTakeOwnership(VRCPlayerApi requestingPlayer)
        {
            return Utilities.IsValid(requestingPlayer) && Contains(requestingPlayer);
        }

        internal void SendSnapshot(VRCPlayerApi player)
        {
            // Compiler-generated receivers append their current fields and manual object state.
            // The hook is deliberately event-driven; no periodic snapshot or polling is scheduled here.
            if (!Contains(player))
                return;

            for (int i = 0; i < protectedObjects.Length; i++)
            {
                GameObject target = protectedObjects[i];
                if (target == null)
                    continue;
                LCGManualObjectSync sync = target.GetComponent<LCGManualObjectSync>();
                if (sync != null)
                    sync.SendSnapshot(player);
            }
        }

        private void AddOccupant(VRCPlayerApi player)
        {
            int length = occupants.Length;
            VRCPlayerApi[] nextOccupants = new VRCPlayerApi[length + 1];
            double[] nextEnteredAt = new double[length + 1];
            for (int i = 0; i < length; i++)
            {
                nextOccupants[i] = occupants[i];
                nextEnteredAt[i] = enteredAt[i];
            }

            nextOccupants[length] = player;
            nextEnteredAt[length] = Networking.GetServerTimeInSeconds();
            occupants = nextOccupants;
            enteredAt = nextEnteredAt;
        }

        private void RemoveOccupant(int removedIndex)
        {
            int nextLength = occupants.Length - 1;
            VRCPlayerApi[] nextOccupants = new VRCPlayerApi[nextLength];
            double[] nextEnteredAt = new double[nextLength];
            int writeIndex = 0;
            for (int i = 0; i < occupants.Length; i++)
            {
                if (i == removedIndex)
                    continue;
                nextOccupants[writeIndex] = occupants[i];
                nextEnteredAt[writeIndex] = enteredAt[i];
                writeIndex++;
            }

            occupants = nextOccupants;
            enteredAt = nextEnteredAt;
        }

        private int IndexOf(VRCPlayerApi player)
        {
            // A departing player's API can already be invalid in OnPlayerLeft.
            // Its ID still identifies the membership entry that must be removed.
            if (player == null)
                return -1;
            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i] == player)
                    return i;
            }
            int playerId = player.playerId;
            if (playerId < 0)
                return -1;
            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i] != null && occupants[i].playerId == playerId)
                    return i;
            }

            return -1;
        }

        private VRCPlayerApi GetLongestPresentOccupant()
        {
            int best = -1;
            for (int i = 0; i < occupants.Length; i++)
            {
                if (!Utilities.IsValid(occupants[i]))
                    continue;
                if (best < 0 || enteredAt[i] < enteredAt[best] ||
                    (enteredAt[i] == enteredAt[best] && occupants[i].playerId < occupants[best].playerId))
                    best = i;
            }

            return best >= 0 ? occupants[best] : null;
        }

        private bool IsProtectedOwner(VRCPlayerApi player)
        {
            for (int i = 0; i < protectedObjects.Length; i++)
            {
                if (protectedObjects[i] != null && Networking.GetOwner(protectedObjects[i]) == player)
                    return true;
            }

            return false;
        }

        private void TransferProtectedOwnership(VRCPlayerApi player)
        {
            if (!Contains(player))
                return;
            for (int i = 0; i < protectedObjects.Length; i++)
            {
                if (protectedObjects[i] != null && Networking.IsOwner(protectedObjects[i]) &&
                    !Contains(Networking.LocalPlayer))
                    Networking.SetOwner(player, protectedObjects[i]);
            }
        }

        private void TransferOwnedObjects(VRCPlayerApi previousOwner, VRCPlayerApi nextOwner)
        {
            if (!Utilities.IsValid(previousOwner) || !Utilities.IsValid(nextOwner))
                return;
            for (int i = 0; i < protectedObjects.Length; i++)
            {
                GameObject target = protectedObjects[i];
                if (target != null && Networking.GetOwner(target) == previousOwner)
                    Networking.SetOwner(nextOwner, target);
            }
        }

        private void ApplyExitMode()
        {
            if (exitMode == LCGZoneExitMode.DisableChildren)
                SetExitControlledObjectsActive(false);
            else if (exitMode == LCGZoneExitMode.RestoreDefaults)
            {
                if (runtime != null)
                    runtime.RestoreZoneDefaults(zoneId);
                for (int i = 0; i < protectedObjects.Length; i++)
                {
                    if (protectedObjects[i] == null)
                        continue;
                    LCGManualObjectSync sync = protectedObjects[i].GetComponent<LCGManualObjectSync>();
                    if (sync != null)
                        sync.RestoreDefaults();
                }
            }
        }

        private void SetExitControlledObjectsActive(bool active)
        {
            for (int i = 0; i < exitControlledObjects.Length; i++)
            {
                if (exitControlledObjects[i] != null)
                    exitControlledObjects[i].SetActive(active);
            }
        }
    }

}
