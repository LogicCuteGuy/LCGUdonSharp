using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncStringDownloadExample : UdonSharpBehaviour
    {
        [SerializeField] private VRCUrl url;

        private string _lastDownloadedText;

        public override async void Interact()
        {
            Debug.Log("[Async string] Starting VRCStringDownloader request.");
            await VRCAsync.LoadStringAsync(url);
            Debug.Log("[Async string] Await continuation ran after the legacy callback. Text: " +
                      _lastDownloadedText);
        }

        // Traditional Udon callback code remains valid. The compiler appends its
        // continuation dispatcher after this body, so this log runs first.
        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _lastDownloadedText = result.Result;
            Debug.Log("[Legacy string callback] Download succeeded: " + result.Url);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _lastDownloadedText = null;
            Debug.LogError("[Legacy string callback] Download failed: " + result.Error);
        }
    }
}
