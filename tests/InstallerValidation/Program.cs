using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;
using BigWalkVRInstaller;
using BigWalkVRInstaller.Installers;
using BigWalkVRInstaller.Services;

namespace InstallerValidation
{
    static class Program
    {

        [STAThread]
        static int Main()
        {
            var root = Path.Combine(Path.GetTempPath(), "CircuitLordInstallerValidation-" + Guid.NewGuid().ToString("N"));
            try
            {
                ValidateSavePrompt(root);
                Directory.CreateDirectory(Path.Combine(root, "R2Northstar"));
                File.WriteAllText(Path.Combine(root, "Titanfall2.exe"), "game");
                File.WriteAllText(Path.Combine(root, "NorthstarLauncher.exe"), "standard-launcher");
                File.WriteAllText(Path.Combine(root, "R2Northstar", "Northstar.dll"), "standard-profile");

                var sameBeta = new ManifestMod { version = "0.1.0", beta = new ModRelease { version = "0.1.0" } };
                var newerBeta = new ManifestMod { version = "0.1.0", beta = new ModRelease { version = "0.2.0" } };
                Assert(!sameBeta.HasNewerBeta, "stable-equivalent beta was available");
                Assert(newerBeta.HasNewerBeta, "newer beta was unavailable");

                var profile = Path.Combine(root, "TF2VR");
                var userFile = Path.Combine(profile, "save_data", "user.json");
                Directory.CreateDirectory(Path.GetDirectoryName(userFile));
                File.WriteAllText(userFile, "user-data");
                File.WriteAllText(Path.Combine(profile, "Northstar.dll"), "untracked-profile");
                File.WriteAllText(Path.Combine(root, "Titanfall2VRLauncher.exe"), "untracked-launcher");

                var documents = Path.Combine(root, "Documents");
                var defaultProfile = Path.Combine(documents, "Respawn", "Titanfall2", "profile");
                var saveDirectory = Titanfall2Installer.SaveDirectory(documents);
                Assert(saveDirectory == Path.Combine(documents, "Respawn", "Titanfall2_VR"), "wrong save directory");
                var vrProfile = Path.Combine(saveDirectory, "profile");
                var fnfSave = Path.Combine(documents, "Respawn", "Titanfall2_fnf", "profile", "savegames", "savegame.sav");
                Directory.CreateDirectory(Path.GetDirectoryName(fnfSave));
                File.WriteAllText(fnfSave, "other-mod-progress");
                Assert(!Titanfall2Installer.CanCopyCampaignSave(documents), "offered a missing campaign save");
                Directory.CreateDirectory(Path.Combine(defaultProfile, "savegames"));
                File.WriteAllText(Path.Combine(defaultProfile, "savegames", "savegame.sav"), "default-checkpoint");
                Assert(!Titanfall2Installer.CanCopyCampaignSave(documents), "offered an incomplete campaign profile");
                File.WriteAllText(Path.Combine(defaultProfile, "profile.cfg"), "campaign-unlocks");
                Assert(Titanfall2Installer.CanCopyCampaignSave(documents), "default campaign save was not offered");

                var installer = new Titanfall2Installer(new AppSettings { Titanfall2Path = root });
                installer.Install(NorthstarPackage("vr-launcher-v1", true), ModPackage("vr-plugin-v1", "0.1.0"), false);
                Assert(installer.Record.version == "0.1.0", "package version not recorded");

                Assert(File.ReadAllText(Path.Combine(root, "NorthstarLauncher.exe")) == "standard-launcher", "standard launcher changed");
                Assert(File.ReadAllText(Path.Combine(root, "R2Northstar", "Northstar.dll")) == "standard-profile", "standard profile changed");
                Assert(File.ReadAllText(Path.Combine(root, "Titanfall2VRLauncher.exe")) == "vr-launcher-v1", "renamed launcher missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "Northstar.dll")) == "vr-profile", "VR profile missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "vr-plugin-v1", "VR plugin missing");
                Assert(File.ReadAllText(userFile) == "user-data", "untracked user file changed during adoption");

                Assert(!Directory.Exists(vrProfile), "install copied campaign progress without consent");
                Titanfall2Installer.CopyCampaignSave(documents);
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "default-checkpoint", "checkpoint was not copied");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "profile.cfg")) == "campaign-unlocks", "campaign unlocks were not copied");
                Assert(!Titanfall2Installer.CanCopyCampaignSave(documents), "offered to replace existing VR progress");
                File.WriteAllText(Path.Combine(vrProfile, "savegames", "savegame.sav"), "vr-progress");
                var refusedOverwrite = false;
                try { Titanfall2Installer.CopyCampaignSave(documents); }
                catch (InvalidOperationException) { refusedOverwrite = true; }
                Assert(refusedOverwrite, "existing VR progress was overwritten");
                Assert(File.ReadAllText(Path.Combine(defaultProfile, "savegames", "savegame.sav")) == "default-checkpoint", "default checkpoint changed");
                Assert(File.ReadAllText(Path.Combine(defaultProfile, "profile.cfg")) == "campaign-unlocks", "default profile changed");

                Assert(installer.IsInstalled, "complete VR package was not recognized");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "tools", "xr_probe.exe")) == "probe", "resolution probe missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe")) == "monitor", "crash monitor missing");
                Assert(File.Exists(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod.json")), "cockpit assets missing");
                var launch = Titanfall2Installer.CreateLaunchInfo(root, new[] {
                    new OpenXrView { width = 2100, height = 2200 }, new OpenXrView { width = 2000, height = 2160 }
                });
                Assert(launch.FileName == Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe"), "launch bypassed crash capture");
                Assert(launch.Arguments == "\"" + profile + "\" \"" + Path.Combine(root, "Titanfall2VRLauncher.exe")
                    + "\" -profile=TF2VR -windowed -w 2100 -h 2200 +sound_without_focus 1 +mat_vsync_mode 0", "wrong monitored launch arguments");
                Assert(launch.EnvironmentVariables["TF2VR_OPENXR"] == "1", "OpenXR was not enabled");
                Assert(!launch.EnvironmentVariables.ContainsKey("TF2VR_DEV_SESSION"), "development session inherited");
                Assert(!launch.EnvironmentVariables.ContainsKey("XR_RUNTIME_JSON"), "runtime override inherited");
                Assert(!launch.UseShellExecute, "VR environment cannot reach launcher");
                Assert(launch.WorkingDirectory == root, "wrong working directory");
                var wide = Titanfall2Installer.CreateLaunchInfo(root, new[] {
                    new OpenXrView { width = 4000, height = 1000 }, new OpenXrView { width = 4100, height = 900 }
                });
                Assert(wide.Arguments.Contains("-w 4100 -h 1000"), "wide eye resolution was cropped");

                installer.Install(NorthstarPackage("vr-launcher-v2", false), ModPackage("vr-plugin-v2", "0.2.0"), true);
                Assert(installer.Record.version == "0.2.0", "updated package version not recorded");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "plugins", "ranim.dll")), "stale owned file survived update");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during update");
                Assert(installer.Record.beta, "beta channel was not recorded");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "update changed VR progress");

                var logs = Path.Combine(root, "TF2VR", "logs");
                var diagnostics = Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR-data");
                Directory.CreateDirectory(logs);
                Directory.CreateDirectory(diagnostics);
                File.WriteAllText(Path.Combine(logs, "nslog-test.txt"), "northstar-log");
                File.WriteAllText(Path.Combine(logs, "nsdump-test.dmp"), "minidump");
                File.WriteAllText(Path.Combine(diagnostics, "engine.txt"), "engine-log");
                File.WriteAllText(Path.Combine(diagnostics, "events.txt"), "events");
                var report = CrashReportService.CreateTitanfall(root, root);
                using (var archive = ZipFile.OpenRead(report))
                {
                    Assert(archive.GetEntry("Northstar/nslog-test.txt") != null, "Northstar log missing from crash report");
                    Assert(archive.GetEntry("Northstar/nsdump-test.dmp") == null, "Northstar memory dump was packaged");
                    Assert(archive.GetEntry("Titanfall2VR/engine.txt") != null, "engine log missing from crash report");
                    Assert(archive.GetEntry("report.json") != null, "crash metadata missing from report");
                }

                ValidateCrashCaptureReport(root, profile);

                installer.Uninstall();
                Assert(!File.Exists(Path.Combine(root, "Titanfall2VRLauncher.exe")), "renamed launcher survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "Northstar.dll")), "VR profile survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "tools", "xr_probe.exe")), "probe survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe")), "monitor survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod.json")), "cockpit survived uninstall");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during uninstall");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "uninstall changed VR progress");
                Assert(File.ReadAllText(Path.Combine(root, "NorthstarLauncher.exe")) == "standard-launcher", "standard launcher changed after uninstall");
                Assert(File.ReadAllText(Path.Combine(root, "R2Northstar", "Northstar.dll")) == "standard-profile", "standard profile changed after uninstall");

                installer.Install(NorthstarPackage("vr-launcher-v3", false), ModPackage("vr-plugin-v3", "0.3.0"), false);
                Assert(installer.IsInstalled, "VR package was not recognized after reinstall");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "vr-plugin-v3", "reinstalled VR plugin missing");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during reinstall");

                Assert(!Titanfall2Installer.CanCopyCampaignSave(documents), "reinstall offered to replace VR progress");
                Assert(File.ReadAllText(fnfSave) == "other-mod-progress", "FNF campaign progress changed");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "reinstall changed campaign progress");
                Console.WriteLine("validated TF2VR install, save import consent, save isolation, updates, crash reports, uninstall, and reinstall");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static void ValidateSavePrompt(string root)
        {
            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            Assert((string)((Button)window.FindName("TitanfallSavesButton")).Content == "Campaign saves", "saves modal button missing");
            Assert((string)((Button)window.FindName("TitanfallSaveDirectoryButton")).Content == "Open folder", "save folder label changed");
            Assert((string)((Button)window.FindName("TitanfallCopySaveButton")).Content == "Import", "import label changed");
            var confirm = typeof(MainWindow).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic);
            var close = typeof(MainWindow).GetMethod("CloseConfirm", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var answer in new[] { false, true })
            {
                var result = (Task<bool>)confirm.Invoke(window, new object[] { "Copy your existing campaign save?", "Launching from this installer uses a new save directory\n\nPress Yes if you'd like to copy your existing save file as a starting point.", "Yes", false, "No" });
                Assert((string)((Button)window.FindName("ConfirmOk")).Content == "Yes", "save prompt has no Yes choice");
                Assert((string)((Button)window.FindName("ConfirmCancel")).Content == "No", "save prompt has no No choice");
                close.Invoke(window, new object[] { answer });
                Assert(result.Result == answer, "save prompt returned the wrong choice");
            }
            confirm.Invoke(window, new object[] { "Uninstall", "Remove files", "Uninstall", true, "Cancel" });
            Assert((string)((Button)window.FindName("ConfirmCancel")).Content == "Cancel", "save prompt changed other dialogs");
            close.Invoke(window, new object[] { false });
            ValidateSaveManagement(window, Path.Combine(root, "save-dialog"));
            window.Close();
        }

        static void ValidateSaveManagement(MainWindow window, string documents)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var show = typeof(MainWindow).GetMethod("ShowTitanfallSaves", flags);
            var copy = typeof(MainWindow).GetMethod("CopyTitanfallSave", flags);
            var confirm = typeof(MainWindow).GetMethod("CloseConfirm", flags);
            var overlay = (Border)window.FindName("TitanfallSavesOverlay");
            var baseStatus = (TextBlock)window.FindName("TitanfallBaseSaveStatus");
            var vrStatus = (TextBlock)window.FindName("TitanfallVrSaveStatus");
            var copyButton = (Button)window.FindName("TitanfallCopySaveButton");
            var source = Path.Combine(Titanfall2Installer.BaseSaveDirectory(documents), "profile");
            var destination = Path.Combine(Titanfall2Installer.SaveDirectory(documents), "profile");
            show.Invoke(window, new object[] { documents });
            Assert(overlay.Visibility == System.Windows.Visibility.Visible, "saves modal did not open");
            Assert(((TextBlock)window.FindName("TitanfallSaveMessage")).Visibility == System.Windows.Visibility.Collapsed, "empty save message left a gap");
            Assert(!((Grid)window.FindName("InstallerView")).IsEnabled, "saves modal left launch controls enabled");
            Assert(baseStatus.Text == "No save" && vrStatus.Text == "No save", "missing save status incorrect");
            Assert(!copyButton.IsEnabled, "copy enabled without a source save");
            Assert(((TextBlock)window.FindName("TitanfallSaveHelp")).Text == "Import needs a complete base game save.", "disabled import was not explained");
            var rejected = false;
            try { Titanfall2Installer.CopyCampaignSave(documents, overwrite: true); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && !Directory.Exists(destination), "missing source created a destination save");

            Directory.CreateDirectory(Path.Combine(source, "savegames"));
            File.WriteAllText(Path.Combine(source, "savegames", "savegame.sav"), "base-checkpoint");
            show.Invoke(window, new object[] { documents });
            Assert(baseStatus.Text == "Incomplete save" && !copyButton.IsEnabled, "incomplete source allowed copying");
            File.WriteAllText(Path.Combine(source, "profile.cfg"), "base-unlocks");
            show.Invoke(window, new object[] { documents });
            Assert(baseStatus.Text == "Save found" && copyButton.IsEnabled, "complete source was not available");
            Assert(((TextBlock)window.FindName("TitanfallSaveHelp")).Text == "Import your base game progress into the VR save.", "import direction was not explained");
            foreach (var answer in new[] { false, true })
            {
                var task = (Task)copy.Invoke(window, new object[] { documents });
                Assert(!task.IsCompleted, "initial import did not wait for confirmation");
                Assert(((TextBlock)window.FindName("ConfirmTitle")).Text == "Import base game save?", "initial import confirmation missing");
                Assert((string)((Button)window.FindName("ConfirmOk")).Content == "Import", "initial import action unclear");
                Assert(!Directory.Exists(destination), "initial import wrote before confirmation");
                confirm.Invoke(window, new object[] { answer });
                task.GetAwaiter().GetResult();
                Assert(Directory.Exists(destination) == answer, "initial import ignored consent");
            }
            Assert(vrStatus.Text == "Save found", "status did not refresh after copy");
            Assert(((TextBlock)window.FindName("TitanfallSaveMessage")).Visibility == System.Windows.Visibility.Visible, "copy result was hidden");
            Assert(File.ReadAllText(Path.Combine(destination, "savegames", "savegame.sav")) == "base-checkpoint", "initial modal copy failed");

            foreach (var partial in new[] { false, true })
            {
                File.WriteAllText(Path.Combine(destination, "profile.cfg"), "vr-unlocks");
                if (partial) File.Delete(Path.Combine(destination, "savegames", "savegame.sav"));
                else File.WriteAllText(Path.Combine(destination, "savegames", "savegame.sav"), "vr-checkpoint");
                show.Invoke(window, new object[] { documents });
                Assert(vrStatus.Text == (partial ? "Incomplete save" : "Save found"), "destination status incorrect");
                Assert(!Titanfall2Installer.CanCopyCampaignSave(documents), "install import allowed an existing destination");
                foreach (var answer in new[] { false, true })
                {
                    var task = (Task)copy.Invoke(window, new object[] { documents });
                    Assert(!task.IsCompleted, "overwrite did not wait for confirmation");
                    Assert(((TextBlock)window.FindName("ConfirmTitle")).Text == "Overwrite VR save?", "overwrite warning missing");
                    Assert((string)((Button)window.FindName("ConfirmOk")).Content == "Overwrite", "overwrite action unclear");
                    Assert(File.ReadAllText(Path.Combine(destination, "profile.cfg")) == "vr-unlocks", "save changed before confirmation");
                    confirm.Invoke(window, new object[] { answer });
                    task.GetAwaiter().GetResult();
                    Assert(File.ReadAllText(Path.Combine(destination, "profile.cfg")) == (answer ? "base-unlocks" : "vr-unlocks"), "overwrite consent ignored");
                    var save = Path.Combine(destination, "savegames", "savegame.sav");
                    if (answer) Assert(File.ReadAllText(save) == "base-checkpoint", "confirmed overwrite did not copy the checkpoint");
                    else if (partial) Assert(!File.Exists(save), "cancel created a checkpoint");
                    else Assert(File.ReadAllText(save) == "vr-checkpoint", "cancel changed the checkpoint");
                }
            }
            Assert(File.ReadAllText(Path.Combine(source, "profile.cfg")) == "base-unlocks", "copy changed base progress");
            Assert(File.ReadAllText(Path.Combine(source, "savegames", "savegame.sav")) == "base-checkpoint", "copy changed the base checkpoint");
            Assert(vrStatus.Text == "Save found", "overwrite left stale status");
            ((Button)window.FindName("TitanfallSavesCloseButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            Assert(overlay.Visibility == System.Windows.Visibility.Collapsed, "saves modal did not close");
            Assert(((Grid)window.FindName("InstallerView")).IsEnabled, "closing saves modal left launch controls disabled");
        }

        static void ValidateCrashCaptureReport(string root, string profile)
        {
            var session = Path.Combine(profile, "crashes", "20260920T000000000Z-11");
            var incident = Path.Combine(session, "exception-11");
            Directory.CreateDirectory(incident);
            File.WriteAllText(Path.Combine(session, "incident.txt"), "exception");
            File.WriteAllText(Path.Combine(session, "monitor.txt"), "process_exit code=3221225477");
            File.WriteAllText(Path.Combine(session, "session.json"), "{\"modSha256\":\"captured-plugin\"}");
            File.WriteAllText(Path.Combine(incident, "capture.txt"), "dump_written=1");
            File.WriteAllText(Path.Combine(incident, "engine.txt"), "frozen-log");
            File.WriteAllText(Path.Combine(incident, "stacks.txt"), "thread 11 event\n  Titanfall2VR.dll+0x11");
            File.WriteAllText(Path.Combine(incident, "process.dmp"), "captured-memory");
            File.WriteAllText(Path.Combine(incident, "process.partial"), "incomplete-memory");
            var later = Path.Combine(profile, "crashes", "20260920T010000000Z-12");
            Directory.CreateDirectory(later);
            File.WriteAllText(Path.Combine(later, "monitor.txt"), "normal exit");
            var report = CrashReportService.CreateTitanfall(root, root);
            using (var archive = ZipFile.OpenRead(report))
            {
                Assert(archive.GetEntry("Capture/exception-11/stacks.txt") != null, "captured stacks missing");
                Assert(!archive.Entries.Any(entry => entry.Name.StartsWith("process.")), "memory dump was packaged");
                Assert(archive.GetEntry("Northstar/nsdump-test.dmp") == null, "unrelated Northstar dump was mixed into capture");
                using (var reader = new StreamReader(archive.GetEntry("Capture/exception-11/engine.txt").Open()))
                    Assert(reader.ReadToEnd() == "frozen-log", "live logs replaced incident logs");
                using (var reader = new StreamReader(archive.GetEntry("report.json").Open()))
                {
                    var metadata = JsonUtil.Deserialize<TitanfallCrashMetadata>(reader.ReadToEnd());
                    Assert(metadata.capturedModSha256 == "captured-plugin", "installed binary replaced capture identity");
                }
            }
            File.WriteAllText(Path.Combine(later, "incident.txt"), "monitor_error");
            File.WriteAllText(Path.Combine(later, "session.json"), "{\"modSha256\":\"later-plugin\"}");
            var missing = CrashReportService.CreateTitanfall(root, root);
            using (var archive = ZipFile.OpenRead(missing))
                Assert(!archive.Entries.Any(entry => entry.Name == "stacks.txt"), "capture failure borrowed older stacks");
        }

        static byte[] NorthstarPackage(string launcher, bool includeRanim)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(archive, "NorthstarLauncher.exe", launcher);
                    Add(archive, "R2Northstar/Northstar.dll", "vr-profile");
                    if (includeRanim) Add(archive, "R2Northstar/plugins/ranim.dll", "ranim");
                }
                return stream.ToArray();
            }
        }

        static byte[] ModPackage(string plugin, string version)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(archive, "Titanfall2VR.dll", plugin);
                    Add(archive, "release.json", "{\n  \"version\": \"" + version + "\",\n  \"description\": \"test\"\n}\n");
                    Add(archive, "xr_probe.exe", "probe");
                    Add(archive, "crash_monitor.exe", "monitor");
                    Add(archive, "launch.json", JsonUtil.Serialize(new TitanfallLaunchSettings {
                        arguments = new[] { "-profile={profile}", "-windowed", "-w", "{width}", "-h", "{height}", "+sound_without_focus", "{sound}" },
                        vrArguments = new[] { "+mat_vsync_mode", "0" }
                    }));
                    Add(archive, "mods/Titanfall2VR.Cockpit/mod.json", "{}");
                }
                return stream.ToArray();
            }
        }

        static void Add(ZipArchive archive, string path, string contents)
        {
            var entry = archive.CreateEntry(path);
            using (var output = entry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(contents);
                output.Write(bytes, 0, bytes.Length);
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
