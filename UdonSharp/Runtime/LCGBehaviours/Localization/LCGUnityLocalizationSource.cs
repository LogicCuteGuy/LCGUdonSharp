using UnityEngine;
using VRC.SDKBase;

namespace UdonSharp
{
    /// <summary>Authoring component. Unity String Tables are baked to Udon before running.</summary>
    [AddComponentMenu("LCGUdonSharp/Localization/Unity Localization Source")]
    [DisallowMultipleComponent]
    public sealed class LCGUnityLocalizationSource : MonoBehaviour, IEditorOnly
    {
        public LCGLocalization localization;
        [Tooltip("The Unity Localization String Table Collection to use. This editor asset is removed from the built world.")]
        public Object stringTableCollection;
        public Object[] assetTableCollections = new Object[0];
    }
}
