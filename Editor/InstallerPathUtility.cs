using System;
using System.IO;

namespace LogicCuteGuy.LCGUdonSharp.Installer
{
    internal static class InstallerPathUtility
    {
        internal static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static bool IsPathWithin(string parent, string child)
        {
            string normalizedParent = NormalizePath(parent);
            string normalizedChild = NormalizePath(child);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return !string.Equals(normalizedParent, normalizedChild, comparison) &&
                   normalizedChild.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison);
        }
    }
}
