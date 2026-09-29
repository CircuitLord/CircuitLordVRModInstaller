using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BigWalkVRInstaller.Services
{
    public class TitanfallCrashMetadata
    {
        public string generatedUtc;
        public string installerVersion;
        public string modVersion;
        public string northstarVersion;
        public string modSha256;
        public string osVersion;
        public bool is64BitOperatingSystem;
        public string captureSession;
        public string capturedModSha256;
    }

    public sealed class TitanfallCaptureIdentity
    {
        public string modSha256;
    }

    public static class CrashReportService
    {
        public static string Create(string gamePath)
        {
            var logs = new Dictionary<string, string>
            {
                ["BepInEx-LogOutput.log"] = Path.Combine(gamePath, "BepInEx", "LogOutput.log"),
                ["Unity-Player.log"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "House House", "Big Walk", "Player.log")
            };

            if (!logs.Any(log => File.Exists(log.Value))) throw new Exception("No Big Walk log files were found.");
            var reportPath = DesktopReportPath("BigWalkVR");
            using (var stream = File.Create(reportPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var log in logs)
                    if (File.Exists(log.Value)) AddFile(archive, log.Value, log.Key);
            return reportPath;
        }

        // reports leave out memory dumps so they can be shared publicly
        public static string CreateTitanfall(string gamePath, string outputDirectory = null)
        {
            var profile = Path.Combine(gamePath, "TF2VR");
            var files = new Dictionary<string, string>();
            var captures = Path.Combine(profile, "crashes");
            var sessions = Directory.Exists(captures) ? new DirectoryInfo(captures).GetDirectories() : Array.Empty<DirectoryInfo>();
            var session = sessions.Where(candidate => File.Exists(Path.Combine(candidate.FullName, "incident.txt")))
                .OrderByDescending(candidate => candidate.Name, StringComparer.Ordinal).FirstOrDefault();
            string capturedHash = null;
            if (session != null)
            {
                var names = new HashSet<string> { "session.json", "monitor.txt", "incident.txt", "capture.txt", "stacks.txt", "engine.txt", "events.txt", "runtime.txt", "frames.csv", "northstar.txt" };
                foreach (var file in session.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => names.Contains(file.Name)))
                    files["Capture/" + file.FullName.Substring(session.FullName.Length + 1).Replace('\\', '/')] = file.FullName;
                var identity = Path.Combine(session.FullName, "session.json");
                if (File.Exists(identity)) capturedHash = JsonUtil.Deserialize<TitanfallCaptureIdentity>(File.ReadAllText(identity)).modSha256;
            }
            else
            {
                AddNewest(files, Path.Combine(profile, "logs"), "nslog*.txt", "Northstar");
                var data = Path.Combine(profile, "plugins", "Titanfall2VR-data");
                foreach (var name in new[] { "engine.txt", "events.txt", "runtime.txt", "frames.csv" })
                {
                    var path = Path.Combine(data, name);
                    if (File.Exists(path)) files["Titanfall2VR/" + name] = path;
                }
                var latest = sessions.OrderByDescending(candidate => candidate.Name, StringComparer.Ordinal).FirstOrDefault();
                if (latest != null) files["Capture/monitor.txt"] = Path.Combine(latest.FullName, "monitor.txt");
            }
            if (files.Count == 0) throw new Exception("No Titanfall 2 VR crash files were found.");

            var record = OwnedFileStore.Read(gamePath, "Titanfall2VR");
            var plugin = Path.Combine(profile, "plugins", "Titanfall2VR.dll");
            var metadata = new TitanfallCrashMetadata
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                installerVersion = SelfUpdater.CurrentVersion,
                modVersion = record?.version,
                northstarVersion = record?.northstarVersion,
                modSha256 = File.Exists(plugin) ? RepoClient.Sha256(File.ReadAllBytes(plugin)) : null,
                osVersion = Environment.OSVersion.VersionString,
                is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                captureSession = session?.Name,
                capturedModSha256 = capturedHash
            };

            var directory = outputDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var reportPath = Path.Combine(directory, $"Titanfall2VR-CrashReport-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var stream = File.Create(reportPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var file in files) AddFile(archive, file.Value, file.Key);
                var entry = archive.CreateEntry("report.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(entry.Open())) writer.Write(JsonUtil.Serialize(metadata));
            }
            return reportPath;
        }

        static string DesktopReportPath(string name) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"{name}-CrashReport-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        static void AddNewest(IDictionary<string, string> files, string directory, string pattern, string entryDirectory)
        {
            if (!Directory.Exists(directory)) return;
            var file = new DirectoryInfo(directory).EnumerateFiles(pattern)
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .FirstOrDefault();
            if (file != null) files[entryDirectory + "/" + file.Name] = file.FullName;
        }

        static void AddFile(ZipArchive archive, string path, string entryName)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = entry.Open()) input.CopyTo(output);
        }
    }
}
