using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncGpuReadbackExample : UdonSharpBehaviour
    {
        [SerializeField] private Texture source;

        private bool _lastRequestHadError;

        public override async void Interact()
        {
            await VRCAsync.RequestGPUReadbackAsync(source);
            Debug.Log("[Async GPU] Readback callback completed. Has error: " + _lastRequestHadError);
        }

        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            _lastRequestHadError = request.hasError;
            Debug.Log("[Legacy GPU callback] Readback completed.");
        }
    }
}
