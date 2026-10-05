using UnityEngine;

namespace UdonSharp
{
    [AddComponentMenu("LCGUdonSharp/Localization/Language Button")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LCGLanguageButton : UdonSharpBehaviour
    {
        public LCGLocalization localization;
        [Tooltip("Language code, e.g. en, th, ja. Empty cycles through available languages.")]
        public string language;

        public override void Interact() { SelectLanguage(); }

        public void SelectLanguage()
        {
            if (localization == null) return;
            if (string.IsNullOrEmpty(language)) localization.NextLanguage();
            else localization.SetLanguage(language);
        }
    }
}
