using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;
using VRC.SDKBase;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncVideoLoadExample : UdonSharpBehaviour
    {
        [SerializeField] private BaseVRCVideoPlayer player;
        [SerializeField] private VRCUrl videoUrl;

        private VideoError _lastError;

        public override async void Interact()
        {
            await VRCAsync.LoadVideoAsync(player, videoUrl, true);
            Debug.Log("[Async video] Load completed after OnVideoReady/OnVideoError. Error: " + _lastError);
        }

        public override void OnVideoReady()
        {
            _lastError = VideoError.Unknown;
            Debug.Log("[Legacy video callback] Video is ready.");
        }

        public override void OnVideoError(VideoError videoError)
        {
            _lastError = videoError;
            Debug.LogError("[Legacy video callback] Load failed: " + videoError);
        }
    }
}
