using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BigWalkVRInstaller.Services
{
    public sealed class LoaderMigrationResult
    {
        public bool Migrated { get; set; }
        public string Details { get; set; }
    }

    public static class BepInExInstaller
    {
        const string SupportedProductVersion = "6.0.0-be.755";
        static readonly string[] DeployedRuntimeFiles =
        {
            "BigWalkVR.Native.dll",
            "openvr_api.dll",
            "phonon.dll",
            "bigwalkvr.vrmanifest",
            "bigwalkvr_actions.json",
            "bindings_knuckles.json",
            "bindings_oculus_touch.json",
            @"Big Walk_Data\Plugins\x86_64\XRSDKOpenVR.dll",
            @"Big Walk_Data\Plugins\x86_64\openvr_api.dll",
            @"Big Walk_Data\UnitySubsystems\XRSDKOpenVR\UnitySubsystemsManifest.json",
            @"Big Walk_Data\StreamingAssets\SteamVR\OpenVRSettings.asset",
            @"Big Walk_Data\Plugins\x86_64\XRSDKOpenXR.dll",
            @"Big Walk_Data\Plugins\x86_64\openxr_loader.dll",
            @"Big Walk_Data\UnitySubsystems\XRSDKOpenXR\UnitySubsystemsManifest.json"
        };

        public static bool IsInstalled(string gamePath) =>
            !string.IsNullOrEmpty(gamePath) &&
            File.Exists(Path.Combine(gamePath, "winhttp.dll")) &&
            GetProductVersion(gamePath).Equals(SupportedProductVersion, StringComparison.OrdinalIgnoreCase);

        public static bool IsMelonLoaderInstalled(string gamePath) =>
            !string.IsNullOrEmpty(gamePath) &&
            File.Exists(Path.Combine(gamePath, "version.dll")) &&
            Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

        public static bool HasUnknownBootstrap(string gamePath) =>
            !string.IsNullOrEmpty(gamePath) &&
            File.Exists(Path.Combine(gamePath, "version.dll")) &&
            !Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

        public static string InstalledVersion(string gamePath)
        {
            var version = GetProductVersion(gamePath);
            return string.IsNullOrEmpty(version) ? null : version;
        }

        static string GetProductVersion(string gamePath)
        {
            var dll = Path.Combine(gamePath, "BepInEx", "core", "BepInEx.Core.dll");
            if (!File.Exists(dll)) return "";
            var version = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "";
            var metadata = version.IndexOf('+');
            return metadata < 0 ? version : version.Substring(0, metadata);
        }

        static readonly string[] Files = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };

        public static void Remove(string gamePath)
        {
            RemoveMelonLoaderFiles(gamePath);
            foreach (var relative in DeployedRuntimeFiles)
                PackageInstaller.DeleteRelative(gamePath, relative);
            foreach (var name in Files)
            {
                var path = Path.Combine(gamePath, name);
                if (File.Exists(path)) File.Delete(path);
            }
            var directory = Path.Combine(gamePath, "BepInEx");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        public static LoaderMigrationResult Extract(string gamePath, byte[] zipBytes)
        {
            if (HasUnknownBootstrap(gamePath))
                throw new Exception("version.dll is active but is not part of a complete MelonLoader install. Remove or identify it before installing BepInEx.");
            var migration = MigrateMelonLoader(gamePath);
            using (var stream = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                const string prefix = "BepInExPack/";
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Length == 0 || !entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var relative = entry.FullName.Substring(prefix.Length);
                    var dest = PackageInstaller.ResolveInside(gamePath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
            return migration;
        }

        static LoaderMigrationResult MigrateMelonLoader(string gamePath)
        {
            if (!IsMelonLoaderInstalled(gamePath)) return new LoaderMigrationResult();

            Remove(gamePath);
            PackageInstaller.ClearRecords(gamePath);
            MigratePreferences(gamePath);

            return new LoaderMigrationResult
            {
                Migrated = true,
                Details = "MelonLoader and its mods were removed. BigWalkVR settings were migrated to BepInEx."
            };
        }

        static void RemoveMelonLoaderFiles(string gamePath)
        {
            foreach (var name in new[] { "version.dll", "dobby.dll", "NOTICE.txt" })
            {
                var path = Path.Combine(gamePath, name);
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (var name in new[] { "MelonLoader", "Mods", "Plugins", "UserLibs" })
            {
                var path = Path.Combine(gamePath, name);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        static void MigratePreferences(string gamePath)
        {
            var source = Path.Combine(gamePath, "UserData", "MelonPreferences.cfg");
            if (!File.Exists(source)) return;

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SnapTurn", "SnapTurnDegrees", "SmoothTurnSpeed", "DevCameraMode", "LeftHandCamera", "CameraStabilization"
            };
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inCategory = false;
            foreach (var rawLine in File.ReadAllLines(source))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inCategory = line.Equals("[BigWalkVR]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inCategory) continue;
                var separator = line.IndexOf('=');
                if (separator < 1) continue;
                var key = line.Substring(0, separator).Trim();
                if (keys.Contains(key)) values[key] = line.Substring(separator + 1).Trim();
            }
            if (values.Count == 0) return;

            var destination = Path.Combine(gamePath, "BepInEx", "config", "com.circuit.bigwalkvr.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var output = new StringBuilder().AppendLine("[BigWalkVR]").AppendLine();
            foreach (var key in new[] { "SnapTurn", "SnapTurnDegrees", "SmoothTurnSpeed", "DevCameraMode", "LeftHandCamera", "CameraStabilization" })
                if (values.TryGetValue(key, out var value)) output.AppendLine(key + " = " + value);
            File.WriteAllText(destination, output.ToString());
        }
    }
}
