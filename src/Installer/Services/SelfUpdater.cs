using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace BigWalkVRInstaller.Services
{
    public static class SelfUpdater
    {
        const string OldSuffix = ".old.exe";

        public static string CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

        static string OldExePath => Path.ChangeExtension(ExePath, null) + OldSuffix;

        // delete leftover renamed exe from a previous update, the old process may still hold it so retry briefly
        public static void CleanupOldExe()
        {
            var old = OldExePath;
            Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    try
                    {
                        File.Delete(old);
                        return;
                    }
                    catch { } // still locked, try again next tick
                    await Task.Delay(500);
                }
            });
        }

        public static bool IsUpdateAvailable(ReleaseInfo mgr) =>
            mgr != null && !string.IsNullOrEmpty(mgr.url) && VersionUtil.IsNewer(mgr.version, CurrentVersion);

        // rename-swap: running exe can be renamed but not overwritten
        public static async Task ApplyUpdate(ReleaseInfo mgr, IProgress<double> progress = null)
        {
            var bytes = await RepoClient.Download(mgr.url, mgr.sha256, progress);
            var exe = ExePath;
            var old = OldExePath;
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.WriteAllBytes(exe, bytes);
            }
            catch
            {
                File.Move(old, exe); // roll back rename
                throw;
            }
            Process.Start(exe);
        }
    }
}
