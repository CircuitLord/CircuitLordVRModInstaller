using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
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

    public sealed class OpenXrRuntimeManifest
    {
        public OpenXrRuntimeInfo runtime;
    }

    public sealed class OpenXrRuntimeInfo
    {
        public string name;
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
        public const string AssetPatcherName = "asset_patcher.exe";
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

        // building game assets takes most of the install, extracting files the rest
        const double AssetShare = 0.8;

        public void Install(byte[] northstarPackage, byte[] modPackage, bool beta, IProgress<double> progress)
        {
            var work = Path.Combine(Path.GetTempPath(), "Titanfall2VR-install-" + Guid.NewGuid().ToString("N"));
            try { Install(northstarPackage, modPackage, beta, work, progress); }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        void Install(byte[] northstarPackage, byte[] modPackage, bool beta, string work, IProgress<double> progress)
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
                var patcher = modArchive.GetEntry(AssetPatcherName)
                    ?? throw new Exception("Titanfall 2 VR package is missing " + AssetPatcherName);
                using (var reader = new StreamReader(releaseEntry.Open()))
                    release = JsonUtil.Deserialize<ReleaseInfo>(reader.ReadToEnd());

                // before anything in the game folder changes
                var assets = BuildGameAssets(patcher, modArchive, work, built => progress.Report(AssetShare * built));
                var files = new List<(long size, Action write)> { (launcher.Length, () => Extract(launcher, LauncherName, written)) };
                foreach (var entry in profileEntries)
                    files.Add((entry.Length, () => Extract(entry, ProfileName + "/" + entry.FullName.Substring(sourcePrefix.Length), written)));
                files.Add((plugin.Length, () => Extract(plugin, ProfileName + "/plugins/Titanfall2VR.dll", written)));
                files.Add((probe.Length, () => Extract(probe, ProfileName + "/tools/xr_probe.exe", written)));
                files.Add((monitor.Length, () => Extract(monitor, ProfileName + "/tools/crash_monitor.exe", written)));
                files.Add((launch.Length, () => Extract(launch, ProfileName + "/tools/launch.json", written)));
                foreach (var entry in modArchive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith("mods/", StringComparison.Ordinal)))
                    files.Add((entry.Length, () => Extract(entry, ProfileName + "/" + entry.FullName, written)));
                foreach (var file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
                    files.Add((new FileInfo(file).Length, () => Copy(file, ProfileName + "/" + file.Substring(assets.Length + 1).Replace('\\', '/'), written)));

                var total = files.Sum(file => file.size);
                long done = 0;
                foreach (var file in files)
                {
                    file.write();
                    done += file.size;
                    progress.Report(AssetShare + (1 - AssetShare) * done / total);
                }
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

        // game derived files ship as patches, the patcher builds them from the installed game and writes them only once every file verifies
        string BuildGameAssets(ZipArchiveEntry patcher, ZipArchive modArchive, string work, Action<double> built)
        {
            foreach (var entry in modArchive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith("patches/", StringComparison.Ordinal)).Append(patcher))
            {
                var destination = Path.Combine(work, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                entry.ExtractToFile(destination);
            }
            var assets = Path.Combine(work, "assets");
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(work, AssetPatcherName),
                Arguments = "apply \"" + GamePath + "\" \"" + Path.Combine(work, "patches") + "\" \"" + assets + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                // "progress <read bytes> <total bytes>" lines
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    var parts = line.Split(' ');
                    if (parts[0] == "progress") built((double)long.Parse(parts[1]) / long.Parse(parts[2]));
                }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Couldn't build the mod's game assets: " + error.Result.Trim());
            }
            return assets;
        }

        void Extract(ZipArchiveEntry entry, string relativePath, ICollection<string> written) =>
            entry.ExtractToFile(Destination(relativePath, written), true);

        void Copy(string file, string relativePath, ICollection<string> written) =>
            File.Copy(file, Destination(relativePath, written), true);

        string Destination(string relativePath, ICollection<string> written)
        {
            var normalized = InstallerFileSystem.Normalize(relativePath);
            var destination = InstallerFileSystem.ResolveInside(GamePath, normalized);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            written.Add(normalized);
            return destination;
        }

        public static string SaveDirectory(string documentsPath) => Path.Combine(documentsPath, "Respawn", "Titanfall2_VR");

        public static CampaignSaveStatus GetCampaignSaveStatus(string directory)
        {
            var count = CampaignFiles.Count(name => File.Exists(Path.Combine(directory, "profile", name)));
            return count == 0 ? CampaignSaveStatus.Missing : count == CampaignFiles.Length ? CampaignSaveStatus.Available : CampaignSaveStatus.Incomplete;
        }

        public void Uninstall() => OwnedFileStore.Remove(GamePath, InstallerId);

        // the EA app installs under Program Files, where only admins can write
        public bool CanWriteGameFolder()
        {
            var probe = Path.Combine(GamePath, ".tf2vr-write-test");
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
        }

        public const string GrantAccessArgument = "--grant-game-folder-access";

        // one UAC prompt for this installer gives this user modify rights on the game folder, so installs, launches, and the game's own logs work without admin
        public void GrantGameFolderAccess()
        {
            var info = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                $"{GrantAccessArgument} \"{GamePath}\" {WindowsIdentity.GetCurrent().User.Value}")
            {
                Verb = "runas",
                UseShellExecute = true
            };
            Process process;
            try { process = Process.Start(info); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new Exception("the Windows prompt was declined, so the installer can't write to the game folder."); }
            using (process)
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception($"couldn't unlock {GamePath}.");
            }
            if (!CanWriteGameFolder()) throw new Exception($"{GamePath} is still read only after unlocking it.");
        }

        // runs in the elevated copy, every file in the game folder inherits the rule
        public static void ApplyGameFolderAccess(string path, string sid)
        {
            var security = Directory.GetAccessControl(path, AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }

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
            info.RedirectStandardError = true;
            using (var probe = Process.Start(info))
            {
                // the probe's last line names the failing OpenXR call, after any loader errors
                var error = probe.StandardError.ReadToEndAsync();
                if (!probe.WaitForExit(15000))
                {
                    probe.Kill();
                    throw new Exception("The headset check timed out. " + RuntimeHelp());
                }
                if (probe.ExitCode != 0)
                    throw new Exception($"Couldn't reach your headset through OpenXR. {RuntimeHelp()}\nDetails: {error.Result.Trim().Split('\n').Last().Trim()} (exit 0x{probe.ExitCode:X8})");
            }
            var views = JsonUtil.Deserialize<OpenXrView[]>(File.ReadAllText(viewsPath));
            Process.Start(CreateLaunchInfo(GamePath, views));
        }

        static string RuntimeHelp()
        {
            string state;
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1"))
            {
                var manifest = key?.GetValue("ActiveRuntime") as string;
                if (manifest == null) state = "No OpenXR runtime is set.";
                else if (!File.Exists(manifest)) state = $"Your OpenXR runtime is set to {manifest}, which no longer exists.";
                // name is optional in runtime manifests
                else state = $"Your active OpenXR runtime is {JsonUtil.Deserialize<OpenXrRuntimeManifest>(File.ReadAllText(manifest)).runtime.name ?? Path.GetFileName(manifest)}.";
            }
            return state + " Make sure your headset is connected and that runtime is running, or set the runtime you play with as active (for SteamVR: SteamVR Settings > OpenXR > Set SteamVR as OpenXR runtime).";
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
