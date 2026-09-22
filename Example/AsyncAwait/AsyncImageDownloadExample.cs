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

        private void Start()
        {
            _downloader = new VRCImageDownloader();
        }

        public override async void Interact()
        {
            Debug.Log("[Async image] Starting VRCImageDownloader request.");
            await VRCAsync.LoadImageAsync(_downloader, imageUrl, targetMaterial);
            Debug.Log("[Async image] Await continuation ran after the legacy callback. Texture: " +
                      _lastDownloadedTexture);
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
