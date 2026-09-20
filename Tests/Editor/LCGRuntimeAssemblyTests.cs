using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGRuntimeAssemblyTests
    {
        private const string AssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LogicCuteGuy.LCGUdonSharp.Runtime.asmdef";
        private const string UdonSharpAssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LogicCuteGuy.LCGUdonSharp.Runtime.USharp.asset";
        private const string ExpectedAssemblyName = "LogicCuteGuy.LCGUdonSharp.Runtime";

        private static readonly string[] RuntimeBehaviourPaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LCGManualObjectSync.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LCGRuntime.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/UdonSharp/Runtime/LCGBehaviours/LCGNetworkZone.cs",
        };

        [Test]
        public void RuntimeBehaviours_AreRegisteredAsUdonSharpAssembly()
        {
            Object assemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(AssemblyPath);
            Object udonSharpAssemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(UdonSharpAssemblyPath);

            Assert.That(assemblyDefinition, Is.Not.Null, "LCG runtime asmdef is missing.");
            Assert.That(udonSharpAssemblyDefinition, Is.Not.Null,
                "LCG runtime UdonSharp assembly registration is missing.");

            SerializedObject serializedDefinition = new SerializedObject(udonSharpAssemblyDefinition);
            SerializedProperty sourceAssembly = serializedDefinition.FindProperty("sourceAssembly");
            Assert.That(sourceAssembly, Is.Not.Null,
                "LCG runtime registration is not a UdonSharpAssemblyDefinition asset.");
            Assert.That(sourceAssembly.objectReferenceValue, Is.SameAs(assemblyDefinition),
                "LCG runtime UdonSharp registration points to the wrong asmdef.");

            foreach (string scriptPath in RuntimeBehaviourPaths)
            {
                string resolvedAssembly = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath);
                Assert.That(Path.GetFileNameWithoutExtension(resolvedAssembly), Is.EqualTo(ExpectedAssemblyName),
                    scriptPath + " does not resolve to the registered LCG runtime assembly.");
            }
        }
    }
}
