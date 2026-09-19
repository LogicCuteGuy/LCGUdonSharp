using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGExampleAssemblyTests
    {
        private const string AssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LogicCuteGuy.LCGUdonSharp.Examples.asmdef";
        private const string UdonSharpAssemblyPath =
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LogicCuteGuy.LCGUdonSharp.Examples.USharp.asset";
        private const string ExpectedAssemblyName = "LogicCuteGuy.LCGUdonSharp.Examples";

        private static readonly string[] ExampleScriptPaths =
        {
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGPacketFieldShowcase.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGPacketMethodShowcase.cs",
            "Packages/com.logiccuteguy.lcgudonsharp/Example/LCGZoneObjectShowcase.cs",
        };

        [Test]
        public void ExampleAssembly_IsRegisteredAsUdonSharpAssembly()
        {
            Object assemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(AssemblyPath);
            Object udonSharpAssemblyDefinition = AssetDatabase.LoadAssetAtPath<Object>(UdonSharpAssemblyPath);

            Assert.That(assemblyDefinition, Is.Not.Null, "Example asmdef is missing.");
            Assert.That(udonSharpAssemblyDefinition, Is.Not.Null,
                "Example UdonSharp assembly definition is missing.");

            SerializedObject serializedDefinition = new SerializedObject(udonSharpAssemblyDefinition);
            SerializedProperty sourceAssembly = serializedDefinition.FindProperty("sourceAssembly");

            Assert.That(sourceAssembly, Is.Not.Null,
                "Example registration is not a UdonSharpAssemblyDefinition asset.");
            Assert.That(sourceAssembly.objectReferenceValue, Is.SameAs(assemblyDefinition),
                "Example UdonSharp assembly registration points to the wrong asmdef.");

            foreach (string scriptPath in ExampleScriptPaths)
            {
                string resolvedAssembly = CompilationPipeline.GetAssemblyNameFromScriptPath(scriptPath);
                Assert.That(Path.GetFileNameWithoutExtension(resolvedAssembly), Is.EqualTo(ExpectedAssemblyName),
                    scriptPath + " does not resolve to the registered example assembly.");
            }
        }
    }
}
