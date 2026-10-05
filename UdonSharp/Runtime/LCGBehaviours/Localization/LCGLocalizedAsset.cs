using UnityEngine;
using UnityEngine.UI;

namespace UdonSharp
{
    [AddComponentMenu("LCGUdonSharp/Localization/Localized Asset")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LCGLocalizedAsset : UdonSharpBehaviour
    {
        public LCGLocalization localization;
        public string[] languages = new string[0];
        public Object[] assets = new Object[0];
        public Image image;
        public RawImage rawImage;
        public AudioSource audioSource;
        public Renderer targetRenderer;
        [Tooltip("Scene objects, one per language. Keep this binding on a parent that stays active.")]
        public GameObject[] variants = new GameObject[0];
        public bool playAudioOnChange;
        private Object originalAsset;
        private bool captured;
        private void Start() { RefreshAsset(); }
        private void OnEnable() { RefreshAsset(); }
        private void OnDisable() { if (localization != null) localization.UnregisterAsset(this); }
        public void RefreshAsset()
        {
            if (localization == null || !isActiveAndEnabled) return;
            localization.RegisterAsset(this);
            if (!captured)
            {
                if (image != null) originalAsset = image.sprite;
                else if (rawImage != null) originalAsset = rawImage.texture;
                else if (audioSource != null) originalAsset = audioSource.clip;
                else if (targetRenderer != null) originalAsset = targetRenderer.sharedMaterial;
                captured = true;
            }
            int index = -1, fallback = -1;
            for (int i = 0; i < languages.Length; i++)
            {
                if (languages[i] == localization.CurrentLanguage) index = i;
                if (languages[i] == localization.defaultLanguage) fallback = i;
            }
            if (index < 0 || (index < assets.Length && assets[index] == null && variants.Length == 0)) index = fallback;
            if (variants.Length > 0 && (index < 0 || index >= variants.Length || variants[index] == null)) index = fallback;
            Object asset = index >= 0 && index < assets.Length ? assets[index] : null;
            if (asset == null) asset = originalAsset;
            if (image != null) image.sprite = (Sprite)asset;
            if (rawImage != null) rawImage.texture = (Texture)asset;
            if (audioSource != null && audioSource.clip != (AudioClip)asset)
            {
                audioSource.Stop(); audioSource.clip = (AudioClip)asset;
                if (playAudioOnChange && audioSource.clip != null) audioSource.Play();
            }
            if (targetRenderer != null) targetRenderer.sharedMaterial = (Material)asset;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null) variants[i].SetActive(i == index);
        }
    }
}
