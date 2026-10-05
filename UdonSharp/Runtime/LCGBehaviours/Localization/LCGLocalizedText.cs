using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UdonSharp
{
    [AddComponentMenu("LCGUdonSharp/Localization/Localized Text")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LCGLocalizedText : UdonSharpBehaviour
    {
        public LCGLocalization localization;
        public string key;
        public TMP_Text tmpText;
        public Text uiText;
        [TextArea] public string fallbackText;
        public string[] variableNames = new string[0];
        public string[] variableValues = new string[0];

        private void OnEnable() { RefreshText(); }
        private void Start() { RefreshText(); }
        private void OnDisable()
        {
            if (localization != null) localization.UnregisterText(this);
        }

        public void RefreshText()
        {
            if (localization == null) return;
            if (isActiveAndEnabled) localization.RegisterText(this);
            string value;
            if (variableNames != null && variableNames.Length > 0)
            {
                int localCount = variableNames.Length;
                string[] names = new string[localCount + localization.variableNames.Length];
                string[] values = new string[names.Length];
                for (int i = 0; i < localCount; i++) { names[i] = variableNames[i]; values[i] = i < variableValues.Length ? variableValues[i] : ""; }
                for (int i = 0; i < localization.variableNames.Length; i++) { names[localCount + i] = localization.variableNames[i]; values[localCount + i] = localization.variableValues[i]; }
                value = localization.GetWithVariables(key, fallbackText, names, values);
            }
            else value = localization.GetOrDefault(key, fallbackText);
            if (tmpText != null) tmpText.text = value;
            if (uiText != null) uiText.text = value;
        }

        public void SetVariable(string name, string value)
        {
            for (int i = 0; i < variableNames.Length && i < variableValues.Length; i++)
                if (variableNames[i] == name) { variableValues[i] = value ?? ""; RefreshText(); return; }
        }
    }
}
