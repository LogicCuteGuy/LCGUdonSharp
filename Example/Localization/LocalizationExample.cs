using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LocalizationExample : UdonSharpBehaviour
    {
        public LCGLocalization localization;
        public LCGLocalizedText greeting;
        public LCGLanguageButton english;
        public LCGLanguageButton thai;
        public LCGLanguageButton japanese;
        [HideInInspector] public bool checksPassed;
        [HideInInspector] public string checkFailure;
        [HideInInspector] public int languageChangeCount;
        [HideInInspector] public string observedLanguage;
        private int itemCount = 2;
        public LCGLocalizedText smartText;

        public void SelectEnglish() { localization.SetLanguage("en"); }
        public void SelectThai() { localization.SetLanguage("th"); }
        public void SelectJapanese() { localization.SetLanguage("ja"); }
        public void UpdateVariables()
        {
            itemCount++;
            localization.SetVariable("count", itemCount.ToString());
            if (smartText != null) smartText.SetVariable("count", itemCount.ToString());
        }

        public void LocalizationChanged()
        {
            languageChangeCount++;
            observedLanguage = localization.CurrentLanguage;
        }

        // Run through SendCustomEvent in Play Mode to exercise compiled Udon.
        public void RunChecks()
        {
            checksPassed = false;
            checkFailure = "";
            localization.SetLanguage("en");
            Check(localization.Get("hello") == "Hello!", "English lookup");
            localization.SetLanguage("th");
            Check(localization.CurrentLanguage == "th", "Thai button");
            Check(localization.Get("hello") == "สวัสดี!", "Thai lookup");
            Check(greeting.uiText.text == "สวัสดี!", "Visible text refresh");
            Check(localization.Get("fallback") == "This text falls back to English.", "Empty translation fallback");
            localization.selectedLanguage = "ja";
            localization.SelectLanguage();
            Check(localization.Get("hello") == "こんにちは！", "Japanese lookup");
            Check(localization.Get("fallback") == "This text falls back to English.", "Missing translation fallback");
            Check(!localization.SetLanguage("unknown"), "Unsupported language rejected");
            Check(localization.CurrentLanguage == "ja", "Unsupported language preserves selection");
            Check(localization.SetLanguage(" EN_us "), "Regional code");
            Check(localization.CurrentLanguage == "en", "Regional code resolves base language");
            Check(localization.Get("absent") == "absent", "Missing key");
            Check(localization.GetOrDefault("absent", "Original text") == "Original text", "Original text fallback");
            Check(localization.Get(null) == "", "Null key");
            localization.FollowClientLanguage();
            localization.OnLanguageChanged("ja");
            Check(localization.CurrentLanguage == "ja", "Client language callback");
            localization.OnLanguageChanged("unsupported");
            Check(localization.CurrentLanguage == "en", "Unsupported client language uses default");
            greeting.gameObject.SetActive(false);
            localization.SetLanguage("th");
            greeting.gameObject.SetActive(true);
            greeting.RefreshText();
            Check(greeting.uiText.text == "สวัสดี!", "Re-enabled text");
            localization.SetLanguage("en");
            checksPassed = checkFailure == "";
            Debug.Log(checksPassed ? "[LCGLocalization] All runtime checks passed." : "[LCGLocalization] Failed: " + checkFailure);
        }

        private void Check(bool passed, string name)
        {
            if (!passed) checkFailure += name + "; ";
        }
    }
}
