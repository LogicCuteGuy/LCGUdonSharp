using System.IO;
using NUnit.Framework;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGUdonSharpInstallerTests
    {
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
