using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BigWalkVRInstaller.Services
{
    public static class OwnedFileStore
    {
        static string DirectoryPath(string gamePath) => Path.Combine(gamePath, ".circuitlord-vr-mods");

        static string RecordPath(string gamePath, string id) =>
            InstallerFileSystem.ResolveInside(DirectoryPath(gamePath), id + ".json");

        public static InstallRecord Read(string gamePath, string id)
        {
            var path = RecordPath(gamePath, id);
            return File.Exists(path) ? JsonUtil.Deserialize<InstallRecord>(File.ReadAllText(path)) : null;
        }

        public static IEnumerable<string> Ids(string gamePath)
        {
            var directory = DirectoryPath(gamePath);
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension) : Enumerable.Empty<string>();
        }

        public static void Write(string gamePath, InstallRecord record)
        {
            Directory.CreateDirectory(DirectoryPath(gamePath));
            File.WriteAllText(RecordPath(gamePath, record.id), JsonUtil.Serialize(record));
        }

        public static void Remove(string gamePath, string id)
        {
            var record = Read(gamePath, id);
            if (record?.files != null)
                foreach (var relative in record.files) InstallerFileSystem.DeleteRelative(gamePath, relative);

            var recordPath = RecordPath(gamePath, id);
            if (File.Exists(recordPath)) File.Delete(recordPath);
            var directory = DirectoryPath(gamePath);
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
        }
    }
}
