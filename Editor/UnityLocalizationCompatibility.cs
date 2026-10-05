using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace LogicCuteGuy.LCGUdonSharp.Installer
{
    // The SDK's legacy DLL declares a global ExtensionMethods class. SBP only
    // needs its explicitly referenced Unity assemblies, so exclude auto DLLs.
    [InitializeOnLoad]
    internal sealed class UnityLocalizationCompatibility : AssetPostprocessor
    {
        static UnityLocalizationCompatibility() { EditorApplication.delayCall += Repair; }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
        {
            foreach (string path in imported)
                if (path.EndsWith("Unity.ScriptableBuildPipeline.Editor.asmdef"))
                { EditorApplication.delayCall += Repair; break; }
        }

        public static void Repair()
        {
            const string assetPath = "Packages/com.unity.scriptablebuildpipeline/Editor/Unity.ScriptableBuildPipeline.Editor.asmdef";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null) return;
            string path = Path.Combine(package.resolvedPath, "Editor/Unity.ScriptableBuildPipeline.Editor.asmdef");
            string before = File.ReadAllText(path);
            string after = Regex.Replace(before, "\"overrideReferences\"\\s*:\\s*false", "\"overrideReferences\": true");
            if (before == after) return;
            File.WriteAllText(path, after);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
