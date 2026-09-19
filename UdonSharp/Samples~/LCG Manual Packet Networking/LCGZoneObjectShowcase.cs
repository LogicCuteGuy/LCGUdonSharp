using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace LCGUdonSharp.Examples.ManualPacketNetworking
{
    /// <summary>
    /// Controls a VRC_ObjectSync child of an LCGNetworkZone. The build processor
    /// replaces VRC_ObjectSync with the generated manual relay in the build copy.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class LCGZoneObjectShowcase : UdonSharpBehaviour
    {
        [SerializeField] private GameObject syncedObject;
        [SerializeField] private float moveDistance = 0.5f;
        [SerializeField] private float rotationDegrees = 30f;

        public void TakeOwnership()
        {
            if (syncedObject == null)
                return;

            // The generated Zone ownership guard rejects players outside the Zone.
            Networking.SetOwner(Networking.LocalPlayer, syncedObject);
        }

        public void MoveAndSync()
        {
            if (!CanEditObject())
                return;

            syncedObject.transform.position += syncedObject.transform.right * moveDistance;
            LCGNetwork.RequestObjectSync(syncedObject);
        }

        public void RotateAndSync()
        {
            if (!CanEditObject())
                return;

            syncedObject.transform.Rotate(0f, rotationDegrees, 0f, Space.World);
            LCGNetwork.RequestObjectSync(syncedObject);
        }

        public void RepositionAndSync()
        {
            if (!CanEditObject())
                return;

            syncedObject.transform.position = transform.position + transform.forward * 2f;
            syncedObject.transform.rotation = transform.rotation;
            LCGNetwork.RequestObjectSync(syncedObject);
        }

        public void MoveLocalOnly()
        {
            if (!CanEditObject())
                return;

            // Deliberately no RequestObjectSync: other players must not see this move.
            syncedObject.transform.position += Vector3.up * moveDistance;
        }

        private bool CanEditObject()
        {
            if (syncedObject == null)
            {
                Debug.LogWarning("[LCG object showcase] Assign Synced Object first.");
                return false;
            }

            if (!Networking.IsOwner(syncedObject))
            {
                Debug.LogWarning("[LCG object showcase] Take ownership first.");
                return false;
            }

            return true;
        }
    }
}
