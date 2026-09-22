using UdonSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDKBase;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncImageDownloadExample : UdonSharpBehaviour
    {
        [SerializeField] private VRCUrl imageUrl;
        [SerializeField] private Material targetMaterial;

        private VRCImageDownloader _downloader;
        private Texture2D _lastDownloadedTexture;
        private int _requestCount;
        private bool _awaitCompleted;
        private int[] _completionCounts = new int[1];

        private void Start()
        {
            _downloader = new VRCImageDownloader();
        }

        public override async void Interact()
        {
            BeginRequest(ref _requestCount, out _awaitCompleted);
            Debug.Log("[Async image] Starting VRCImageDownloader request.");
            await VRCAsync.LoadImageAsync(_downloader, imageUrl, targetMaterial);
            CompleteRequest(ref _completionCounts[0], out _awaitCompleted);
            Debug.Log("[Async image] Await continuation ran after the legacy callback. Texture: " +
                      _lastDownloadedTexture + ", requests=" + _requestCount +
                      ", completions=" + _completionCounts[0] +
                      ", completed=" + _awaitCompleted);
        }

        // Ordinary C# ref/out methods can run on either side of an SDK await.
        private void BeginRequest(ref int requestCount, out bool completed)
        {
            requestCount++;
            completed = false;
        }

        private void CompleteRequest(ref int completionCount, out bool completed)
        {
            completionCount++;
            completed = true;
        }

        // The request identity is used to match this callback to the await.
        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            _lastDownloadedTexture = result.Result;
            Debug.Log("[Legacy image callback] Download succeeded: " + result.Url);
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            _lastDownloadedTexture = null;
            Debug.LogError("[Legacy image callback] Download failed: " + result.Error);
        }
    }
}
