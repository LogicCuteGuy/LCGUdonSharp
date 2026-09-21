using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncVideoEndExample : UdonSharpBehaviour
    {
        [SerializeField] private BaseVRCVideoPlayer player;

        private VideoError _lastError;

        public override async void Interact()
        {
            await VRCAsync.WaitForVideoEndAsync(player);
            Debug.Log("[Async video] Playback ended or failed. Error: " + _lastError);
        }

        public override void OnVideoEnd()
        {
            _lastError = VideoError.Unknown;
            Debug.Log("[Legacy video callback] Playback ended.");
        }

        public override void OnVideoError(VideoError videoError)
        {
            _lastError = videoError;
            Debug.LogError("[Legacy video callback] Playback failed: " + videoError);
        }
    }
}
