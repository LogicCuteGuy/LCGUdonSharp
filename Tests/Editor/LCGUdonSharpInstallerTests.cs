using System.IO;
using NUnit.Framework;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGUdonSharpInstallerTests
    {
        [Test]
        public void PayloadValidator_AcceptsInstalledLcgCompiler()
        {
            InstallerPayloadValidator.Validate("Packages/com.logiccuteguy.lcgudonsharp/UdonSharp");
        }

        [TestCase("Runtime/UdonSharpAttributes.cs")]
        [TestCase("Runtime/UdonSharpBehaviour.cs")]
        [TestCase("Runtime/LCGBehaviours/LCGRuntime.cs")]
        [TestCase("Editor/Compiler/Lowering/CollectionSyntaxLowerer.cs")]
        public void PayloadValidator_RejectsMissingLcgFeature(string relative)
        {
            string source = Path.GetFullPath("Packages/com.logiccuteguy.lcgudonsharp/UdonSharp");
            string copy = Path.Combine(Path.GetTempPath(), "lcg-payload-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                // Real source files ensure this test catches an incomplete compiler tree.
                foreach (string file in Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(copy, file.Substring(source.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                }
                InstallerPayloadValidator.ValidateFeatures(copy);
                File.Delete(Path.Combine(copy, relative));
                Assert.Throws<InvalidDataException>(() => InstallerPayloadValidator.ValidateFeatures(copy));
            }
            finally
            {
                if (Directory.Exists(copy))
                    Directory.Delete(copy, true);
            }
        }

        [Test]
        public void IsPathWithin_AcceptsDescendant()
        {
            string parent = Path.Combine(Path.GetTempPath(), "lcgudonsharp-root");
            string child = Path.Combine(parent, "nested", "payload");

            Assert.That(InstallerPathUtility.IsPathWithin(parent, child), Is.True);
        }

        [Test]
        public void IsPathWithin_RejectsSiblingWithSharedPrefix()
        {
            string parent = Path.Combine(Path.GetTempPath(), "lcgudonsharp-root");
            string sibling = parent + "-other";

            Assert.That(InstallerPathUtility.IsPathWithin(parent, sibling), Is.False);
        }

        [Test]
        public void IsPathWithin_RejectsParentItself()
        {
            string parent = Path.Combine(Path.GetTempPath(), "lcgudonsharp-root");

            Assert.That(InstallerPathUtility.IsPathWithin(parent, parent), Is.False);
        }
    }
}
