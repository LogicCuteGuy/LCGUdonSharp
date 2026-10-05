using NUnit.Framework;
using UdonSharp;
using UdonSharpEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGLocalizationTests
    {
        private GameObject root;
        private TextAsset data;

        [TearDown]
        public void Cleanup()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (data != null) Object.DestroyImmediate(data);
        }

        private LCGLocalization Create(string json)
        {
            root = new GameObject("Localization test");
            var manager = root.AddComponent<LCGLocalization>();
            manager.followClientLanguage = false;
            data = new TextAsset(json);
            manager.translations = data;
            return manager;
        }

        [Test]
        public void EarlyCalls_RegionalCodes_AndFallbacks()
        {
            var manager = Create("{\"en\":{\"hello\":\"Hello\",\"fallback\":\"Default\"},\"th\":{\"hello\":\"สวัสดี\",\"fallback\":\"\"},\"ja\":{}}");
            Assert.That(manager.Get("hello"), Is.EqualTo("Hello")); // Before Start.
            Assert.That(manager.SetLanguage(" TH_th "), Is.True);
            Assert.That(manager.CurrentLanguage, Is.EqualTo("th"));
            Assert.That(manager.Get("hello"), Is.EqualTo("สวัสดี"));
            Assert.That(manager.Get("fallback"), Is.EqualTo("Default"));
            Assert.That(manager.SetLanguage("ja"), Is.True);
            Assert.That(manager.Get("fallback"), Is.EqualTo("Default"));
            Assert.That(manager.SetLanguage("invalid"), Is.False);
            Assert.That(manager.CurrentLanguage, Is.EqualTo("ja"));
            Assert.That(manager.Get("absent"), Is.EqualTo("absent"));
            Assert.That(manager.GetOrDefault("absent", "Original"), Is.EqualTo("Original"));
            Assert.That(manager.Get(null), Is.EqualTo(""));
            manager.NextLanguage();
            Assert.That(manager.CurrentLanguage, Is.EqualTo("en"));
            manager.OnLanguageChanged("th");
            Assert.That(manager.CurrentLanguage, Is.EqualTo("en")); // Manual choice wins.
        }

        [TestCase("{}")]
        [TestCase("{\"en\":7,\"th\":{\"wrong\":null,\"hello\":42}}")]
        public void EmptyOrWronglyTypedEntries_AreSafe(string json)
        {
            var manager = Create(json);
            Assert.That(manager.Get("hello"), Is.EqualTo("hello"));
            Assert.That(manager.GetOrDefault("wrong", "Original"), Is.EqualTo("Original"));
            Assert.That(manager.SetLanguage(null), Is.False);
            Assert.DoesNotThrow(manager.NextLanguage);
        }

        [Test]
        public void DirectManagerCalls_SmartVariables_Assets_AndBoundaries()
        {
            var manager = Create("{\"en\":{\"smart\":\"Hello {name}: {count:plural:one item|{} items}, {count:000}\"},\"th\":{\"smart\":\"{name} {count} ชิ้น\"}}");
            manager.smartFormatter = root.AddComponent<LCGSmartFormatter>();
            manager.smartKeys = new[] { "en:smart", "th:smart" };
            manager.SetVariable("name", "{literal}"); manager.SetVariable("count", "2");
            Assert.That(manager.Get("smart"), Is.EqualTo("Hello {literal}: 2 items, 002"));
            manager.selectedLanguage = "th"; manager.SelectLanguage();
            Assert.That(manager.Get("smart"), Is.EqualTo("{literal} 2 ชิ้น"));
            Assert.That(manager.SetLanguageByIndex(-1), Is.False);
            Assert.That(manager.SetLanguageByIndex(2), Is.False);
            Assert.That(manager.SetLanguageByIndex(0), Is.True);
            manager.SetVariable("count", "1");
            Assert.That(manager.Get("smart"), Is.EqualTo("Hello {literal}: one item, 001"));
            Assert.That(manager.smartFormatter.Format("{{name}} {state:choose(on|off):yes|no|other}", new[]{"state"}, new[]{"on"}, "en"), Is.EqualTo("{name} yes"));
            var en = new GameObject("English variant"); var th = new GameObject("Thai variant");
            en.transform.SetParent(root.transform); th.transform.SetParent(root.transform);
            var asset = root.AddComponent<LCGLocalizedAsset>();
            asset.localization = manager; asset.languages = new[]{"en","th"}; asset.variants = new[]{en,th};
            asset.RefreshAsset(); manager.RegisterAsset(asset);
            Assert.That(manager.localizedAssets.Length, Is.EqualTo(1));
            manager.RefreshAll();
            Assert.That(en.activeSelf && !th.activeSelf, Is.True);
            manager.SetLanguage("th");
            Assert.That(!en.activeSelf && th.activeSelf, Is.True);
            manager.UnregisterAsset(asset);
            Assert.That(manager.localizedAssets, Is.Empty);
            asset.variants[1] = null;
            asset.RefreshAsset();
            Assert.That(en.activeSelf, Is.True); // Missing variant falls back to default.
        }

        [TestCase("{count:plural:one|{} items}", "en", true)]
        [TestCase("{count:plural:{} ชิ้น}", "th", true)]
        [TestCase("{count:plural:one|many}", "ru", false)]
        [TestCase("{state:choose(on):yes|other}", "en", true)]
        [TestCase("{state:choose(on):yes}", "en", false)]
        [TestCase("{person.Name}", "en", false)]
        [TestCase("{count:garbage}", "en", false)]
        public void SmartSyntax_IsValidatedBeforeBuild(string value, string language, bool valid)
        {
            if (valid) Assert.DoesNotThrow(() => LCGUnityLocalizationBridge.ValidateSmart(value, language));
            else Assert.Throws<UnityEditor.Build.BuildFailedException>(() => LCGUnityLocalizationBridge.ValidateSmart(value, language));
        }

        [TestCase("broken")]
        [TestCase("[]")]
        public void InvalidRoot_IsSafe(string json)
        {
            var manager = Create(json);
            LogAssert.Expect(LogType.Warning, "[LCGLocalization] Missing or invalid translation table.");
            Assert.That(manager.Get("hello"), Is.EqualTo("hello"));
            Assert.That(manager.GetLanguages(), Is.Empty);
        }

        [TestCase("{\"en\":{\"hello\":\"Hello\"},\"th\":{\"hello\":\"สวัสดี\"}}", true)]
        [TestCase("{\"EN\":{}}", false)]
        [TestCase("{\"en\":7}", false)]
        [TestCase("{\"en\":{\"hello\":null}}", false)]
        [TestCase("{\"en\":{\"\":\"Empty key\"}}", false)]
        public void EditorValidation_RejectsTablesItCannotEdit(string json, bool valid)
        {
            DataToken token;
            Assert.That(VRCJson.TryDeserializeFromJson(json, out token), Is.True);
            string message;
            Assert.That(LCGLocalizationWindow.ValidateTable(token.DataDictionary, out message), Is.EqualTo(valid));
        }
    }
}
