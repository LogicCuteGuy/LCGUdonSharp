using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UdonSharp;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace UdonSharpEditor
{
    public sealed class LCGLocalizationWindow : EditorWindow
    {
        private LCGLocalization manager;
        private DataDictionary table = new DataDictionary();
        private Vector2 scroll;
        private string newLanguage = "ja", newKey = "", filter = "";
        private bool dirty { get { return hasUnsavedChanges; } set { hasUnsavedChanges = value; } }
        private string error;

        private void OnEnable() { saveChangesMessage = "Save your translation table before closing?"; }
        public override void SaveChanges() { SaveTable(); if (!dirty) base.SaveChanges(); }

        [MenuItem("Tools/LCGUdonSharp/Localization/Legacy JSON Table")]
        public static void Open()
        {
            var window = GetWindow<LCGLocalizationWindow>("LCG Localization");
            window.minSize = new Vector2(640, 360);
            if (window.manager == null)
            {
                window.manager = FindObjectsOfType<LCGLocalization>(true).FirstOrDefault();
                window.LoadTable();
            }
        }

        public static void Open(LCGLocalization localization)
        {
            Open();
            var window = GetWindow<LCGLocalizationWindow>();
            if (window.manager == localization) return;
            if (!window.CanDiscard()) return;
            window.manager = localization;
            window.LoadTable();
        }

        private bool CanDiscard()
        {
            return !dirty || EditorUtility.DisplayDialog("Unsaved translations", "Discard the changes in this table?", "Discard", "Keep editing");
        }

        private void LoadTable()
        {
            dirty = false;
            error = null;
            table = new DataDictionary();
            if (manager == null || manager.translations == null) return;
            DataToken root;
            if (!VRCJson.TryDeserializeFromJson(manager.translations.text, out root)
                || root.TokenType != TokenType.DataDictionary || !ValidateTable(root.DataDictionary, out error))
            {
                error = error ?? "Invalid JSON: expected language dictionaries containing string translations.";
                return;
            }
            table = root.DataDictionary;
        }

        public static bool ValidateTable(DataDictionary value, out string message)
        {
            message = null;
            foreach (DataToken language in value.GetKeys().ToArray())
            {
                DataToken entries;
                if (language.TokenType != TokenType.String || !Regex.IsMatch(language.String, "^[a-z]{2,3}(-[a-z0-9]{2,8})*$")
                    || !value.TryGetValue(language, TokenType.DataDictionary, out entries))
                { message = "Use lowercase language codes (en, th, ja, zh-hant) and a dictionary for each language."; return false; }
                foreach (DataToken key in entries.DataDictionary.GetKeys().ToArray())
                {
                    DataToken text;
                    if (key.TokenType != TokenType.String || string.IsNullOrWhiteSpace(key.String)
                        || !entries.DataDictionary.TryGetValue(key, TokenType.String, out text))
                    { message = "Translation keys must be nonempty and every translation must be a string."; return false; }
                }
            }
            return true;
        }

        private string[] Languages() { return table.GetKeys().ToArray().Select(x => x.String).ToArray(); }
        private string[] Keys()
        {
            return Languages().SelectMany(x => table[x].DataDictionary.GetKeys().ToArray().Select(k => k.String))
                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("1. Choose a manager   2. Edit translations   3. Bind selected text", EditorStyles.boldLabel);
            var selected = (LCGLocalization)EditorGUILayout.ObjectField("Manager", manager, typeof(LCGLocalization), true);
            if (selected != manager && CanDiscard()) { manager = selected; LoadTable(); }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Create Localization Manager"))
                {
                    if (!CanDiscard()) return;
                    var go = new GameObject("LCG Localization");
                    Undo.RegisterCreatedObjectUndo(go, "Create localization manager");
                    manager = go.AddUdonSharpComponent<LCGLocalization>();
                    table = new DataDictionary();
                    table["en"] = new DataDictionary(); table["th"] = new DataDictionary();
                    table["en"].DataDictionary["hello"] = "Hello!";
                    table["th"].DataDictionary["hello"] = "สวัสดี!";
                    dirty = true; error = null;
                    Selection.activeGameObject = go;
                }
            }
            if (manager == null) return;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                var asset = (TextAsset)EditorGUILayout.ObjectField("Translation file", manager.translations, typeof(TextAsset), false);
                if (asset != manager.translations && CanDiscard())
                {
                    Undo.RecordObject(manager, "Assign translation table");
                    manager.translations = asset;
                    UdonSharpEditorUtility.CopyProxyToUdon(manager);
                    EditorUtility.SetDirty(manager);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                    LoadTable();
                }
            }
            if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                if (GUILayout.Button("Reload") && CanDiscard()) LoadTable();
                return;
            }
            EditorGUILayout.HelpBox("Changes are saved with Save Table. Empty translations fall back to the default language, then the original text. Language choice is local to each player.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(dirty ? "Save Table *" : "Save Table")) SaveTable();
                    if (GUILayout.Button("Reload") && CanDiscard()) LoadTable();
                    if (GUILayout.Button("Bind Selected Text")) BindSelected();
                    if (GUILayout.Button("Preview Default")) PreviewDefault();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    newLanguage = EditorGUILayout.TextField("New language", newLanguage);
                    if (GUILayout.Button("Add Language", GUILayout.Width(110)))
                    {
                        string code = newLanguage.Trim().Replace('_', '-').ToLowerInvariant();
                        if (!Regex.IsMatch(code, "^[a-z]{2,3}(-[a-z0-9]{2,8})*$")) ShowNotification(new GUIContent("Use a language code such as en, th or ja."));
                        else if (!table.ContainsKey(code)) { table[code] = new DataDictionary(); dirty = true; }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    newKey = EditorGUILayout.TextField("New key", newKey);
                    if (GUILayout.Button("Add Key", GUILayout.Width(110)))
                    {
                        if (!string.IsNullOrWhiteSpace(newKey) && table.Count > 0)
                        {
                            foreach (string language in Languages())
                                if (!table[language].DataDictionary.ContainsKey(newKey.Trim())) table[language].DataDictionary[newKey.Trim()] = "";
                            newKey = ""; dirty = true;
                        }
                    }
                }
                filter = EditorGUILayout.TextField("Search key", filter);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                string[] languages = Languages();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Key", GUILayout.Width(170));
                    foreach (string language in languages) GUILayout.Label(language, GUILayout.Width(220));
                }
                foreach (string key in Keys())
                {
                    if (key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(key, GUILayout.Width(170));
                        foreach (string language in languages)
                        {
                            var entries = table[language].DataDictionary;
                            DataToken token;
                            string before = entries.TryGetValue(key, out token) ? token.String : "";
                            string after = EditorGUILayout.TextArea(before, GUILayout.Width(220), GUILayout.MinHeight(38));
                            if (after != before) { entries[key] = after; dirty = true; }
                        }
                        if (GUILayout.Button("×", GUILayout.Width(24)))
                        { foreach (string language in languages) table[language].DataDictionary.Remove(key); dirty = true; }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void SaveTable()
        {
            if (manager == null) return;
            if (!ValidateTable(table, out error)) return;
            string path = AssetDatabase.GetAssetPath(manager.translations);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                path = EditorUtility.SaveFilePanelInProject("Save translation table", "Translations", "json", "Choose a folder in Assets.");
            if (string.IsNullOrEmpty(path)) return;
            DataToken json;
            if (!VRCJson.TrySerializeToJson(table, JsonExportType.Beautify, out json)) { error = "Could not serialize translation table."; return; }
            File.WriteAllText(path, json.String + "\n", new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Undo.RecordObject(manager, "Assign translation table");
            manager.translations = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            EditorUtility.SetDirty(manager);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            dirty = false;
        }

        private void BindSelected()
        {
            if (table.Count == 0) { ShowNotification(new GUIContent("Add a language first.")); return; }
            if (string.IsNullOrEmpty(manager.defaultLanguage) || !table.ContainsKey(manager.defaultLanguage))
            { ShowNotification(new GUIContent("Set the manager's default language to a table language first.")); return; }
            var components = Selection.gameObjects.Where(x => x.scene == manager.gameObject.scene)
                .SelectMany(x => x.GetComponentsInChildren<Component>(true))
                .Where(x => x is TMP_Text || x is Text).Distinct().ToArray();
            foreach (var component in components)
            {
                var binding = component.GetComponent<LCGLocalizedText>();
                bool created = binding == null;
                if (created)
                {
                    binding = component.gameObject.AddUdonSharpComponent<LCGLocalizedText>();
                    Undo.RegisterCreatedObjectUndo(UdonSharpEditorUtility.GetBackingUdonBehaviour(binding), "Localize text");
                    Undo.RegisterCreatedObjectUndo(binding, "Localize text");
                }
                else if (binding.localization != null && binding.localization != manager) continue;
                Undo.RecordObject(binding, "Bind localized text");
                binding.localization = manager;
                if (component is TMP_Text tmp) { binding.tmpText = tmp; if (created) binding.fallbackText = tmp.text; }
                if (component is Text text) { binding.uiText = text; if (created) binding.fallbackText = text.text; }
                if (string.IsNullOrEmpty(binding.key))
                {
                    string stem = Regex.Replace(component.name.ToLowerInvariant(), "[^a-z0-9_.-]", "_");
                    string key = stem; int suffix = 2;
                    while (Keys().Contains(key)) key = stem + "_" + suffix++;
                    binding.key = key;
                }
                var entries = table[manager.defaultLanguage].DataDictionary;
                if (!entries.ContainsKey(binding.key)) { entries[binding.key] = binding.fallbackText ?? ""; dirty = true; }
                UdonSharpEditorUtility.CopyProxyToUdon(binding);
                EditorUtility.SetDirty(binding);
            }
            if (components.Length > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            ShowNotification(new GUIContent("Selected text bound. Save Table to keep new keys."));
        }

        private void PreviewDefault()
        {
            if (!table.ContainsKey(manager.defaultLanguage)) return;
            foreach (var binding in FindObjectsOfType<LCGLocalizedText>(true).Where(x => x.localization == manager))
            {
                DataToken value;
                string text = !string.IsNullOrEmpty(binding.key) && table[manager.defaultLanguage].DataDictionary.TryGetValue(binding.key, TokenType.String, out value)
                    && !string.IsNullOrEmpty(value.String) ? value.String : binding.fallbackText;
                if (binding.tmpText != null) { Undo.RecordObject(binding.tmpText, "Preview translation"); binding.tmpText.text = text; EditorUtility.SetDirty(binding.tmpText); }
                if (binding.uiText != null) { Undo.RecordObject(binding.uiText, "Preview translation"); binding.uiText.text = text; EditorUtility.SetDirty(binding.uiText); }
            }
        }
    }

    [CustomEditor(typeof(LCGLocalization))]
    internal sealed class LCGLocalizationInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            if (((LCGLocalization)target).GetComponent<LCGUnityLocalizationSource>() != null)
            {
                serializedObject.Update();
                DrawPropertiesExcluding(serializedObject, "m_Script", "translations");
                serializedObject.ApplyModifiedProperties();
                if (GUILayout.Button("Edit Unity String Tables")) LCGUnityLocalizationBridge.OpenTables();
            }
            else
            {
                DrawDefaultInspector();
                if (GUILayout.Button("Edit Translation Table")) LCGLocalizationWindow.Open((LCGLocalization)target);
            }
            var manager = (LCGLocalization)target;
            if ((manager.languageDropdown != null || manager.languageTMPDropdown != null) && GUILayout.Button("Wire Language Dropdown"))
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                if (manager.languageDropdown != null) WireDropdown(manager.languageDropdown, backing);
                if (manager.languageTMPDropdown != null) WireDropdown(manager.languageTMPDropdown, backing);
            }
        }

        private static void WireDropdown(UnityEngine.Object control, VRC.Udon.UdonBehaviour backing)
        {
            var serialized = new SerializedObject(control);
            var calls = serialized.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_Target").objectReferenceValue == backing &&
                    call.FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent" &&
                    call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "SelectDropdownLanguage") return;
            }
            Undo.RecordObject(control, "Wire language dropdown");
            if (control is Dropdown dropdown) UnityEventTools.AddStringPersistentListener(dropdown.onValueChanged, backing.SendCustomEvent, "SelectDropdownLanguage");
            if (control is TMP_Dropdown tmp) UnityEventTools.AddStringPersistentListener(tmp.onValueChanged, backing.SendCustomEvent, "SelectDropdownLanguage");
            EditorUtility.SetDirty(control);
        }
    }

    [CustomEditor(typeof(LCGLocalizedText))]
    internal sealed class LCGLocalizedTextInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            DrawDefaultInspector();
            var binding = (LCGLocalizedText)target;
            if (binding.localization == null) EditorGUILayout.HelpBox("Choose a manager or use Bind Selected Text in the Localization window.", MessageType.Warning);
            else if (binding.localization.translations != null)
            {
                DataToken root;
                if (VRCJson.TryDeserializeFromJson(binding.localization.translations.text, out root) && root.TokenType == TokenType.DataDictionary)
                {
                    var keys = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (DataToken language in root.DataDictionary.GetValues().ToArray())
                        if (language.TokenType == TokenType.DataDictionary)
                            foreach (DataToken key in language.DataDictionary.GetKeys().ToArray()) keys.Add(key.String);
                    string[] choices = new[] { "Choose key…" }.Concat(keys).ToArray();
                    int index = Math.Max(0, Array.IndexOf(choices, binding.key));
                    int selected = EditorGUILayout.Popup("Available keys", index, choices);
                    if (selected > 0 && selected != index)
                    {
                        serializedObject.Update(); serializedObject.FindProperty("key").stringValue = choices[selected]; serializedObject.ApplyModifiedProperties();
                    }
                }
                if (GUILayout.Button("Edit Translation Table")) LCGLocalizationWindow.Open(binding.localization);
            }
        }
    }

    [CustomEditor(typeof(LCGLanguageButton))]
    internal sealed class LCGLanguageButtonInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            DrawDefaultInspector();
            var selector = (LCGLanguageButton)target;
            var button = selector.GetComponent<Button>();
            if (button != null && GUILayout.Button("Wire UI Button"))
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(selector);
                Undo.RecordObject(button, "Wire language button");
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentTarget(i) == backing && button.onClick.GetPersistentMethodName(i) == "SendCustomEvent") return;
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "SelectLanguage");
                EditorUtility.SetDirty(button);
            }
        }
    }
}
