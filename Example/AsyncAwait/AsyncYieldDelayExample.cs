using System.Threading.Tasks;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.AsyncAwait
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AsyncYieldDelayExample : UdonSharpBehaviour
    {
        public override async void Interact()
        {
            Debug.Log("[Async/Await] Interact started.");

            await Task.Yield();
            Debug.Log("[Async/Await] Continued on the next frame.");

            await Task.Delay(1000);
            Debug.Log("[Async/Await] Continued after one second.");
        }
    }
}
