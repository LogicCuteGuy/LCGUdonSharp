# Unity Localization → Udon

> Documentation version: **0.3.9** · [All examples](../README.md) · [Package guide](../../README.md)

Use Unity Localization's **Locales**, **String Table Collection** and **Localize String Event** to author your UI. LCGUdonSharp bakes table values and text connections into the temporary Play Mode/build scene. The authoring scene retains the native components; the built world uses Udon language selection without loading Addressables.

## Example in the scene

Open `Example/TestLCGUdonSharp.unity` and select **UnityLocalizationExample**, at `(17, 0, 0)`. Enter Play Mode and use its English/Thai/Japanese buttons. The prefab is also available as `UnityLocalizationExample.prefab` in this folder.

Edit **LCG Example Strings** using **Window > Asset Management > Localization Tables** or **Tools > LCGUdonSharp > Localization > Unity Tables**. Native table assets and locales are under `UnityTables`. Re-enter Play Mode to bake edits automatically. The example font is a subset of Noto Sans JP/Thai under the included OFL licenses; provide full fonts when adding other text.

## Setup for your own UI

1. Create locales and a String Table Collection in Unity Localization.
2. Choose **Tools > LCGUdonSharp > Localization > Create Unity Manager**. Assign the collection on **Unity Localization Source**. Set **Default Language** on the manager to a locale code in the collection.
3. Add Unity's **Localize String Event** to TextMeshPro or UI Text. Choose the collection and key, then connect **On Update String** to the dynamic `Text.text` / `TMP_Text.text` setter.
4. Call `manager.SetLanguage("th")` from your Udon script, or wire a dropdown as described below. **LCG Language Button** remains an optional convenience.
5. Save the scene and play. **Bake / Validate Now** on the source component checks connections before playing.

One manager per collection per scene is supported. Text and manager must belong to the same scene; inactive text is included. Smart Strings and Asset Tables support is described below. Unsupported configurations stop baking.

Language choice follows the VRChat client until manually selected. It is local and is not persisted across visits. Empty/missing translations fall back to the default language and then the original text/key. The existing `LCGLocalization.Get("key")`, `SetLanguage("th")`, `GetOrDefault()`, `NextLanguage()` and `FollowClientLanguage()` methods remain available.

## Dependencies and checks

This project uses Unity Localization 1.4.5 and Scriptable Build Pipeline 1.21.25 on Unity 2022.3. The compatibility hook excludes automatically referenced plugin DLLs from SBP's editor assembly while preserving its explicit Unity assembly references. This prevents the SDK's global `ExtensionMethods` class from shadowing SBP's helper. The SDK DLL itself is unchanged.

Run **LCGUnityLocalizationTests** and **LCGLocalizationTests** in Unity's EditMode Test Runner. The example component's `RunChecks` event exercises compiled Udon buttons, translations, fallbacks and re-enabled text in Play Mode.
## Calling the manager without a Language Button

`LCGLanguageButton` is optional. Your Udon script can call the manager from any event:

```csharp
public LCGLocalization localization;
public void OnSomethingHappened()
{
    localization.SetLanguage("th");
    localization.SetVariable("name", "Player");
    localization.SetVariable("count", "3");
}
```

Other calls: `SetLanguageByIndex(index)`, `NextLanguage()`, `FollowClientLanguage()` and `RefreshAll()`. Invalid codes/indices preserve the current language. For a parameterless Udon event, set `selectedLanguage` through `SetProgramVariable`, then send `SelectLanguage`. Unity UI events should target the backing **Udon Behaviour > SendCustomEvent**, with the event name; a direct proxy callback does not survive a VRChat build.

Assign `Language Dropdown` or `Language TMP Dropdown`, provide the option codes in `Dropdown Languages`, then click **Wire Language Dropdown** on the manager. This adds the `SelectDropdownLanguage` event once. Language changes update the dropdown without triggering its event again. Set `Language Changed Targets` to Udon scripts and implement `LocalizationChanged()` (or change `Language Changed Event`). Read `localization.CurrentLanguage` inside that event. Notifications occur only when the language changes; variables refresh text without changing the language.

## Smart Strings and variables

Mark entries **Smart** in Unity's String Tables. The Udon bridge supports:

- Named and numbered scalar placeholders: `{name}`, `{0}`.
- Numeric formats: `{count:000}`, `{score:F2}`, `{ratio:P0}`. Number formatting uses invariant culture.
- Choose with a required fallback: `{state:choose(on|off):Enabled|Disabled|Unknown}`.
- English plural with two forms: `{count:plural:One item|{} items}`.
- Thai/Japanese/Chinese plural with one form: `{count:plural:{} items}`.
- Escaped braces: `{{name}}`. Missing variables retain their placeholder; inserted values are literal text.

Set global values using `manager.SetVariable(name, value)`; values are strings, so pass numbers as strings using invariant formatting. Set initial values in matching `Variable Names` / `Variable Values` arrays. Scalar local variables on Unity's `Localize String Event` are baked into the text binding; they override globals. Change those using `binding.SetVariable(name, value)`. `GetWithVariables(key, fallback, names, values)` formats a lookup with explicit values without changing state.

Reflection selectors, persistent global Unity variable groups, runtime Unity argument objects, date/list/conditional/custom formatters, nested named placeholders and plural rules for other locales are not supported. Unsupported syntax stops baking. This is a subset of [Unity Smart Strings](https://docs.unity3d.com/Packages/com.unity.localization@1.4/manual/Smart/SmartStrings.html), implemented in Udon.

## Images, textures, audio and GameObjects

Add Unity **Asset Table Collections** to the source's `Asset Table Collections` array. Add the native components and connect their dynamic events:

- **Localize Sprite Event** → `Image.sprite`.
- **Localize Texture Event** → `RawImage.texture`.
- **Localize Audio Clip Event** → `AudioSource.clip`.
- **LCGUdonSharp > Localization > Localize Prefab Event** (`LCGLocalizePrefabEvent`) → no callback is required. This inherits Unity's prefab event and safely removes its owned preview with `DestroyImmediate` in Edit Mode; Unity Localization 1.4.5's original event uses `Destroy` and can log an editor error when switching previews. The bake creates one child per locale and toggles the appropriate variant. These variants are local; no network spawning is performed.

Asset entries are resolved at bake time, including named subassets. Missing translations use the default locale, then the original component asset. Assets are included directly in the world; this increases build size when many locale variants are included. Repeated bakes use the authoring assets, and native events remain editable in the source scene. Arbitrary native asset callbacks and per-asset locale overrides are rejected.

For existing scene GameObjects or materials, add **LCG Localized Asset** on an always-active parent, assign the manager, locale codes and matching `Variants` or `Assets` arrays. Materials use `Target Renderer`; sprites/textures/clips use their corresponding component fields. Each binding holds one localized asset set. The manager discovers these bindings at bake time. `Play Audio On Change` optionally starts the selected clip; by default it only changes the clip and stops previous playback.

The main example now includes direct script calls without Language Button components, a dropdown, a variable update button, localized sprite/texture, three short audio tones and prefab variants. Audio tones demonstrate clip switching; they are not spoken translations. Run `.codex-validation/verify-expanded-localization.cs` through the project Unity bridge in Play Mode for the integration check.
