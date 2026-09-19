using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    /// <summary>
    /// Demonstrates a coalesced packet field and its verified-sender callback.
    /// Wire the public methods to UI Buttons or call Interact to increment the score.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGPacketFieldShowcase : UdonSharpBehaviour
    {
        [SerializeField] private Text output;

        [LCGPacket(
            Authority = LCGPacketAuthority.ObjectOwner,
            Callback = nameof(OnScoreChanged))]
        [SerializeField] private int score;

        private void Start()
        {
            ShowStatus("Ready");
        }

        public override void Interact()
        {
            AddOne();
        }

        public void TakeOwnership()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            ShowStatus("Requested ownership");
        }

        public void AddOne()
        {
            if (!EnsureLocalOwnership())
                return;

            score++;
            ShowStatus("Queued score " + score);
        }

        public void AddThreeCoalesced()
        {
            if (!EnsureLocalOwnership())
                return;

            // These assignments happen in one event. Only the latest value is sent.
            score++;
            score++;
            score++;
            ShowStatus("Queued latest score " + score);
        }

        public void ForceResend()
        {
            if (!EnsureLocalOwnership())
                return;

            // Sends even if score has not changed.
            ForceSendPacket(nameof(score));
            ShowStatus("Forced score " + score);
        }

        public void OnScoreChanged(VRCPlayerApi sender)
        {
            string senderName = Utilities.IsValid(sender) ? sender.displayName : "unknown";
            ShowStatus("Received " + score + " from " + senderName);
        }

        private bool EnsureLocalOwnership()
        {
            if (Networking.IsOwner(gameObject))
                return true;

            ShowStatus("Take ownership first");
            return false;
        }

        private void ShowStatus(string message)
        {
            if (output != null)
                output.text = "LCG field\n" + message + "\nCurrent score: " + score;
            Debug.Log("[LCG field showcase] " + message);
        }
    }
}
