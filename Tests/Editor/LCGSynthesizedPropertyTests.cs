using System;
using System.Reflection;
using NUnit.Framework;

namespace UdonSharp.Tests
{
    public sealed class LCGSynthesizedPropertyTests
    {
        [TestCase("TMProTextMeshProUGUI.__get_text__SystemString", true)]
        [TestCase("VRCUdonCommonInterfacesIUdonEventReceiver.__set_enabled__SystemBoolean__SystemVoid", false)]
        [TestCase("SystemInt32Array.__Get__SystemInt32__SystemInt32", true)]
        public void GuardLabel_SupportsPropertiesWithoutRoslynMetadata(string signature, bool getter)
        {
            Assembly compiler = typeof(Compiler.UdonSharpCompilerV1).Assembly;
            Type typeSymbol = compiler.GetType("UdonSharp.Compiler.Symbols.TypeSymbol", true);
            Type methodType = compiler.GetType("UdonSharp.Compiler.Symbols.ExternSynthesizedMethodSymbol", true);
            object method = Activator.CreateInstance(methodType, new object[]
            {
                null, signature, Array.CreateInstance(typeSymbol, 0), null, false, false
            });
            Type propertyType = compiler.GetType("UdonSharp.Compiler.Symbols.SynthesizedPropertySymbol", true);
            object property = Activator.CreateInstance(propertyType, new[]
            {
                null, getter ? method : null, getter ? null : method
            });
            Type symbolType = compiler.GetType("UdonSharp.Compiler.Symbols.Symbol", true);
            Assert.That(symbolType.GetProperty("RoslynSymbol").GetValue(property), Is.Null);

            // This is the Name access used by EmitAccessGuards for reads and writes.
            string label = null;
            Assert.DoesNotThrow(() => label = "property:" + symbolType.GetProperty("Name").GetValue(property));
            Assert.That(label, Is.EqualTo("property:" + signature));
        }
    }
}
