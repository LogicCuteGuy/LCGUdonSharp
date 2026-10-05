using System;
using System.Linq;
using TMPro;
using UdonSharp;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace UdonSharpEditor
{
    /// <summary>Uses Unity's authoring assets; bakes into the temporary Udon scene.</summary>
    public sealed class LCGUnityLocalizationBridge : IProcessSceneWithReport
    {
        public int callbackOrder => -2000;
        public void OnProcessScene(Scene scene, BuildReport report) { BakeScene(scene, true); }

        [MenuItem("Tools/LCGUdonSharp/Localization/Unity Tables")]
        public static void OpenTables() { EditorApplication.ExecuteMenuItem("Window/Asset Management/Localization Tables"); }

        [MenuItem("Tools/LCGUdonSharp/Localization/Create Unity Manager")]
        private static void CreateManager()
        {
            var go = new GameObject("Unity Localization");
            Undo.RegisterCreatedObjectUndo(go, "Create Unity localization manager");
            var manager = go.AddUdonSharpComponent<LCGLocalization>();
            var source = Undo.AddComponent<LCGUnityLocalizationSource>(go);
            source.localization = manager;
            Selection.activeGameObject = go;
        }

        public static string SerializeCollection(StringTableCollection collection)
        {
            if (collection == null || collection.StringTables.Count == 0)
                throw new BuildFailedException("LCG Localization requires a String Table Collection with at least one locale.");
            var root = new DataDictionary();
            foreach (var table in collection.StringTables)
            {
                var values = new DataDictionary();
                foreach (var entry in table.Values)
                {
                    if (entry.IsSmart) ValidateSmart(entry.Value, table.LocaleIdentifier.Code);
                    values[entry.Key] = entry.Value ?? "";
                }
                root[table.LocaleIdentifier.Code.ToLowerInvariant()] = values;
            }
            DataToken json;
            if (!VRCJson.TrySerializeToJson(root, JsonExportType.Minify, out json))
                throw new BuildFailedException("Could not bake Unity String Tables.");
            return json.String;
        }

        public static void ValidateSmart(string value, string language)
        {
            // Flat selectors, numeric formatting, choose, and one/two-form plurals.
            string remaining = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\{\{|\}\}", "");
            remaining = System.Text.RegularExpressions.Regex.Replace(remaining,
                @"\{[\w]+(?::(?:[FNPEfnpe][0-9]{0,2}|0[0#.,]*|choose\([^{}:]*\):(?:[^{}]|\{\})*|(?:plural|p):(?:[^{}]|\{\})*))?\}", "");
            if (remaining.IndexOf('{') >= 0 || remaining.IndexOf('}') >= 0)
                throw new BuildFailedException("Unsupported Smart String syntax: " + value);
            var plurals = System.Text.RegularExpressions.Regex.Matches(value ?? "", @"\{\w+:(?:plural|p):((?:[^{}]|\{\})*)\}");
            foreach (System.Text.RegularExpressions.Match plural in plurals)
            {
                string baseLocale = language.Split('-')[0];
                int forms = plural.Groups[1].Value.Split('|').Length;
                if (!(baseLocale == "en" && forms == 2) && !((baseLocale == "ja" || baseLocale == "th" || baseLocale == "zh") && forms == 1))
                    throw new BuildFailedException("Plural rules supported for en (two forms), ja/th/zh (one form): " + language);
            }
            foreach (System.Text.RegularExpressions.Match choose in System.Text.RegularExpressions.Regex.Matches(value ?? "", @"\{\w+:choose\(([^{}:]*)\):((?:[^{}]|\{\})*)\}"))
                if (choose.Groups[2].Value.Split('|').Length != choose.Groups[1].Value.Split('|').Length + 1)
                    throw new BuildFailedException("Choose requires one output per choice and a final fallback: " + value);
        }

        public static void ValidateDynamicListener(Component component, string eventField, int index)
        {
            var call = new SerializedObject(component).FindProperty(eventField + ".m_PersistentCalls.m_Calls").GetArrayElementAtIndex(index);
            if (call.FindPropertyRelative("m_Mode").enumValueIndex != 0)
                throw new BuildFailedException("Choose the dynamic property setter, not a fixed event argument: " + component.name);
        }

        private static void BakeAssets(Scene scene, LCGUnityLocalizationSource source, LCGLocalization manager, bool temporary)
        {
            var collections = (source.assetTableCollections ?? new UnityEngine.Object[0]).Select(x => x as AssetTableCollection).ToArray();
            if (collections.Any(x => x == null)) throw new BuildFailedException("Choose valid Asset Table Collections: " + source.name);
            var components = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Component>(true)).Where(x =>
                x is LocalizeSpriteEvent || x is LocalizeTextureEvent || x is LocalizeAudioClipEvent || x is LocalizedGameObjectEvent).ToArray();
            var assigned = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var component in components)
            {
                var reference = (UnityEngine.Localization.LocalizedAssetBase)component.GetType().GetProperty("AssetReference").GetValue(component);
                var collection = LocalizationEditorSettings.GetAssetTableCollection(reference.TableReference);
                if (!collections.Contains(collection)) continue;
                if (reference.LocaleOverride != null) throw new BuildFailedException("Asset locale overrides are not supported: " + component.name);
                var key = collection.SharedData.GetEntryFromReference(reference.TableEntryReference);
                if (key == null) throw new BuildFailedException("Choose a valid asset key: " + component.name);
                var update = (UnityEngine.Events.UnityEventBase)component.GetType().GetProperty("OnUpdateAsset").GetValue(component);
                Type assetType = component is LocalizeSpriteEvent ? typeof(Sprite) : component is LocalizeAudioClipEvent ? typeof(AudioClip) : component is LocalizeTextureEvent ? typeof(Texture) : typeof(GameObject);
                var assets = collection.AssetTables.Select(table =>
                {
                    var entry = table.GetEntry(key.Id);
                    if (entry == null || entry.IsEmpty) return null;
                    string path = AssetDatabase.GUIDToAssetPath(entry.Guid);
                    var candidates = AssetDatabase.LoadAllAssetsAtPath(path).Where(x => assetType.IsInstanceOfType(x));
                    if (entry.IsSubAsset) candidates = candidates.Where(x => x.name == entry.SubAssetName);
                    var resolved = candidates.ToArray();
                    if (resolved.Length != 1) throw new BuildFailedException("Missing or ambiguous localized asset: " + table.LocaleIdentifier.Code + "/" + key.Key);
                    return resolved[0];
                }).ToArray();
                string[] languages = collection.AssetTables.Select(x => x.LocaleIdentifier.Code.ToLowerInvariant()).ToArray();
                if (!languages.Contains(manager.defaultLanguage)) throw new BuildFailedException("Asset table requires the default locale: " + collection.name);
                var targets = new System.Collections.Generic.List<GameObject>();
                if (component is LocalizedGameObjectEvent)
                {
                    if (update.GetPersistentEventCount() > 0) throw new BuildFailedException("Localized prefab callbacks are not supported; use the generated child variants: " + component.name);
                    targets.Add(component.gameObject);
                }
                else
                {
                    for (int j = 0; j < update.GetPersistentEventCount(); j++)
                    {
                        if (update.GetPersistentListenerState(j) == UnityEngine.Events.UnityEventCallState.Off) continue;
                        ValidateDynamicListener(component, "m_UpdateAsset", j);
                        var target = update.GetPersistentTarget(j) as Component;
                        string method = update.GetPersistentMethodName(j);
                        if (target == null || target.gameObject.scene != scene ||
                            !(component is LocalizeSpriteEvent && target is Image && method == "set_sprite" ||
                              component is LocalizeTextureEvent && target is RawImage && method == "set_texture" ||
                              component is LocalizeAudioClipEvent && target is AudioSource && method == "set_clip"))
                            throw new BuildFailedException("Connect localized assets to Image.sprite, RawImage.texture or AudioSource.clip in this scene: " + component.name);
                        targets.Add(target.gameObject);
                    }
                    if (targets.Count == 0) throw new BuildFailedException("Connect On Update Asset: " + component.name);
                }
                foreach (var target in targets.Distinct())
                {
                    if (!assigned.Add(target)) throw new BuildFailedException("Use separate objects for separate localized asset entries: " + target.name);
                    var binding = target.GetComponent<LCGLocalizedAsset>();
                    if (binding == null) { binding = target.AddComponent<LCGLocalizedAsset>(); UdonSharpEditorUtility.RunBehaviourSetup(binding); }
                    if (binding.localization != null && binding.localization != manager) throw new BuildFailedException("Asset already has another manager: " + target.name);
                    binding.localization = manager; binding.languages = languages; binding.assets = assets;
                    binding.enabled = ((Behaviour)component).enabled;
                    if (component is LocalizeSpriteEvent) binding.image = target.GetComponent<Image>();
                    if (component is LocalizeTextureEvent) binding.rawImage = target.GetComponent<RawImage>();
                    if (component is LocalizeAudioClipEvent) binding.audioSource = target.GetComponent<AudioSource>();
                    if (component is LocalizedGameObjectEvent && temporary)
                    {
                        binding.variants = new GameObject[assets.Length];
                        for (int j = 0; j < assets.Length; j++)
                            if (assets[j] != null)
                            {
                                binding.variants[j] = (GameObject)PrefabUtility.InstantiatePrefab(assets[j], target.transform);
                                binding.variants[j].name = "Localized " + languages[j];
                                binding.variants[j].SetActive(false);
                            }
                    }
                    UdonSharpEditorUtility.CopyProxyToUdon(binding);
                }
                if (temporary) UnityEngine.Object.DestroyImmediate(component);
            }
        }

        public static void BakeScene(Scene scene, bool temporary)
        {
            var sources = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LCGUnityLocalizationSource>(true)).ToArray();
            if (sources.Length == 0) return;
            var events = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LocalizeStringEvent>(true)).ToArray();
            var collections = sources.Select(x => x.stringTableCollection as StringTableCollection).ToArray();
            if (collections.Where(x => x != null).GroupBy(x => x).Any(x => x.Count() > 1))
                throw new BuildFailedException("Assign each String Table Collection to only one LCG manager per scene.");
            if (sources.SelectMany(x => x.assetTableCollections ?? new UnityEngine.Object[0]).Where(x => x != null).GroupBy(x => x).Any(x => x.Count() > 1))
                throw new BuildFailedException("Assign each Asset Table Collection to only one LCG manager per scene.");
            // Validate all source data before writing generated state.
            var payloads = collections.Select(SerializeCollection).ToArray();
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var collection = collections[i];
                var manager = source.localization;
                if (manager == null || manager.gameObject.scene != scene)
                    throw new BuildFailedException("Assign a localization manager in the same scene: " + source.name);
                if (!collection.StringTables.Any(x => x.LocaleIdentifier.Code == manager.defaultLanguage))
                    throw new BuildFailedException("Default Language must match a locale in " + collection.TableCollectionName);
                manager.bakedTranslations = payloads[i];
                manager.translations = null;
                manager.smartKeys = collection.StringTables.SelectMany(table => table.Values.Where(x => x.IsSmart).Select(x => table.LocaleIdentifier.Code.ToLowerInvariant() + ":" + x.Key)).Distinct().ToArray();
                if (manager.smartKeys.Length > 0 && manager.smartFormatter == null)
                {
                    manager.smartFormatter = manager.gameObject.GetComponent<LCGSmartFormatter>();
                    if (manager.smartFormatter == null)
                    {
                        manager.smartFormatter = manager.gameObject.AddComponent<LCGSmartFormatter>();
                        UdonSharpEditorUtility.RunBehaviourSetup(manager.smartFormatter);
                    }
                }
                if (manager.variableNames == null || manager.variableValues == null || manager.variableNames.Length != manager.variableValues.Length)
                    throw new BuildFailedException("Variable Names and Values must have matching lengths: " + manager.name);
                UdonSharpEditorUtility.CopyProxyToUdon(manager);
                foreach (var localizeEvent in events)
                {
                    if (localizeEvent.StringReference == null)
                        throw new BuildFailedException("Choose a String Table reference on " + localizeEvent.name);
                    if (LocalizationEditorSettings.GetStringTableCollection(localizeEvent.StringReference.TableReference) != collection) continue;
                    if (localizeEvent.StringReference.LocaleOverride != null)
                        throw new BuildFailedException("Per-text locale overrides are not supported: " + localizeEvent.name);
                    var key = collection.SharedData.GetEntryFromReference(localizeEvent.StringReference.TableEntryReference);
                    if (key == null) throw new BuildFailedException("Choose a valid String Table entry on " + localizeEvent.name);
                    if ((localizeEvent.StringReference.Arguments != null && localizeEvent.StringReference.Arguments.Count > 0)
                        || new SerializedObject(localizeEvent).FindProperty("m_FormatArguments").arraySize > 0)
                        throw new BuildFailedException("Runtime Unity localization variables need Udon formatting: " + localizeEvent.name);
                    var targets = new System.Collections.Generic.List<Component>();
                    for (int j = 0; j < localizeEvent.OnUpdateString.GetPersistentEventCount(); j++)
                    {
                        if (localizeEvent.OnUpdateString.GetPersistentListenerState(j) == UnityEngine.Events.UnityEventCallState.Off) continue;
                        ValidateDynamicListener(localizeEvent, "m_UpdateString", j);
                        var target = localizeEvent.OnUpdateString.GetPersistentTarget(j) as Component;
                        if (!(target is TMP_Text) && !(target is Text) || localizeEvent.OnUpdateString.GetPersistentMethodName(j) != "set_text")
                            throw new BuildFailedException("Only Text.text or TMP_Text.text listeners are supported: " + localizeEvent.name);
                        targets.Add(target);
                    }
                    if (targets.Count == 0) throw new BuildFailedException("Connect On Update String to Text.text or TMP_Text.text: " + localizeEvent.name);
                    foreach (var target in targets.Distinct())
                    {
                        if (target.gameObject.scene != scene)
                            throw new BuildFailedException("Text must be in the same scene: " + target.name);
                        var binding = target.GetComponent<LCGLocalizedText>();
                        if (binding != null && binding.localization != null && binding.localization != manager)
                            throw new BuildFailedException("Text is already assigned to another manager: " + target.name);
                        bool created = binding == null;
                        if (created)
                        {
                            // Leave VM initialization to the SDK after scene processing.
                            binding = target.gameObject.AddComponent<LCGLocalizedText>();
                            UdonSharpEditorUtility.RunBehaviourSetup(binding);
                        }
                        binding.localization = manager;
                        binding.enabled = localizeEvent.enabled;
                        binding.key = key.Key;
                        binding.variableNames = localizeEvent.StringReference.Keys.ToArray();
                        binding.variableValues = localizeEvent.StringReference.Values.Select(v =>
                        {
                            object value = v.GetSourceValue(null);
                            if (value == null || value is string || value is bool || value is int || value is float || value is double || value is long)
                                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                            throw new BuildFailedException("Only scalar local Smart variables are supported: " + localizeEvent.name);
                        }).ToArray();
                        if (target is TMP_Text tmp) { binding.tmpText = tmp; if (created) binding.fallbackText = tmp.text; }
                        if (target is Text text) { binding.uiText = text; if (created) binding.fallbackText = text.text; }
                        UdonSharpEditorUtility.CopyProxyToUdon(binding);
                    }
                    // These are temporary Play Mode/build copies. Authoring events stay intact.
                    if (temporary) UnityEngine.Object.DestroyImmediate(localizeEvent);
                }
                BakeAssets(scene, source, manager, temporary);
                manager.localizedAssets = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LCGLocalizedAsset>(true)).Where(x => x.localization == manager).ToArray();
                UdonSharpEditorUtility.CopyProxyToUdon(manager);
                if (temporary) UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }

    [CustomEditor(typeof(LCGUnityLocalizationSource))]
    internal sealed class LCGUnityLocalizationSourceInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localization"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("assetTableCollections"), true);
            var property = serializedObject.FindProperty("stringTableCollection");
            property.objectReferenceValue = EditorGUILayout.ObjectField("String Table Collection", property.objectReferenceValue, typeof(StringTableCollection), false);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Edit strings in Unity's Localization Tables. Add Localize String Event to your text and connect On Update String to its text property. Play Mode and builds bake automatically into Udon.", MessageType.Info);
            if (GUILayout.Button("Open Unity Localization Tables")) LCGUnityLocalizationBridge.OpenTables();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                if (GUILayout.Button("Bake / Validate Now"))
                {
                    var source = (LCGUnityLocalizationSource)target;
                    LCGUnityLocalizationBridge.BakeScene(source.gameObject.scene, false);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
                }
        }
    }
}
