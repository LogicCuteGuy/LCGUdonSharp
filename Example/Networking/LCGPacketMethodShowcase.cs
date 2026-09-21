using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    /// <summary>
    /// Demonstrates ordered packet methods, broadcast delivery, targeted delivery,
    /// and the difference between a direct local call and a network send.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGPacketMethodShowcase : UdonSharpBehaviour
    {
        [SerializeField] private Text output;
        private int messageNumber;

        public void BroadcastToOthers()
        {
            messageNumber++;
            SendCustomNetworkEvent(
                NetworkEventTarget.Others,
                nameof(ReceiveAnnouncement),
                "Broadcast #" + messageNumber,
                transform.position);
            ShowStatus("Sent broadcast #" + messageNumber);
        }

        public void SendToFirstOtherPlayer()
        {
            VRCPlayerApi target = FindFirstOtherPlayer();
            if (!Utilities.IsValid(target))
            {
                ShowStatus("No other player is available");
                return;
            }

            messageNumber++;
            SendLCGNetworkEvent(
                target,
                nameof(ReceiveAnnouncement),
                "Targeted #" + messageNumber,
                transform.position);
            ShowStatus("Sent targeted packet to " + target.displayName);
        }

        public void RunLocalOnly()
        {
            messageNumber++;
            // A direct call never creates a packet.
            ReceiveAnnouncement("Local-only #" + messageNumber, transform.position);
        }

        [LCGPacket(Authority = LCGPacketAuthority.Any)]
        public void ReceiveAnnouncement(string message, Vector3 origin)
        {
            ShowStatus(message + " at " + origin);
        }

        private VRCPlayerApi FindFirstOtherPlayer()
        {
            int playerCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] players = new VRCPlayerApi[playerCount];
            VRCPlayerApi.GetPlayers(players);
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]) && !players[i].isLocal)
                    return players[i];
            }

            return null;
        }

        private void ShowStatus(string message)
        {
            if (output != null)
                output.text = "LCG method\n" + message;
            Debug.Log("[LCG method showcase] " + message);
        }
    }
}
