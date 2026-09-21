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
        [SerializeField, HideInInspector] private int zoneId;
        [SerializeField, HideInInspector] private int epoch;
        [SerializeField, HideInInspector] private LCGRuntime runtime;
        [SerializeField, HideInInspector] private GameObject[] protectedObjects = new GameObject[0];
        [SerializeField, HideInInspector] private GameObject[] exitControlledObjects = new GameObject[0];

        private VRCPlayerApi[] occupants = new VRCPlayerApi[0];
        private double[] enteredAt = new double[0];

        public int ZoneId => zoneId;
        public int Epoch => epoch;
        public LCGZoneExitMode ExitMode => exitMode;

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
            if (occupants.Length == 1)
                TransferProtectedOwnership(player);
            if (player.isLocal && exitMode == LCGZoneExitMode.DisableChildren)
                SetExitControlledObjectsActive(true);
            // Each client can observe entry at a different time. The entrant asks
            // for a snapshot, and owners also push one when they observe entry.
            if (runtime != null)
                runtime.RequestZoneSnapshot(zoneId, player);
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            int index = IndexOf(player);
            if (index < 0)
                return;

            bool wasProtectedOwner = IsProtectedOwner(player);
            RemoveOccupant(index);
            epoch++;

            if (wasProtectedOwner && occupants.Length > 0)
                TransferOwnedObjects(player, GetLongestPresentOccupant());

            if (player.isLocal)
                ApplyExitMode();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            OnPlayerTriggerExit(player);
        }

        public bool Contains(VRCPlayerApi player)
        {
            return IndexOf(player) >= 0;
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
            if (!Utilities.IsValid(player))
                return -1;
            for (int i = 0; i < occupants.Length; i++)
            {
                if (Utilities.IsValid(occupants[i]) && occupants[i].playerId == player.playerId)
                    return i;
            }

            return -1;
        }

        private VRCPlayerApi GetLongestPresentOccupant()
        {
            int best = 0;
            for (int i = 1; i < occupants.Length; i++)
            {
                if (enteredAt[i] < enteredAt[best] ||
                    (enteredAt[i] == enteredAt[best] && occupants[i].playerId < occupants[best].playerId))
                    best = i;
            }

            return occupants[best];
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
            if (!Utilities.IsValid(player))
                return;
            for (int i = 0; i < protectedObjects.Length; i++)
            {
                if (protectedObjects[i] != null)
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
