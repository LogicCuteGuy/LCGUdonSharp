using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LogicCuteGuy.LCGUdonSharp.Installer
{
    [InitializeOnLoad]
    internal static class LCGUdonSharpInstaller
    {
        internal const string PackageName = "com.logiccuteguy.lcgudonsharp";
        internal const string SupportedSdkVersion = "3.10.5";
        internal const string InstallerVersion = "0.1.2";

        private const string WorldsPackageName = "com.vrchat.worlds";
        private const string StateRelativePath = "ProjectSettings/LogicCuteGuy.LCGUdonSharp.json";
        private const string LegacyStateRelativePath = "ProjectSettings/LogicCuteGuy.UdonSharpInterface.json";
        private const string WorkspaceRelativePath = "Library/LogicCuteGuy.LCGUdonSharp";
        private const string LegacyWorkspaceRelativePath = "Library/LogicCuteGuy.UdonSharpInterface";
        private static readonly string[] LegacyInstalledRelativePaths =
        {
            "Assets/LogicCuteGuy/UdonSharpInterface/UdonSharp",
            "Assets/LogicCuteGuy/LCGUdonSharp/UdonSharp",
            "Packages/com.logiccuteguy.lcgudonsharp.compiler",
        };
        private static bool _queued;
        private static bool _running;

        [Serializable]
        private sealed class InstallState
        {
            public string installerVersion;
            public string sdkVersion;
            public bool suspended;
        }

        static LCGUdonSharpInstaller()
        {
            QueueAutomaticSetup();
            Events.registeredPackages += _ => QueueAutomaticSetup();
        }

        private static void QueueAutomaticSetup()
        {
            if (_queued)
                return;

            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                InstallState state = LoadState();
                if (state == null || !state.suspended)
                    InstallOrRepair(false);
            };
        }

        [MenuItem("Tools/LCGUdonSharp/Install or Repair", priority = 120)]
        private static void InstallOrRepairMenu()
        {
            InstallOrRepair(true);
        }

        [MenuItem("Tools/LCGUdonSharp/Restore VRChat UdonSharp and Disable Auto Setup", priority = 121)]
        private static void RestoreMenu()
        {
            if (_running)
                return;

            _running = true;
            try
            {
                string projectRoot = GetProjectRoot();
                PackageInfo installerPackage = FindPackage(PackageName);
                PackageInfo worldsPackage = FindPackage(WorldsPackageName);
                if (installerPackage == null || worldsPackage == null)
                    throw new InvalidOperationException("LCGUdonSharp or the VRChat Worlds SDK is not registered with Unity Package Manager.");

                string sdkUdonSharp = NormalizePath(Path.Combine(worldsPackage.resolvedPath, "Integrations", "UdonSharp"));
                string backup = GetBackupPath(projectRoot, worldsPackage.version);
                string installed = NormalizePath(Path.Combine(installerPackage.resolvedPath, "UdonSharp"));

                RequirePathWithin(worldsPackage.resolvedPath, sdkUdonSharp, "SDK UdonSharp folder");
                RequirePathWithin(installerPackage.resolvedPath, installed, "installed compiler");
                RequirePathWithin(projectRoot, backup, "backup");

                MigrateLegacyBackup(projectRoot, worldsPackage.version, backup);

                if (!Directory.Exists(backup))
                    throw new DirectoryNotFoundException("No installer backup exists at " + backup);

                DeleteDirectoryAndMeta(sdkUdonSharp);
                CopyDirectory(backup, sdkUdonSharp);
                CopyMetaIfPresent(backup, sdkUdonSharp);
                DeleteDirectoryAndMeta(installed);
                DeleteLegacyInstallations(projectRoot);

                SaveState(projectRoot, new InstallState
                {
                    installerVersion = InstallerVersion,
                    sdkVersion = worldsPackage.version,
                    suspended = true,
                });

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[LCGUdonSharp] Restored the VRChat SDK compiler and disabled automatic setup.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _running = false;
            }
        }

        internal static void InstallOrRepair(bool force)
        {
            if (_running)
                return;

            _running = true;
            string sdkUdonSharp = null;
            string installed = null;
            string backup = null;
            string staging = null;
            try
            {
                string projectRoot = GetProjectRoot();
                PackageInfo installerPackage = FindPackage(PackageName);
                PackageInfo worldsPackage = FindPackage(WorldsPackageName);
                if (installerPackage == null || worldsPackage == null)
                    return;

                if (!string.Equals(worldsPackage.version, SupportedSdkVersion, StringComparison.Ordinal))
                {
                    Debug.LogError($"[LCGUdonSharp] VRChat Worlds SDK {worldsPackage.version} is not supported. Expected {SupportedSdkVersion}; no files were changed.");
                    return;
                }

                string payload = NormalizePath(Path.Combine(installerPackage.resolvedPath, "Payload~", "UdonSharp"));
                sdkUdonSharp = NormalizePath(Path.Combine(worldsPackage.resolvedPath, "Integrations", "UdonSharp"));
                installed = NormalizePath(Path.Combine(installerPackage.resolvedPath, "UdonSharp"));
                string workspace = NormalizePath(Path.Combine(projectRoot, WorkspaceRelativePath));
                backup = GetBackupPath(projectRoot, worldsPackage.version);
                staging = NormalizePath(Path.Combine(workspace, "Staging", Guid.NewGuid().ToString("N"), "UdonSharp"));

                RequirePathWithin(installerPackage.resolvedPath, payload, "compiler payload");
                RequirePathWithin(worldsPackage.resolvedPath, sdkUdonSharp, "SDK UdonSharp folder");
                RequirePathWithin(installerPackage.resolvedPath, installed, "installed clone");
                RequirePathWithin(projectRoot, workspace, "installer workspace");
                RequirePathWithin(projectRoot, backup, "backup");

                // Validate before backing up, deleting, or replacing either compiler.
                InstallerPayloadValidator.Validate(payload);

                MigrateLegacyBackup(projectRoot, worldsPackage.version, backup);

                InstallState state = LoadState();
                bool alreadyInstalled = Directory.Exists(installed) && !Directory.Exists(sdkUdonSharp) &&
                                        state != null && state.installerVersion == InstallerVersion &&
                                        state.sdkVersion == worldsPackage.version && !state.suspended;
                if (alreadyInstalled && !force)
                    return;

                CopyDirectory(payload, staging);
                CopyMetaIfPresent(payload, staging);
                InstallerPayloadValidator.Validate(staging);

                if (Directory.Exists(sdkUdonSharp) && !Directory.Exists(backup))
                {
                    CopyDirectory(sdkUdonSharp, backup);
                    CopyMetaIfPresent(sdkUdonSharp, backup);
                }

                DeleteDirectoryAndMeta(sdkUdonSharp);
                DeleteDirectoryAndMeta(installed);
                DeleteLegacyInstallations(projectRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(installed));
                Directory.Move(staging, installed);
                MoveMetaIfPresent(staging, installed);

                SaveState(projectRoot, new InstallState
                {
                    installerVersion = InstallerVersion,
                    sdkVersion = worldsPackage.version,
                    suspended = false,
                });

                DeleteEmptyStagingParents(staging, workspace);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[LCGUdonSharp] Installed the compiler clone in the package and removed the SDK-bundled copy.");
            }
            catch (Exception exception)
            {
                TryRestoreCompilerAfterFailedInstall(sdkUdonSharp, installed, backup);
                Debug.LogException(exception);
            }
            finally
            {
                _running = false;
            }
        }

        private static void TryRestoreCompilerAfterFailedInstall(string sdkUdonSharp, string installed, string backup)
        {
            if (string.IsNullOrEmpty(sdkUdonSharp) || string.IsNullOrEmpty(installed) ||
                string.IsNullOrEmpty(backup) || Directory.Exists(installed) || Directory.Exists(sdkUdonSharp) ||
                !Directory.Exists(backup))
                return;

            try
            {
                CopyDirectory(backup, sdkUdonSharp);
                CopyMetaIfPresent(backup, sdkUdonSharp);
                Debug.LogWarning("[LCGUdonSharp] Setup failed, so the backed-up VRChat compiler was restored.");
            }
            catch (Exception rollbackException)
            {
                Debug.LogException(rollbackException);
            }
        }

        internal static string NormalizePath(string path)
        {
            return InstallerPathUtility.NormalizePath(path);
        }

        internal static bool IsPathWithin(string parent, string child)
        {
            return InstallerPathUtility.IsPathWithin(parent, child);
        }

        private static void RequirePathWithin(string parent, string child, string label)
        {
            if (!IsPathWithin(parent, child))
                throw new InvalidOperationException($"Unsafe {label} path: {child}");
        }

        private static PackageInfo FindPackage(string packageName)
        {
            return PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => string.Equals(package.name, packageName, StringComparison.Ordinal));
        }

        private static string GetProjectRoot()
        {
            return NormalizePath(Path.Combine(Application.dataPath, ".."));
        }

        private static string GetBackupPath(string projectRoot, string sdkVersion)
        {
            return NormalizePath(Path.Combine(projectRoot, WorkspaceRelativePath, "Backups", "com.vrchat.worlds-" + sdkVersion, "UdonSharp"));
        }

        private static void MigrateLegacyBackup(string projectRoot, string sdkVersion, string backup)
        {
            if (Directory.Exists(backup))
                return;

            string legacyBackup = NormalizePath(Path.Combine(projectRoot, LegacyWorkspaceRelativePath, "Backups", "com.vrchat.worlds-" + sdkVersion, "UdonSharp"));
            RequirePathWithin(projectRoot, legacyBackup, "legacy backup");
            if (!Directory.Exists(legacyBackup))
                return;

            CopyDirectory(legacyBackup, backup);
            CopyMetaIfPresent(legacyBackup, backup);
        }

        private static void DeleteLegacyInstallations(string projectRoot)
        {
            foreach (string relativePath in LegacyInstalledRelativePaths)
            {
                string legacyPath = NormalizePath(Path.Combine(projectRoot, relativePath));
                RequirePathWithin(projectRoot, legacyPath, "legacy installation");
                DeleteDirectoryAndMeta(legacyPath);

                string parent = Path.GetDirectoryName(legacyPath);
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    DeleteDirectoryAndMeta(parent);
            }
        }

        private static InstallState LoadState()
        {
            string path = Path.Combine(GetProjectRoot(), StateRelativePath);
            if (!File.Exists(path))
                path = Path.Combine(GetProjectRoot(), LegacyStateRelativePath);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonUtility.FromJson<InstallState>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LCGUdonSharp] Ignoring unreadable installer state: " + exception.Message);
                return null;
            }
        }

        private static void SaveState(string projectRoot, InstallState state)
        {
            string path = NormalizePath(Path.Combine(projectRoot, StateRelativePath));
            RequirePathWithin(projectRoot, path, "installer state");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(state, true));
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
            }
        }

        private static void DeleteDirectoryAndMeta(string path)
        {
            if (Directory.Exists(path))
            {
                MakeWritable(path);
                Directory.Delete(path, true);
            }
            if (File.Exists(path + ".meta"))
            {
                File.SetAttributes(path + ".meta", File.GetAttributes(path + ".meta") & ~FileAttributes.ReadOnly);
                File.Delete(path + ".meta");
            }
        }

        private static void MakeWritable(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);

            foreach (string childDirectory in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(childDirectory, File.GetAttributes(childDirectory) & ~FileAttributes.ReadOnly);

            File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);
        }

        private static void CopyMetaIfPresent(string sourceDirectory, string destinationDirectory)
        {
            if (File.Exists(sourceDirectory + ".meta"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory));
                File.Copy(sourceDirectory + ".meta", destinationDirectory + ".meta", true);
                File.SetAttributes(destinationDirectory + ".meta", File.GetAttributes(destinationDirectory + ".meta") & ~FileAttributes.ReadOnly);
            }
        }

        private static void MoveMetaIfPresent(string sourceDirectory, string destinationDirectory)
        {
            string sourceMeta = sourceDirectory + ".meta";
            if (!File.Exists(sourceMeta))
                return;

            string destinationMeta = destinationDirectory + ".meta";
            if (File.Exists(destinationMeta))
                File.Delete(destinationMeta);
            File.Move(sourceMeta, destinationMeta);
        }

        private static void DeleteEmptyStagingParents(string staging, string workspace)
        {
            string current = Path.GetDirectoryName(staging);
            while (IsPathWithin(workspace, current) && Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
        }
    }
}
