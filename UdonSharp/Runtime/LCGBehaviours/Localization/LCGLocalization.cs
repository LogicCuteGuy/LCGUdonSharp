using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace UdonSharp
{
    [AddComponentMenu("LCGUdonSharp/Localization/Localization Manager")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LCGLocalization : UdonSharpBehaviour
    {
        [Tooltip("A JSON table: {\"en\": {\"hello\": \"Hello\"}, \"th\": {\"hello\": \"สวัสดี\"}}. Edit with Tools > LCGUdonSharp > Localization.")]
        public TextAsset translations;
        // Unity String Tables are baked into this field before Play Mode/build.
        [HideInInspector] public string bakedTranslations;
        public string defaultLanguage = "en";
        public bool followClientLanguage = true;
        [Tooltip("For SendCustomEvent: set this code, then call SelectLanguage.")]
        public string selectedLanguage;
        public UnityEngine.UI.Dropdown languageDropdown;
        public TMPro.TMP_Dropdown languageTMPDropdown;
        [Tooltip("Dropdown option codes in display order. Empty uses GetLanguages order.")]
        public string[] dropdownLanguages = new string[0];
        public UdonSharpBehaviour[] languageChangedTargets = new UdonSharpBehaviour[0];
        public string languageChangedEvent = "LocalizationChanged";
        public LCGSmartFormatter smartFormatter;
        [HideInInspector] public string[] smartKeys = new string[0];
        public string[] variableNames = new string[0];
        public string[] variableValues = new string[0];
        [HideInInspector] public LCGLocalizedAsset[] localizedAssets = new LCGLocalizedAsset[0];

        private DataDictionary tables;
        private bool initialized;
        private bool manualLanguage;
        private string currentLanguage = "";
        private LCGLocalizedText[] listeners = new LCGLocalizedText[0];

        public string CurrentLanguage { get { EnsureInitialized(); return currentLanguage; } }

        private void Start()
        {
            EnsureInitialized();
            if (!manualLanguage && followClientLanguage) FollowClientLanguage();
            RefreshAll();
        }

        public override void OnLanguageChanged(string language)
        {
            if (followClientLanguage && !manualLanguage) ApplyLanguage(language);
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            tables = new DataDictionary();
            DataToken root;
            string json = !string.IsNullOrEmpty(bakedTranslations) ? bakedTranslations : (translations != null ? translations.text : "");
            if (VRCJson.TryDeserializeFromJson(json, out root)
                && root.TokenType == TokenType.DataDictionary)
                tables = root.DataDictionary;
            else Debug.LogWarning("[LCGLocalization] Missing or invalid translation table.", this);

            currentLanguage = ResolveLanguage(defaultLanguage);
            if (currentLanguage == "")
            {
                string[] languages = GetLanguages();
                if (languages.Length > 0) currentLanguage = languages[0];
            }
            if (followClientLanguage && Networking.LocalPlayer != null)
            {
                string language = ResolveLanguage(VRCPlayerApi.GetCurrentLanguage());
                if (language != "") currentLanguage = language;
            }
        }

        public string[] GetLanguages()
        {
            EnsureInitialized();
            DataList keys = tables.GetKeys();
            string[] languages = new string[keys.Count];
            int count = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                DataToken table;
                if (keys[i].TokenType == TokenType.String && tables.TryGetValue(keys[i], TokenType.DataDictionary, out table))
                    languages[count++] = keys[i].String;
            }
            string[] result = new string[count];
            for (int i = 0; i < count; i++) result[i] = languages[i];
            return result;
        }

        private string ResolveLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return "";
            language = language.Trim().Replace('_', '-').ToLowerInvariant();
            DataToken table;
            if (tables.TryGetValue(language, TokenType.DataDictionary, out table)) return language;
            int separator = language.IndexOf('-');
            if (separator > 0)
            {
                language = language.Substring(0, separator);
                if (tables.TryGetValue(language, TokenType.DataDictionary, out table)) return language;
            }
            return "";
        }

        public string Get(string key) { return GetOrDefault(key, key); }

        public string GetOrDefault(string key, string fallback)
        { return GetWithVariables(key, fallback, variableNames, variableValues); }

        public string GetWithVariables(string key, string fallback, string[] names, string[] values)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(key)) return fallback ?? "";
            string value = Read(currentLanguage, key);
            string formattingLanguage = currentLanguage;
            if (value == "") { formattingLanguage = ResolveLanguage(defaultLanguage); value = Read(formattingLanguage, key); }
            value = value == "" ? (fallback ?? key) : value;
            if (smartFormatter != null && smartKeys != null)
                for (int i = 0; i < smartKeys.Length; i++)
                    if (smartKeys[i] == formattingLanguage + ":" + key) return smartFormatter.Format(value, names, values, formattingLanguage.Split('-')[0]);
            return value;
        }

        public void SetVariable(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < variableNames.Length; i++)
                if (variableNames[i] == name) { variableValues[i] = value ?? ""; RefreshAll(); return; }
            string[] names = new string[variableNames.Length + 1];
            string[] values = new string[names.Length];
            for (int i = 0; i < variableNames.Length; i++) { names[i] = variableNames[i]; values[i] = variableValues[i]; }
            names[names.Length - 1] = name; values[values.Length - 1] = value ?? "";
            variableNames = names; variableValues = values;
            RefreshAll();
        }

        public void RefreshAll()
        {
            for (int i = 0; i < listeners.Length; i++)
                if (listeners[i] != null) listeners[i].RefreshText();
            if (localizedAssets != null)
                for (int i = 0; i < localizedAssets.Length; i++)
                    if (localizedAssets[i] != null) localizedAssets[i].RefreshAsset();
            SyncDropdowns();
        }

        private void SyncDropdowns()
        {
            string[] choices = dropdownLanguages != null && dropdownLanguages.Length > 0 ? dropdownLanguages : GetLanguages();
            for (int i = 0; i < choices.Length; i++)
                if (ResolveLanguage(choices[i]) == currentLanguage)
                {
                    if (languageDropdown != null) languageDropdown.SetValueWithoutNotify(i);
                    if (languageTMPDropdown != null) languageTMPDropdown.SetValueWithoutNotify(i);
                    break;
                }
        }

        private string Read(string language, string key)
        {
            DataToken table;
            DataToken value;
            if (language != "" && tables.TryGetValue(language, TokenType.DataDictionary, out table)
                && table.DataDictionary.TryGetValue(key, TokenType.String, out value)) return value.String;
            return "";
        }

        public bool SetLanguage(string language)
        {
            EnsureInitialized();
            string resolved = ResolveLanguage(language);
            if (resolved == "") return false;
            manualLanguage = true;
            ApplyLanguage(resolved);
            return true;
        }

        public void SelectLanguage() { SetLanguage(selectedLanguage); }

        public bool SetLanguageByIndex(int index)
        {
            string[] languages = GetLanguages();
            return index >= 0 && index < languages.Length && SetLanguage(languages[index]);
        }

        public void SelectDropdownLanguage()
        {
            int index = languageTMPDropdown != null ? languageTMPDropdown.value :
                (languageDropdown != null ? languageDropdown.value : -1);
            string[] languages = dropdownLanguages != null && dropdownLanguages.Length > 0 ? dropdownLanguages : GetLanguages();
            if (index >= 0 && index < languages.Length) SetLanguage(languages[index]);
        }

        private void ApplyLanguage(string language)
        {
            EnsureInitialized();
            string resolved = ResolveLanguage(language);
            // Unsupported client languages use the configured default.
            if (resolved == "") resolved = ResolveLanguage(defaultLanguage);
            if (resolved == "" || currentLanguage == resolved) return;
            currentLanguage = resolved;
            selectedLanguage = resolved;
            RefreshAll();
            if (languageChangedTargets != null && !string.IsNullOrEmpty(languageChangedEvent))
                for (int i = 0; i < languageChangedTargets.Length; i++)
                    if (languageChangedTargets[i] != null) languageChangedTargets[i].SendCustomEvent(languageChangedEvent);
        }

        public void FollowClientLanguage()
        {
            manualLanguage = false;
            ApplyLanguage(Networking.LocalPlayer != null ? VRCPlayerApi.GetCurrentLanguage() : defaultLanguage);
        }

        public void NextLanguage()
        {
            string[] languages = GetLanguages();
            if (languages.Length == 0) return;
            int index = 0;
            for (int i = 0; i < languages.Length; i++)
                if (languages[i] == currentLanguage) index = (i + 1) % languages.Length;
            SetLanguage(languages[index]);
        }

        public void RegisterText(LCGLocalizedText listener)
        {
            if (listener == null) return;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] == listener) return;
            LCGLocalizedText[] next = new LCGLocalizedText[listeners.Length + 1];
            for (int i = 0; i < listeners.Length; i++) next[i] = listeners[i];
            next[listeners.Length] = listener;
            listeners = next;
        }

        public void RegisterAsset(LCGLocalizedAsset binding)
        {
            if (binding == null) return;
            if (localizedAssets == null) localizedAssets = new LCGLocalizedAsset[0];
            for (int i = 0; i < localizedAssets.Length; i++) if (localizedAssets[i] == binding) return;
            LCGLocalizedAsset[] next = new LCGLocalizedAsset[localizedAssets.Length + 1];
            for (int i = 0; i < localizedAssets.Length; i++) next[i] = localizedAssets[i];
            next[next.Length - 1] = binding;
            localizedAssets = next;
        }

        public void UnregisterAsset(LCGLocalizedAsset binding)
        {
            if (localizedAssets == null) return;
            for (int i = 0; i < localizedAssets.Length; i++)
                if (localizedAssets[i] == binding)
                {
                    LCGLocalizedAsset[] next = new LCGLocalizedAsset[localizedAssets.Length - 1];
                    for (int j = 0; j < i; j++) next[j] = localizedAssets[j];
                    for (int j = i + 1; j < localizedAssets.Length; j++) next[j - 1] = localizedAssets[j];
                    localizedAssets = next;
                    return;
                }
        }

        public void UnregisterText(LCGLocalizedText listener)
        {
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != listener) continue;
                LCGLocalizedText[] next = new LCGLocalizedText[listeners.Length - 1];
                for (int j = 0; j < i; j++) next[j] = listeners[j];
                for (int j = i + 1; j < listeners.Length; j++) next[j - 1] = listeners[j];
                listeners = next;
                return;
            }
        }
    }
}
