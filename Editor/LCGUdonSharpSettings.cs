using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UdonSharp.LCGCompiler
{
    /// <summary>LCG compiler settings, kept out of the stock UdonSharpSettings asset.</summary>
    internal static class LCGUdonSharpSettings
    {
        private const string SettingsPath = "ProjectSettings/LogicCuteGuy.LCGUdonSharp.json";

        [Serializable]
        private class Data
        {
            public int asyncTaskFrameCapacity = 32;
        }

        private static Data _data;

        /// <summary>Maximum compiler-generated async frames per behaviour. Read once on the main thread before lowering.</summary>
        internal static int AsyncTaskFrameCapacity => Mathf.Clamp(Load().asyncTaskFrameCapacity, 1, 256);

        private static Data Load()
        {
            if (_data != null)
                return _data;

            var data = new Data();
            if (File.Exists(SettingsPath))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), data);
            return _data = data;
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Project/Udon Sharp/LCGUdonSharp", SettingsScope.Project)
        {
            guiHandler = _ =>
            {
                Data data = Load();
                EditorGUI.BeginChangeCheck();
                int capacity = EditorGUILayout.IntSlider(new GUIContent("Async task frame capacity",
                    "Maximum compiler-generated async frames per behaviour (1-256). State is allocated only for behaviours that use async lowering."),
                    data.asyncTaskFrameCapacity, 1, 256);
                if (!EditorGUI.EndChangeCheck())
                    return;

                data.asyncTaskFrameCapacity = capacity;
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(data, true));
            },
        };
    }
}
