using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor.Build;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGUnityLocalizationTests
    {
        public sealed class TestCollection : StringTableCollection
        {
            public StringTable testTable;
            public override System.Collections.ObjectModel.ReadOnlyCollection<StringTable> StringTables
                => System.Array.AsReadOnly(new[] { testTable });
        }

        [Test]
        public void BakeReadsNativeTables_AndTracksEdits()
        {
            var collection = ScriptableObject.CreateInstance<TestCollection>();
            var shared = ScriptableObject.CreateInstance<SharedTableData>();
            var table = ScriptableObject.CreateInstance<StringTable>();
            var go = new GameObject("Native localization test");
            try
            {
                table.SharedData = shared;
                table.LocaleIdentifier = new LocaleIdentifier("en");
                table.AddEntry("native_key", "From Unity table");
                collection.testTable = table;
                var manager = go.AddComponent<LCGLocalization>();
                manager.followClientLanguage = false;
                manager.bakedTranslations = LCGUnityLocalizationBridge.SerializeCollection(collection);
                Assert.That(manager.Get("native_key"), Is.EqualTo("From Unity table"));
                table.GetEntry("native_key").Value = "Edited in Unity";
                Assert.That(LCGUnityLocalizationBridge.SerializeCollection(collection), Does.Contain("Edited in Unity"));
                table.GetEntry("native_key").IsSmart = true;
                Assert.That(LCGUnityLocalizationBridge.SerializeCollection(collection), Does.Contain("Edited in Unity"));
                table.GetEntry("native_key").Value = "{items:list:{}|, }";
                Assert.Throws<BuildFailedException>(() => LCGUnityLocalizationBridge.SerializeCollection(collection));
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(collection);
                Object.DestroyImmediate(table); Object.DestroyImmediate(shared);
            }
        }

        [Test]
        public void MissingCollection_FailsClearly()
        {
            Assert.Throws<BuildFailedException>(() => LCGUnityLocalizationBridge.SerializeCollection(null));
        }

        [Test]
        public void PrefabPreview_ReplacesAndDisablesImmediately_WithoutDestroyingSources()
        {
            var go = new GameObject("Prefab preview test");
            var first = new GameObject("First source");
            var second = new GameObject("Second source");
            try
            {
                var component = go.AddComponent<LCGLocalizePrefabEvent>();
                var update = typeof(LCGLocalizePrefabEvent).GetMethod("UpdateAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                update.Invoke(component, new object[] { first });
                var previous = go.transform.GetChild(0).gameObject;
                update.Invoke(component, new object[] { second });
                Assert.That(previous == null, Is.True);
                Assert.That(go.transform.childCount, Is.EqualTo(1));
                Assert.That(go.transform.GetChild(0).name, Is.EqualTo("Second source(Clone)"));
                component.enabled = false;
                Assert.That(go.transform.childCount, Is.Zero);
                Assert.That(first != null && second != null, Is.True);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeListener_RequiresDynamicValue(bool fixedValue)
        {
            var go = new GameObject("Listener validation", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            try
            {
                var component = go.AddComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
                component.enabled = false;
                var setter = (UnityEngine.Events.UnityAction<string>)System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<string>), go.GetComponent<UnityEngine.UI.Text>(), "set_text");
                if (fixedValue) UnityEditor.Events.UnityEventTools.AddStringPersistentListener(component.OnUpdateString, setter, "Fixed");
                else UnityEditor.Events.UnityEventTools.AddPersistentListener(component.OnUpdateString, setter);
                if (fixedValue) Assert.Throws<BuildFailedException>(() => LCGUnityLocalizationBridge.ValidateDynamicListener(component, "m_UpdateString", 0));
                else Assert.DoesNotThrow(() => LCGUnityLocalizationBridge.ValidateDynamicListener(component, "m_UpdateString", 0));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ExistingSettingsAreCached_ForCompilerWorkers()
        {
            var cache = typeof(UdonSharpSettings).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = cache.GetValue(null);
            try
            {
                cache.SetValue(null, null);
                var settings = UdonSharpSettings.GetSettings();
                Assert.That(cache.GetValue(null), Is.SameAs(settings));
                Assert.That(Task.Run(() => UdonSharpSettings.GetSettings()).GetAwaiter().GetResult(), Is.SameAs(settings));
            }
            finally { cache.SetValue(null, previous); }
        }
    }
}
