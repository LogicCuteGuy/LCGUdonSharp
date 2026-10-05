using UnityEngine;
using UnityEngine.Localization.Components;
using VRC.SDKBase;

namespace UdonSharp
{
    /// <summary>Unity prefab localization with safe cleanup of its owned editor preview.</summary>
    [AddComponentMenu("LCGUdonSharp/Localization/Localize Prefab Event")]
    public sealed class LCGLocalizePrefabEvent : LocalizedGameObjectEvent, IEditorOnly
    {
        private GameObject current;

        protected override void UpdateAsset(GameObject localizedAsset)
        {
            ClearInstance();
            if (localizedAsset != null)
            {
                current = Instantiate(localizedAsset, transform);
                current.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }
            OnUpdateAsset.Invoke(current);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ClearInstance();
        }

        private void ClearInstance()
        {
            if (current == null) return;
            if (Application.isPlaying) Destroy(current);
            else DestroyImmediate(current);
            current = null;
        }
    }
}
