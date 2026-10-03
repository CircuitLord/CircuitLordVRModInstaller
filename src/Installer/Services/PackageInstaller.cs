using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BigWalkVRInstaller.Services
{
    // installs mod packages as zips that mirror the game folder layout
    public static class PackageInstaller
    {
        const string RuntimePayloadPrefix = "BepInEx\\patchers\\BigWalkVR.Runtime\\";
        static readonly HashSet<string> DistributionMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".package.json", "manifest.json", "icon.png", "README.md"
        };

        static string RecordDir(string gamePath) => Path.Combine(gamePath, "UserData", "BigWalkVRInstaller");

        static string RecordPath(string gamePath, string id)
        {
            ValidateId(id);
            return ResolveInside(RecordDir(gamePath), id + ".json");
        }

        static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Any(c => !IsIdCharacter(c)))
                throw new Exception($"invalid mod id: {id}");
        }

        static bool IsIdCharacter(char c) =>
            c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_';

        public static InstallRecord ReadRecord(string gamePath, string id)
        {
            try
            {
                var path = RecordPath(gamePath, id);
                if (!File.Exists(path)) return null;
                return JsonUtil.Deserialize<InstallRecord>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        public static List<InstallRecord> ReadRecords(string gamePath)
        {
            var directory = RecordDir(gamePath);
            if (!Directory.Exists(directory)) return new List<InstallRecord>();
            return Directory.EnumerateFiles(directory, "*.json")
                .Select(path => ReadRecord(gamePath, Path.GetFileNameWithoutExtension(path)))
                .Where(record => record != null)
                .ToList();
        }

        public static void Install(string gamePath, ManifestMod mod, byte[] zipBytes, string channel)
        {
            ValidateId(mod.id);
            var previous = ReadRecord(gamePath, mod.id);
            var preserve = PathSet(mod.preserve);
            var tokenize = PathSet(mod.tokenize);
            var written = new List<string>();

            using (var stream = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Length == 0) continue; // directory entry
                    var relative = Normalize(entry.FullName);
                    if (DistributionMetadata.Contains(relative)) continue;
                    var dest = ResolveInside(gamePath, relative);

                    written.Add(relative);
                    if (preserve.Contains(relative) && File.Exists(dest)) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                    if (tokenize.Contains(relative)) FillTokens(dest, gamePath);
                }
            }

            if (written.Count == 0) throw new Exception("package is empty");

            written = IncludeDeployedPaths(written);

            // drop files the previous version shipped that this one dropped
            if (previous?.files != null)
            {
                var current = new HashSet<string>(written, StringComparer.OrdinalIgnoreCase);
                foreach (var stale in IncludeDeployedPaths(previous.files).Where(f => !current.Contains(f)))
                    DeleteRelative(gamePath, stale);
            }

            WriteRecord(gamePath, new InstallRecord { id = mod.id, version = mod.version, runtime = "BepInEx", channel = channel, files = written });
        }

        public static void Uninstall(string gamePath, string id)
        {
            ValidateId(id);
            var record = ReadRecord(gamePath, id);
            if (record?.files != null)
                foreach (var relative in IncludeDeployedPaths(record.files))
                    DeleteRelative(gamePath, relative);

            var path = RecordPath(gamePath, id);
            if (File.Exists(path)) File.Delete(path);
        }

        internal static void ClearRecords(string gamePath)
        {
            var directory = RecordDir(gamePath);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        static void WriteRecord(string gamePath, InstallRecord record)
        {
            Directory.CreateDirectory(RecordDir(gamePath));
            File.WriteAllText(RecordPath(gamePath, record.id), JsonUtil.Serialize(record));
        }

        static List<string> IncludeDeployedPaths(IEnumerable<string> paths)
        {
            var result = paths.ToList();
            result.AddRange(result
                .Where(path => path.StartsWith(RuntimePayloadPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Substring(RuntimePayloadPrefix.Length))
                .ToList());
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static void DeleteRelative(string gamePath, string relative)
        {
            try { InstallerFileSystem.DeleteRelative(gamePath, relative); }
            catch { }
        }

        static void FillTokens(string path, string gamePath)
        {
            var text = File.ReadAllText(path);
            text = text.Replace("{{GAMEDIR_JSON}}", gamePath.Replace("\\", "\\\\")).Replace("{{GAMEDIR}}", gamePath);
            File.WriteAllText(path, text);
        }

        static string Normalize(string entryPath) => InstallerFileSystem.Normalize(entryPath);

        // manifest authors may use either slash, match on the normalized form
        static HashSet<string> PathSet(List<string> paths) =>
            new HashSet<string>((paths ?? new List<string>()).Select(Normalize), StringComparer.OrdinalIgnoreCase);

        // keeps a malicious or malformed package from writing outside the game folder
        internal static string ResolveInside(string gamePath, string relative) =>
            InstallerFileSystem.ResolveInside(gamePath, relative);
    }
}
