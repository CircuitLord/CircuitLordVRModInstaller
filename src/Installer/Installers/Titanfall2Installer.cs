using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BigWalkVRInstaller.Services;
using Microsoft.Win32;

namespace BigWalkVRInstaller.Installers
{
    public sealed class TitanfallLaunchSettings
    {
        public string[] arguments;
        public string[] vrArguments;
    }

    public sealed class OpenXrView
    {
        public int width;
        public int height;
    }

    public enum CampaignSaveStatus { Missing, Incomplete, Available }

    public sealed class Titanfall2Installer : IVrModInstaller
    {
        public const string InstallerId = "Titanfall2VR";
        public const string NorthstarVersion = "1.31.13";
        public const string NorthstarUrl = "https://github.com/R2Northstar/Northstar/releases/download/v1.31.13/Northstar.release.v1.31.13.zip";
        public const string NorthstarSha256 = "e622b96e7609912060a61ba3eed382eeafd6cc7b62c105ea609a36bc36e75322";
        public const long NorthstarSize = 107146100;
        public const string LauncherName = "Titanfall2VRLauncher.exe";
        public const string ProfileName = "TF2VR";
        public const string SteamAppId = "1237970";

        static readonly SteamGameLocator Locator = new SteamGameLocator(
            "Titanfall2", "Titanfall2.exe", @"SOFTWARE\Respawn\Titanfall2", "Install Dir");

        static readonly string[] CampaignFiles = { "profile.cfg", "savegames/savegame.sav" };

        readonly AppSettings _settings;

        public Titanfall2Installer(AppSettings settings) => _settings = settings;

        public string Id => "titanfall-2-vr";
        public string Name => "Titanfall 2 VR";
        public string GamePath => _settings.Titanfall2Path;
        public bool HasGame => Locator.IsValid(GamePath);
        public InstallRecord Record => HasGame ? OwnedFileStore.Read(GamePath, InstallerId) : null;
        public bool IsInstalled => Record != null
            && File.Exists(Path.Combine(GamePath, LauncherName))
            && File.Exists(Path.Combine(GamePath, ProfileName, "Northstar.dll"))
            && File.Exists(Path.Combine(GamePath, ProfileName, "plugins", "Titanfall2VR.dll"))
            && File.Exists(Path.Combine(GamePath, ProfileName, "tools", "xr_probe.exe"))
            && File.Exists(Path.Combine(GamePath, ProfileName, "tools", "crash_monitor.exe"))
            && File.Exists(Path.Combine(GamePath, ProfileName, "tools", "launch.json"));
        public string InstalledVersion => IsInstalled ? Record.version : null;

        public static ReleaseInfo PinnedNorthstar => new ReleaseInfo
        {
            version = NorthstarVersion,
            url = NorthstarUrl,
            sha256 = NorthstarSha256,
            size = NorthstarSize
        };

        public string DetectGamePath()
        {
            var path = Locator.Detect();
            if (path != null) SetGamePath(path);
            return path;
        }

        public void SetGamePath(string path)
        {
            if (!Locator.IsValid(path)) throw new Exception("That folder doesn't contain Titanfall2.exe");
            _settings.Titanfall2Path = SteamGameLocator.Canonical(path);
            _settings.Save();
        }

        public bool CanUpdate(ManifestMod modRelease, bool beta) => IsInstalled &&
            (Record.beta != beta
             || VersionUtil.IsNewer(modRelease.version, Record.version)
             || !string.Equals(Record.northstarVersion, NorthstarVersion, StringComparison.OrdinalIgnoreCase));

        public void Install(byte[] northstarPackage, byte[] modPackage, bool beta)
        {
            var previous = Record;
            var written = new List<string>();
            ReleaseInfo release;
            using (var northstarStream = new MemoryStream(northstarPackage))
            using (var northstarArchive = new ZipArchive(northstarStream, ZipArchiveMode.Read))
            using (var modStream = new MemoryStream(modPackage))
            using (var modArchive = new ZipArchive(modStream, ZipArchiveMode.Read))
            {
                var launcher = northstarArchive.GetEntry("NorthstarLauncher.exe")
                    ?? throw new Exception("Northstar package is missing NorthstarLauncher.exe");
                const string sourcePrefix = "R2Northstar/";
                var profileEntries = northstarArchive.Entries
                    .Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith(sourcePrefix, StringComparison.Ordinal))
                    .ToList();
                if (!profileEntries.Any(entry => entry.FullName == "R2Northstar/Northstar.dll"))
                    throw new Exception("Northstar package is missing R2Northstar/Northstar.dll");

                var plugin = modArchive.GetEntry("Titanfall2VR.dll")
                    ?? throw new Exception("Titanfall 2 VR package is missing Titanfall2VR.dll");
                var probe = modArchive.GetEntry("xr_probe.exe")
                    ?? throw new Exception("Titanfall 2 VR package is missing xr_probe.exe");
                var monitor = modArchive.GetEntry("crash_monitor.exe")
                    ?? throw new Exception("VR package is missing crash_monitor.exe");
                var launch = modArchive.GetEntry("launch.json")
                    ?? throw new Exception("Titanfall 2 VR package is missing launch.json");
                var releaseEntry = modArchive.GetEntry("release.json")
                    ?? throw new Exception("Titanfall 2 VR package is missing release.json");
                using (var reader = new StreamReader(releaseEntry.Open()))
                    release = JsonUtil.Deserialize<ReleaseInfo>(reader.ReadToEnd());

                Extract(launcher, LauncherName, written);
                foreach (var entry in profileEntries)
                {
                    var relative = ProfileName + "/" + entry.FullName.Substring(sourcePrefix.Length);
                    Extract(entry, relative, written);
                }
                Extract(plugin, ProfileName + "/plugins/Titanfall2VR.dll", written);
                Extract(probe, ProfileName + "/tools/xr_probe.exe", written);
                Extract(monitor, ProfileName + "/tools/crash_monitor.exe", written);
                Extract(launch, ProfileName + "/tools/launch.json", written);
                foreach (var entry in modArchive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith("mods/", StringComparison.Ordinal)))
                    Extract(entry, ProfileName + "/" + entry.FullName, written);
            }

            if (previous?.files != null) InstallerFileSystem.RemoveStaleFiles(GamePath, previous.files, written);
            OwnedFileStore.Write(GamePath, new InstallRecord
            {
                id = InstallerId,
                version = release.version,
                northstarVersion = NorthstarVersion,
                runtime = "Northstar",
                beta = beta,
                files = written.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        void Extract(ZipArchiveEntry entry, string relativePath, ICollection<string> written)
        {
            var normalized = InstallerFileSystem.Normalize(relativePath);
            var destination = InstallerFileSystem.ResolveInside(GamePath, normalized);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            entry.ExtractToFile(destination, true);
            written.Add(normalized);
        }

        public static string SaveDirectory(string documentsPath) => Path.Combine(documentsPath, "Respawn", "Titanfall2_VR");
        public static string BaseSaveDirectory(string documentsPath) => Path.Combine(documentsPath, "Respawn", "Titanfall2");

        public static CampaignSaveStatus GetCampaignSaveStatus(string directory)
        {
            var count = CampaignFiles.Count(name => File.Exists(Path.Combine(directory, "profile", name)));
            return count == 0 ? CampaignSaveStatus.Missing : count == CampaignFiles.Length ? CampaignSaveStatus.Available : CampaignSaveStatus.Incomplete;
        }

        public static bool CanCopyCampaignSave(string documentsPath) =>
            GetCampaignSaveStatus(BaseSaveDirectory(documentsPath)) == CampaignSaveStatus.Available
            && GetCampaignSaveStatus(SaveDirectory(documentsPath)) == CampaignSaveStatus.Missing;

        public static void CopyCampaignSave(string documentsPath, bool overwrite = false)
        {
            if (Process.GetProcessesByName("Titanfall2").Any()) throw new InvalidOperationException("Close Titanfall 2 before copying saves.");
            if (GetCampaignSaveStatus(BaseSaveDirectory(documentsPath)) != CampaignSaveStatus.Available)
                throw new InvalidOperationException("No complete base game save to copy.");
            if (!overwrite && GetCampaignSaveStatus(SaveDirectory(documentsPath)) != CampaignSaveStatus.Missing)
                throw new InvalidOperationException("Copying would overwrite existing campaign progress.");
            var source = Path.Combine(BaseSaveDirectory(documentsPath), "profile");
            var destination = Path.Combine(SaveDirectory(documentsPath), "profile");
            foreach (var name in CampaignFiles)
            {
                var target = Path.Combine(destination, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(source, name), target, overwrite);
            }
        }

        public void Uninstall() => OwnedFileStore.Remove(GamePath, InstallerId);

        static ProcessStartInfo VrProcess(string gamePath, string executable)
        {
            var info = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = gamePath,
                UseShellExecute = false
            };
            foreach (var key in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
                if (key.StartsWith("TF2VR_", StringComparison.OrdinalIgnoreCase) || new[] { "VR_PATHREG_OVERRIDE", "VR_CONFIG_PATH", "VR_LOG_PATH", "XR_RUNTIME_JSON" }.Contains(key))
                    info.EnvironmentVariables.Remove(key);
            info.EnvironmentVariables["TF2VR_OPENXR"] = "1";
            return info;
        }

        public static ProcessStartInfo CreateLaunchInfo(string gamePath, OpenXrView[] views)
        {
            if (views.Length != 2 || views.Any(view => view.width <= 0 || view.height <= 0))
                throw new Exception("OpenXR must provide two valid eye resolutions");
            var height = views.Max(view => view.height);
            // eye sized buffers, the mod lays out native UI in their 16:9 corner
            var width = views.Max(view => view.width);
            var settings = JsonUtil.Deserialize<TitanfallLaunchSettings>(File.ReadAllText(Path.Combine(gamePath, ProfileName, "tools", "launch.json")));
            var info = VrProcess(gamePath, Path.Combine(gamePath, ProfileName, "tools", "crash_monitor.exe"));
            info.Arguments = "\"" + Path.Combine(gamePath, ProfileName) + "\" \"" + Path.Combine(gamePath, LauncherName) + "\" "
                + string.Join(" ", settings.arguments.Concat(settings.vrArguments)
                .Select(arg => arg.Replace("{profile}", ProfileName).Replace("{width}", width.ToString())
                    .Replace("{height}", height.ToString()).Replace("{sound}", "1")));
            return info;
        }

        public void Play()
        {
            var tools = Path.Combine(GamePath, ProfileName, "tools");
            var viewsPath = Path.Combine(tools, "xr_views.json");
            var info = VrProcess(GamePath, Path.Combine(tools, "xr_probe.exe"));
            info.Arguments = "--views \"" + viewsPath + "\"";
            info.CreateNoWindow = true;
            using (var probe = Process.Start(info))
            {
                if (!probe.WaitForExit(15000))
                {
                    probe.Kill();
                    throw new Exception("OpenXR probe timed out. Check your VR runtime and headset.");
                }
                if (probe.ExitCode != 0) throw new Exception("OpenXR probe failed. Check your VR runtime and headset.");
            }
            var views = JsonUtil.Deserialize<OpenXrView[]>(File.ReadAllText(viewsPath));
            Process.Start(CreateLaunchInfo(GamePath, views));
        }

        // same key OriginSDK and Northstar use to start the EA app
        public static string EaAppPath() =>
            SteamGameLocator.RegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Origin", "ClientPath");

        // opens visibly so sign in happens on the desktop, Northstar starts it minimized
        public static void OpenEaApp() => Process.Start(EaAppPath());

        // steam installs the EA app and links the account on first launch
        public static void LaunchFromSteam() => Process.Start(SteamGameLocator.SteamExePath(), "-applaunch " + SteamAppId);

        public static bool IsRunning() =>
            Process.GetProcessesByName("Titanfall2").Any() || Process.GetProcessesByName("Titanfall2VRLauncher").Any();
    }
}
