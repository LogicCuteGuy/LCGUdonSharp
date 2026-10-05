# LCG Localization

> Documentation version: **0.3.9** · [All examples](../README.md) · [Package guide](../../README.md)

The primary workflow now uses **Unity Localization**. See [Unity String Tables, scene example and setup](UnityLocalization.md). The instructions below describe the retained legacy JSON workflow, inspired by [ikuko/ULocalization](https://github.com/ikuko/ULocalization). For Smart Strings, sprite/texture/audio and GameObject localization, use the Unity workflow linked above.

## Quick setup (no code)

1. Open **Tools > LCGUdonSharp > Localization > Legacy JSON Table** and click **Create Localization Manager**.
2. Click **Save Table** and save the JSON in your project's `Assets` folder.
3. Select the text objects or their parent in the Hierarchy, then click **Bind Selected Text**. TextMeshPro and legacy UI Text are supported, including inactive children. Existing text becomes the default translation; keys are generated automatically. Text assigned to another manager is left alone.
4. Enter translations in the table. Add any language code such as `en`, `th`, `ja` or `zh-hant`. Click **Save Table**, then save your scene. The component Inspector also provides a key picker.
5. For a language button, add **LCG Language Button**, assign the manager and set its language code. For Unity UI Button, click **Wire UI Button** in its Inspector. A collider-based world button uses `Interact()` automatically. Leave the code empty to cycle languages.

**Default Language** controls missing-translation fallback. **Follow Client Language** starts with VRChat's selected language and follows `OnLanguageChanged` until the player manually selects a language. Call `FollowClientLanguage()` to return to automatic selection. Language choice is local; it is not synced or persisted between visits. A regional code such as `en-US` falls back to `en`. An unsupported client language uses the default language.

Empty or missing translations use the default language, then the text component's **Fallback Text**. `Get()` returns the key when it is missing. Calls before `Start()` initialize safely. Tables are read once per manager at runtime; restarting Play Mode applies file edits.

TextMeshPro fonts need glyphs for every language you display. Add suitable font assets/fallback fonts in your project. Localization changes the text, not the font.

## From UdonSharp

```csharp
using UdonSharp;

public class Example : UdonSharpBehaviour
{
    public LCGLocalization localization;

    public override void Interact()
    {
        localization.SetLanguage("th");
        UnityEngine.Debug.Log(localization.Get("hello"));
    }
}
```

Also available: `CurrentLanguage`, `GetLanguages()`, `GetOrDefault(key, fallback)`, `NextLanguage()` and `FollowClientLanguage()`. `SetLanguage()` returns `false` for unsupported codes without changing the selection. Variables can be formatted explicitly, e.g. `string.Format(localization.Get("score"), score)` with `"Score: {0}"` in the table.

## Example and checks

Open `LocalizationExample.unity` in this folder and enter Play Mode. Its English/Thai/Japanese buttons are already connected. **LocalizationExample** contains a **RunChecks** event with a `checksPassed` result; it exercises compiled Udon lookup, language buttons, simulated client language callbacks, regional codes, missing/empty translations and re-enabled text. For early calls, malformed tables and editor validation run **LCGLocalizationTests** in Unity's EditMode Test Runner.

The example uses legacy UI Text with Unity's built-in font. For a production multilingual UI, provide your own multilingual font assets as described above.
