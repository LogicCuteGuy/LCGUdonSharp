using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    // An intentionally busy motion producer. The existing router coalesces
    // replaceable samples and applies its scene-wide send budget.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGHighBandwidthExample : UdonSharpBehaviour
    {
        public LCGNetworkZone zone;
        public Transform[] movingObjects;
        public TMP_Text status;
        [Range(1, 32)] public int activeObjects = 16;
        [Range(1f, 30f)] public float sampleHz = 20f;
        [System.NonSerialized] public VRCPlayerApi __lcgPacketSender;
        [LCGPacket(Authority = LCGPacketAuthority.ObjectOwner, Callback = nameof(OnStreamingChanged))]
        [SerializeField] private bool streaming;
        private Vector3[] homePositions;
        private UdonBehaviour runtime;
        private float nextSample;
        private float nextDisplay;
        private float nextToggle;
        private int producedSamples;
        private int locallyOwnedObjects;

        private void Start()
        {
            int count = movingObjects == null ? 0 : movingObjects.Length;
            homePositions = new Vector3[count];
            for (int i = 0; i < count; i++)
                if (movingObjects[i] != null) homePositions[i] = movingObjects[i].localPosition;
            // The scene processor binds this compiler-generated register.
            UdonBehaviour backing = (UdonBehaviour)GetComponent(typeof(UdonBehaviour));
            if (backing != null) runtime = (UdonBehaviour)backing.GetProgramVariable("__lcgRuntime");
            RefreshStatus();
        }

        public override void Interact()
        {
            if (!IsLocalMember()) return;
            if (Networking.IsOwner(gameObject)) ToggleAsOwner();
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestToggle));
        }

        [LCGPacket(Authority = LCGPacketAuthority.Any)]
        public void _RequestToggle()
        {
            if (zone == null || !Utilities.IsValid(__lcgPacketSender) || !zone.Contains(__lcgPacketSender)) return;
            ToggleAsOwner();
        }

        private void ToggleAsOwner()
        {
            if (!IsLocalMember() || !Networking.IsOwner(gameObject) || Time.time < nextToggle) return;
            nextToggle = Time.time + 0.5f;
            if (!streaming && movingObjects != null)
            {
                // Acquire once on explicit start, never on every sample.
                for (int i = 0; i < Mathf.Min(movingObjects.Length, 32); i++)
                    if (movingObjects[i] != null) Networking.SetOwner(Networking.LocalPlayer, movingObjects[i].gameObject);
            }
            streaming = !streaming;
            RefreshStatus();
        }

        private void Update()
        {
            if (streaming && IsLocalMember() && Networking.IsOwner(gameObject) &&
                homePositions != null && Time.time >= nextSample)
            {
                float hz = sampleHz;
                if (!(hz >= 1f && hz <= 30f)) hz = 20f;
                nextSample = Time.time + 1f / hz;
                int count = movingObjects == null ? 0 : Mathf.Min(Mathf.Clamp(activeObjects, 1, 32), movingObjects.Length);
                count = Mathf.Min(count, homePositions.Length);
                locallyOwnedObjects = 0;
                for (int i = 0; i < count; i++)
                {
                    Transform item = movingObjects[i];
                    if (item == null || !Networking.IsOwner(item.gameObject)) continue;
                    float phase = Time.time * 2f + i * 0.35f;
                    item.localPosition = homePositions[i] + new Vector3(0f, Mathf.Sin(phase) * 0.45f, Mathf.Cos(phase) * 0.35f);
                    item.localRotation = Quaternion.Euler(0f, phase * Mathf.Rad2Deg, 0f);
                    LCGNetwork.RequestObjectSync(item.gameObject);
                    locallyOwnedObjects++;
                    producedSamples++;
                }
            }
            if (Time.time < nextDisplay) return;
            nextDisplay = Time.time + 0.5f;
            RefreshStatus();
        }

        public void OnStreamingChanged(VRCPlayerApi sender) { RefreshStatus(); }
        public override void OnOwnershipTransferred(VRCPlayerApi player) { RefreshStatus(); }

        private bool IsLocalMember()
        {
            return zone != null && Utilities.IsValid(Networking.LocalPlayer) && zone.Contains(Networking.LocalPlayer);
        }

        private void RefreshStatus()
        {
            if (status == null) return;
            int pending = runtime == null ? 0 : (int)runtime.GetProgramVariable("pendingMotionCount");
            int batches = runtime == null ? 0 : (int)runtime.GetProgramVariable("motionBatchesSent");
            int lastBytes = runtime == null ? 0 : (int)runtime.GetProgramVariable("lastMotionBatchBytes");
            status.text = "LCG MOTION / enter zone + click start-stop\n" + (streaming ? "RUNNING" : "STOPPED") +
                " | " + (Networking.IsOwner(gameObject) ? "OWNER" : "RECEIVER") +
                "\nRequested: " + activeObjects + " objects x " + sampleHz + " Hz" +
                "\nProduced locally: " + producedSamples + " | owned objects: " + locallyOwnedObjects +
                "\nLCG scene queue: " + pending + " | dispatched batches: " + batches + " | last " + lastBytes + " B" +
                "\nClogged: " + Networking.IsClogged + " | zone members: " + (zone == null ? 0 : zone.OccupantCount) +
                (runtime == null ? "\nRuntime missing: enter Play/Build" : "\nJoin with a second client to generate remote motion traffic");
        }
    }
}
