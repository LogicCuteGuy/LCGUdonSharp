using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    // Bulk state, outside LCG zones. Serialization callbacks measure serialized
    // bytes, including SDK overhead; they do not measure on-the-wire delivery.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class NativeHighBandwidthExample : UdonSharpBehaviour
    {
        public TMP_Text status;
        [Range(64, 8192)] public int payloadBytes = 2048;
        [Range(1f, 10f)] public float requestedHz = 4f;
        [UdonSynced] private bool streaming;
        [UdonSynced] private int revision;
        [UdonSynced] private byte[] payload = new byte[0];
        private bool inFlight;
        private float nextSample;
        private float nextToggle;
        private float nextDisplay;
        private float meterStarted;
        private int serializedBytes;
        private int successfulSerializations;
        private int failedSerializations;
        private int receivedSnapshots;
        private int lastByteCount;
        private float serializedBytesPerSecond;

        private void Start() { meterStarted = Time.time; RefreshStatus(); }

        public override void Interact()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            if (Networking.IsOwner(gameObject)) ToggleAsOwner();
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestToggle));
        }

        [NetworkCallable(2)]
        public void _RequestToggle()
        {
            if (!NetworkCalling.InNetworkCall || !Utilities.IsValid(NetworkCalling.CallingPlayer)) return;
            ToggleAsOwner();
        }

        private void ToggleAsOwner()
        {
            if (!Networking.IsOwner(gameObject) || Time.time < nextToggle) return;
            nextToggle = Time.time + 0.5f;
            streaming = !streaming;
            // Keep the buffer stable while a serialization is outstanding.
            if (!inFlight) PreparePayload();
            inFlight = true;
            RequestSerialization();
            RefreshStatus();
        }

        private void Update()
        {
            if (streaming && Networking.IsOwner(gameObject) && !inFlight &&
                !Networking.IsClogged && Time.time >= nextSample)
            {
                float hz = requestedHz;
                if (!(hz >= 1f && hz <= 10f)) hz = 4f;
                nextSample = Time.time + 1f / hz; // No catch-up bursts after a stall.
                PreparePayload();
                revision++;
                // A changing header makes each snapshot observable without a
                // per-tick loop over the entire large buffer.
                payload[0] = (byte)(revision & 255);
                payload[1] = (byte)((revision >> 8) & 255);
                payload[2] = (byte)((revision >> 16) & 255);
                payload[3] = (byte)((revision >> 24) & 255);
                inFlight = true;
                RequestSerialization();
            }
            if (Time.time < nextDisplay) return;
            float elapsed = Time.time - meterStarted;
            if (elapsed > 0f) serializedBytesPerSecond = serializedBytes / elapsed;
            serializedBytes = 0;
            meterStarted = Time.time;
            nextDisplay = Time.time + 0.5f;
            RefreshStatus();
        }

        private void PreparePayload()
        {
            int length = Mathf.Clamp(payloadBytes, 64, 8192);
            if (payload != null && payload.Length == length) return;
            payload = new byte[length];
            for (int i = 0; i < length; i++) payload[i] = (byte)(i & 255);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            inFlight = false;
            lastByteCount = result.byteCount;
            if (result.success)
            {
                successfulSerializations++;
                serializedBytes += result.byteCount;
            }
            else failedSerializations++;
        }

        public override void OnDeserialization() { receivedSnapshots++; RefreshStatus(); }
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            inFlight = false;
            nextSample = Time.time + 0.5f;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (status == null) return;
            status.text = "NATIVE BULK / click start-stop\n" + (streaming ? "RUNNING" : "STOPPED") +
                " | " + (Networking.IsOwner(gameObject) ? "OWNER" : "RECEIVER") +
                "\nPayload: " + (payload == null ? 0 : payload.Length) + " B | requested " + requestedHz + " Hz" +
                "\nSerialized: " + Mathf.RoundToInt(serializedBytesPerSecond) + " B/s | last " + lastByteCount + " B" +
                "\nSuccess/fail: " + successfulSerializations + "/" + failedSerializations +
                " | received: " + receivedSnapshots + " | revision: " + revision +
                "\nClogged: " + Networking.IsClogged + " | pending: " + inFlight;
        }
    }
}
