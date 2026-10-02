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
        static int Main(string[] args) => args.Length == 0 ? Validate() : args[0] == "apply" ? FakeAssetPatcher(args) : FakeMonitor(args);

        static int FakeMonitor(string[] args)
        {
            File.WriteAllLines(Path.Combine(args[0], "monitor-launch.txt"), args);
            File.WriteAllText(Path.Combine(args[0], "monitor-started"), "");
            return 0;
        }

        // the installer runs this exe as the package's asset patcher, a fail-assets file in the game folder makes it fail
        static int FakeAssetPatcher(string[] args)
        {
            if (args[0] != "apply" || !File.Exists(Path.Combine(args[2], "manifest.json"))) return 2;
            if (File.Exists(Path.Combine(args[1], "fail-assets")))
            {
                Console.Error.WriteLine("game file differs");
                return 1;
            }
            Console.WriteLine("progress 1 4");
            Console.WriteLine("progress 4 4");
            var output = Path.Combine(args[3], "mods", "Titanfall2VR.Cockpit", "mod", "patched.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, "patched");
            return 0;
        }

        // records reports synchronously, unlike Progress<T>
        sealed class ProgressLog : IProgress<double>
        {
            public readonly System.Collections.Generic.List<double> Values = new System.Collections.Generic.List<double>();
            public void Report(double value) => Values.Add(value);
        }

        static int Validate()
        {
            var root = Path.Combine(Path.GetTempPath(), "CircuitLordInstallerValidation-" + Guid.NewGuid().ToString("N"));
            try
            {
                ValidateSaveModal(root);
                ValidateChannelMenu();
                ValidateNorthstarCache(root);
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
                var localAppData = Path.Combine(root, "Local");
                var saveDirectory = Titanfall2Installer.SaveDirectory(localAppData);
                Assert(saveDirectory == Path.Combine(localAppData, "Respawn", "Titanfall2_VR"), "wrong save directory");
                var vrProfile = Path.Combine(saveDirectory, "profile");
                var fnfSave = Path.Combine(documents, "Respawn", "Titanfall2_fnf", "profile", "savegames", "savegame.sav");
                Directory.CreateDirectory(Path.GetDirectoryName(fnfSave));
                File.WriteAllText(fnfSave, "other-mod-progress");
                Directory.CreateDirectory(Path.Combine(defaultProfile, "savegames"));
                File.WriteAllText(Path.Combine(defaultProfile, "savegames", "savegame.sav"), "default-checkpoint");
                File.WriteAllText(Path.Combine(defaultProfile, "profile.cfg"), "campaign-unlocks");

                var installer = new Titanfall2Installer(new AppSettings { Titanfall2Path = root });
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                Icacls(root, $"/inheritance:r /grant:r *{user}:(OI)(CI)RX *S-1-5-18:(OI)(CI)F");
                Assert(!installer.CanWriteGameFolder(), "protected game folder was reported writable");
                // the elevated step's own code, the test folder's owner can change its access without admin
                Titanfall2Installer.ApplyGameFolderAccess(root, user);
                Assert(installer.CanWriteGameFolder(), "unlocked game folder was reported read only");
                File.AppendAllText(Path.Combine(root, "R2Northstar", "Northstar.dll"), "");
                var progress = new ProgressLog();
                installer.Install(NorthstarPackage("vr-launcher-v1", true), ModPackage("vr-plugin-v1", "0.1.0"), false, progress);
                Assert(installer.Record.version == "0.1.0", "package version not recorded");
                Assert(progress.Values.Contains(0.2) && progress.Values.Last() == 1, "install progress missed asset building or completion: " + string.Join(", ", progress.Values));
                Assert(progress.Values.Zip(progress.Values.Skip(1), (previous, next) => next >= previous).All(rising => rising), "install progress went backwards");

                Assert(File.ReadAllText(Path.Combine(root, "NorthstarLauncher.exe")) == "standard-launcher", "standard launcher changed");
                Assert(File.ReadAllText(Path.Combine(root, "R2Northstar", "Northstar.dll")) == "standard-profile", "standard profile changed");
                Assert(File.ReadAllText(Path.Combine(root, "Titanfall2VRLauncher.exe")) == "vr-launcher-v1", "renamed launcher missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "Northstar.dll")) == "vr-profile", "VR profile missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "vr-plugin-v1", "VR plugin missing");
                Assert(File.ReadAllText(userFile) == "user-data", "untracked user file changed during adoption");

                Assert(!Directory.Exists(saveDirectory), "install created a VR save");
                Directory.CreateDirectory(Path.Combine(vrProfile, "savegames"));
                File.WriteAllText(Path.Combine(vrProfile, "savegames", "savegame.sav"), "vr-progress");
                Assert(File.ReadAllText(Path.Combine(defaultProfile, "savegames", "savegame.sav")) == "default-checkpoint", "default checkpoint changed");
                Assert(File.ReadAllText(Path.Combine(defaultProfile, "profile.cfg")) == "campaign-unlocks", "default profile changed");

                Assert(installer.IsInstalled, "complete VR package was not recognized");
                Assert(File.Exists(Path.Combine(root, "TF2VR", "tools", "xr_probe.exe")), "diagnostic probe missing");
                Assert(File.Exists(Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe")), "crash monitor missing");
                var replacedDirectX = Path.Combine(root, "bin", "x64_retail", "dxgi.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(replacedDirectX));
                File.WriteAllText(replacedDirectX, "reshade");
                var replacementError = "";
                try { installer.Play(); }
                catch (Exception ex) { replacementError = ex.Message; }
                Assert(replacementError.Contains(replacedDirectX) && !File.Exists(Path.Combine(profile, "monitor-started")), "launch ignored a replaced DirectX file: " + replacementError);
                var launcherLog = Path.Combine(profile, "launcher.txt");
                Assert(File.ReadAllText(launcherLog).Contains("launch_error=Exception"), "blocked launch error was not logged");
                File.Delete(replacedDirectX);
                var monitorPath = Path.Combine(profile, "tools", "crash_monitor.exe");
                File.Move(monitorPath, monitorPath + ".disabled");
                try { installer.Play(); }
                catch (System.ComponentModel.Win32Exception) { }
                Assert(File.ReadAllText(launcherLog).Contains("win32_error=2"), "Windows launch error was not logged");
                File.Move(monitorPath + ".disabled", monitorPath);
                installer.Play();
                var launchLog = File.ReadAllText(launcherLog);
                Assert(launchLog.Contains("monitor_started pid=") && launchLog.Contains("binary=TF2VR/Northstar.dll exists=True") && !launchLog.Contains("launch_error="), "launch diagnostics missing or stale");
                Assert(System.Threading.SpinWait.SpinUntil(() => File.Exists(Path.Combine(profile, "monitor-started")), 5000), "Play did not launch the crash monitor");
                var monitored = File.ReadAllLines(Path.Combine(profile, "monitor-launch.txt"));
                Assert(monitored[0] == profile && monitored[1] == Path.Combine(root, "Titanfall2VRLauncher.exe"), "Play bypassed crash capture");
                Assert(!File.Exists(Path.Combine(profile, "tools", "xr_views.json")), "Play still used probe dimensions");
                Assert(File.Exists(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod.json")), "cockpit assets missing");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod", "patched.txt")) == "patched", "patched game assets missing");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "tools", "asset_patcher.exe")), "asset patcher was installed");
                foreach (var license in new[] { "LICENSE.txt", "THIRD_PARTY_NOTICES.md", Path.Combine("licenses", "OpenXR.txt") })
                    Assert(File.Exists(Path.Combine(root, "TF2VR", license)), license + " missing");
                var launch = Titanfall2Installer.CreateLaunchInfo(root);
                Assert(launch.FileName == Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe"), "launch bypassed crash capture");
                Assert(launch.Arguments == "\"" + profile + "\" \"" + Path.Combine(root, "Titanfall2VRLauncher.exe")
                    + "\" -profile=TF2VR -windowed -w 1280 -h 720 +sound_without_focus 1 +mat_vsync_mode 0", "wrong monitored launch arguments");
                Assert(launch.EnvironmentVariables["TF2VR_OPENXR"] == "1", "OpenXR was not enabled");
                Assert(!launch.EnvironmentVariables.ContainsKey("TF2VR_DEV_SESSION"), "development session inherited");
                Assert(!launch.EnvironmentVariables.ContainsKey("XR_RUNTIME_JSON"), "runtime override inherited");
                Assert(!launch.UseShellExecute, "VR environment cannot reach launcher");
                Assert(launch.WorkingDirectory == root, "wrong working directory");

                var failFlag = Path.Combine(root, "fail-assets");
                File.WriteAllText(failFlag, "");
                var patchFailed = false;
                try { installer.Install(NorthstarPackage("vr-launcher-v2", false), ModPackage("vr-plugin-v2", "0.2.0"), true, new ProgressLog()); }
                catch (Exception ex) { patchFailed = ex.Message.Contains("game file differs"); }
                Assert(patchFailed, "asset patch failure did not stop the install");
                Assert(installer.Record.version == "0.1.0", "failed install recorded a version");
                Assert(File.ReadAllText(Path.Combine(root, "Titanfall2VRLauncher.exe")) == "vr-launcher-v1", "failed install changed the launcher");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "vr-plugin-v1", "failed install changed the plugin");
                File.Delete(failFlag);

                installer.Install(NorthstarPackage("vr-launcher-v2", false), ModPackage("vr-plugin-v2", "0.2.0"), true, new ProgressLog());
                Assert(installer.Record.version == "0.2.0", "updated package version not recorded");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "plugins", "ranim.dll")), "stale owned file survived update");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during update");
                Assert(installer.Record.beta, "beta channel was not recorded");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "update changed VR progress");

                ValidateCustomInstall(root, installer);

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
                    Assert(archive.GetEntry("Launcher/launcher.txt") != null, "launcher log missing from report");
                }

                ValidateCrashCaptureReport(root, profile);

                installer.Uninstall();
                Assert(!File.Exists(Path.Combine(root, "Titanfall2VRLauncher.exe")), "renamed launcher survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "Northstar.dll")), "VR profile survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "tools", "xr_probe.exe")), "probe survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "tools", "crash_monitor.exe")), "monitor survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod.json")), "cockpit survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "mods", "Titanfall2VR.Cockpit", "mod", "patched.txt")), "patched game assets survived uninstall");
                Assert(!File.Exists(Path.Combine(root, "TF2VR", "licenses", "OpenXR.txt")), "licenses survived uninstall");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during uninstall");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "uninstall changed VR progress");
                Assert(File.ReadAllText(Path.Combine(root, "NorthstarLauncher.exe")) == "standard-launcher", "standard launcher changed after uninstall");
                Assert(File.ReadAllText(Path.Combine(root, "R2Northstar", "Northstar.dll")) == "standard-profile", "standard profile changed after uninstall");

                installer.Install(NorthstarPackage("vr-launcher-v3", false), ModPackage("vr-plugin-v3", "0.3.0"), false, new ProgressLog());
                Assert(installer.IsInstalled, "VR package was not recognized after reinstall");
                Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "vr-plugin-v3", "reinstalled VR plugin missing");
                Assert(File.ReadAllText(userFile) == "user-data", "user file changed during reinstall");

                Assert(File.ReadAllText(Path.Combine(defaultProfile, "profile.cfg")) == "campaign-unlocks", "reinstall changed the default profile");
                Assert(File.ReadAllText(fnfSave) == "other-mod-progress", "FNF campaign progress changed");
                Assert(File.ReadAllText(Path.Combine(vrProfile, "savegames", "savegame.sav")) == "vr-progress", "reinstall changed campaign progress");
                Console.WriteLine("validated Northstar cache, TF2VR install, install progress, asset patching and its failure, save isolation, updates, crash reports, uninstall, and reinstall");
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

        // the themed bar fills in proportion to its value
        static void ValidateProgressBar()
        {
            var bar = new ProgressBar { Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("Progress"), Width = 200 };
            foreach (var value in new[] { 0.25, 0.5, 1 })
            {
                bar.Value = value;
                bar.Measure(new System.Windows.Size(200, 4));
                bar.Arrange(new System.Windows.Rect(0, 0, 200, 4));
                bar.UpdateLayout();
                var fill = ((System.Windows.FrameworkElement)bar.Template.FindName("PART_Indicator", bar)).ActualWidth;
                Assert(Math.Abs(fill - 200 * value) < 0.5, $"progress bar filled {fill} of 200 at {value}");
            }
        }

        // the saves modal reports the VR save and opens its folder, with no base game import
        // the URL refuses connections, so only cache hits succeed
        static void ValidateNorthstarCache(string root)
        {
            const string url = "https://127.0.0.1:1/Northstar.zip";
            var cache = Titanfall2Installer.NorthstarCache(Path.Combine(root, "Local"));
            var package = Encoding.UTF8.GetBytes("northstar-package");
            var sha256 = RepoClient.Sha256(package);
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(Path.Combine(cache, sha256 + ".zip"), package);
            var progress = new ProgressLog();
            Assert(RepoClient.CachedDownload(url, sha256, cache, progress).GetAwaiter().GetResult().SequenceEqual(package), "cached Northstar was not reused");
            Assert(progress.Values.SequenceEqual(new[] { 1.0 }), "cached Northstar did not complete its progress");

            File.WriteAllText(Path.Combine(cache, sha256 + ".zip"), "corrupt");
            var downloaded = false;
            try { RepoClient.CachedDownload(url, sha256, cache).GetAwaiter().GetResult(); }
            catch (System.Net.Http.HttpRequestException) { downloaded = true; }
            Assert(downloaded, "corrupt cached Northstar was reused");
            Directory.Delete(cache, true);
        }

        static void ValidateChannelMenu()
        {
            var window = new MainWindow();
            var create = typeof(MainWindow).GetMethod("CreateChannelMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            var selected = "";
            Func<bool, Task> select = beta => { selected = beta ? "beta" : "stable"; return Task.CompletedTask; };
            Func<Task> zip = () => { selected = "zip"; return Task.CompletedTask; };
            var stable = new ManifestMod { version = "1.0.0" };
            var menu = (ContextMenu)create.Invoke(window, new object[] { stable, false, select, zip });
            Assert(menu.Items.Count == 2 && menu.Items.Cast<object>().All(item => item is MenuItem), "stable menu contains a separator or is missing ZIP action");
            Assert(((MenuItem)menu.Items[0]).IsChecked, "stable selection missing");
            var zipHeader = (StackPanel)((MenuItem)menu.Items[1]).Header;
            Assert(((TextBlock)zipHeader.Children[0]).Text == "Install from ZIP…", "ZIP action label changed");
            ((MenuItem)menu.Items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            Assert(selected == "zip", "ZIP action did not run");
            stable.beta = new ModRelease { version = "1.1.0" };
            menu = (ContextMenu)create.Invoke(window, new object[] { stable, null, select, zip });
            Assert(menu.Items.Count == 3, "beta menu missing a release or ZIP action");
            var betaHeader = (StackPanel)((MenuItem)menu.Items[1]).Header;
            zipHeader = (StackPanel)((MenuItem)menu.Items[2]).Header;
            for (var i = 0; i < 2; i++)
            {
                var betaText = (TextBlock)betaHeader.Children[i];
                var zipText = (TextBlock)zipHeader.Children[i];
                Assert(betaText.FontSize == zipText.FontSize && betaText.FontWeight == zipText.FontWeight
                    && betaText.Foreground == zipText.Foreground && betaText.Margin == zipText.Margin, "ZIP styling differs from beta");
            }
            Assert(!((MenuItem)menu.Items[0]).IsChecked && !((MenuItem)menu.Items[1]).IsChecked, "custom install selected a published release");
            ((MenuItem)menu.Items[0]).RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            Assert(selected == "stable", "custom install could not select stable");
            ((MenuItem)menu.Items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            Assert(selected == "beta", "custom install could not select beta");
            menu = (ContextMenu)create.Invoke(window, new object[] { null, false, select, zip });
            Assert(menu.Items.Count == 1, "offline menu did not retain ZIP installation");
            menu = (ContextMenu)create.Invoke(window, new object[] { stable, true, select, null });
            Assert(menu.Items.Count == 2 && ((MenuItem)menu.Items[1]).IsChecked, "Big Walk channel selection changed");
            window.Close();
            Console.WriteLine("validated version menus and ZIP action");
        }

        static void ValidateCustomInstall(string root, Titanfall2Installer installer)
        {
            var package = Path.Combine(root, "custom.zip");
            File.WriteAllBytes(package, ModPackage("custom-plugin", "0.2.0"));
            installer.Install(NorthstarPackage("custom-launcher", false), File.ReadAllBytes(package), false, new ProgressLog(), custom: true);
            Assert(installer.Record.custom && !installer.Record.beta, "custom install was not recorded");
            Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "custom-plugin", "ZIP plugin was not installed");
            var settings = new AppSettings { Titanfall2Path = root };
            var reopened = new Titanfall2Installer(settings);
            var release = new ManifestMod { version = "9.0.0" };
            Assert(!reopened.CanUpdate(release, false) && !reopened.CanUpdate(release, true), "published release would replace custom build on launch");
            var window = new MainWindow();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_settings", flags).SetValue(window, settings);
            typeof(MainWindow).GetField("_titanfall", flags).SetValue(window, reopened);
            typeof(MainWindow).GetField("_titanfallAvailable", flags).SetValue(window, release);
            typeof(MainWindow).GetField("_titanfallRelease", flags).SetValue(window, release);
            typeof(MainWindow).GetMethod("RefreshTitanfallState", flags).Invoke(window, null);
            Assert(((Button)window.FindName("TitanfallChannelsButton")).Visibility == System.Windows.Visibility.Visible, "dropdown hidden without beta");
            Assert(((TextBlock)window.FindName("TitanfallVersionText")).Text.Contains("custom"), "custom version label missing");
            var installedContent = (StackPanel)((Border)window.FindName("TitanfallInstalledChip")).Child;
            Assert(installedContent.Children.Cast<TextBlock>().All(text => text.VerticalAlignment == System.Windows.VerticalAlignment.Center), "installed checkmark and label are not centered");
            Assert((string)((Button)window.FindName("LaunchButton")).Content == "Launch in VR", "custom launch offered a public update");
            window.Close();
            var rejected = false;
            try { reopened.Install(NorthstarPackage("bad-launcher", false), new byte[] { 1, 2, 3 }, false, new ProgressLog(), custom: true); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected && reopened.Record.custom, "invalid ZIP changed custom install state");
            Assert(File.ReadAllText(Path.Combine(root, "TF2VR", "plugins", "Titanfall2VR.dll")) == "custom-plugin", "invalid ZIP replaced custom plugin");
            reopened.Install(NorthstarPackage("vr-launcher-v2", false), ModPackage("vr-plugin-v2", "0.2.0"), false, new ProgressLog());
            Assert(!reopened.Record.custom && reopened.CanUpdate(release, false), "selecting stable did not restore published updates");
            Console.WriteLine("validated custom ZIP install, persistence, launch pinning, invalid ZIP rejection, and return to stable");
        }

        static void ValidateSaveModal(string root)
        {
            var app = new App();
            app.InitializeComponent();
            ValidateProgressBar();
            var window = new MainWindow();
            var documents = Path.Combine(root, "save-dialog");
            var show = typeof(MainWindow).GetMethod("ShowTitanfallSaves", BindingFlags.Instance | BindingFlags.NonPublic);
            var overlay = (Border)window.FindName("TitanfallSavesOverlay");
            var status = (TextBlock)window.FindName("TitanfallVrSaveStatus");
            var profile = Path.Combine(Titanfall2Installer.SaveDirectory(documents), "profile");
            Assert((string)((Button)window.FindName("TitanfallSavesButton")).Content == "Campaign saves", "saves modal button missing");
            Assert((string)((Button)window.FindName("TitanfallSaveDirectoryButton")).Content == "Open folder", "save folder label changed");
            Assert(window.FindName("TitanfallCopySaveButton") == null, "base game save import remains");
            show.Invoke(window, new object[] { documents });
            Assert(overlay.Visibility == System.Windows.Visibility.Visible, "saves modal did not open");
            Assert(!((Grid)window.FindName("InstallerView")).IsEnabled, "saves modal left launch controls enabled");
            Assert(status.Text == "No save", "missing save status incorrect");
            Directory.CreateDirectory(Path.Combine(profile, "savegames"));
            File.WriteAllText(Path.Combine(profile, "savegames", "savegame.sav"), "vr-checkpoint");
            show.Invoke(window, new object[] { documents });
            Assert(status.Text == "Incomplete save", "incomplete save status incorrect");
            File.WriteAllText(Path.Combine(profile, "profile.cfg"), "vr-unlocks");
            show.Invoke(window, new object[] { documents });
            Assert(status.Text == "Save found", "complete save status incorrect");
            ((Button)window.FindName("TitanfallSavesCloseButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            Assert(overlay.Visibility == System.Windows.Visibility.Collapsed, "saves modal did not close");
            Assert(((Grid)window.FindName("InstallerView")).IsEnabled, "closing saves modal left launch controls disabled");
            window.Close();
        }

        static void ValidateCrashCaptureReport(string root, string profile)
        {
            var session = Path.Combine(profile, "crashes", "20260920T000000000Z-11");
            var incident = Path.Combine(session, "exception-11");
            Directory.CreateDirectory(incident);
            File.WriteAllText(Path.Combine(session, "incident.txt"), "exception");
            File.WriteAllText(Path.Combine(session, "monitor.txt"), "process_exit code=3221225477");
            File.WriteAllText(Path.Combine(session, "session.json"), "{\"modSha256\":\"captured-plugin\"}");
            File.WriteAllText(Path.Combine(session, "diagnostics.txt"), "commit_limit_bytes=100");
            File.WriteAllText(Path.Combine(session, "memory.txt"), "process_private_bytes=10");
            File.WriteAllText(Path.Combine(session, "launcher.txt"), "monitor_started pid=11");
            var firstChance = Path.Combine(session, "first-chance-11-1");
            Directory.CreateDirectory(firstChance);
            File.WriteAllText(Path.Combine(firstChance, "exceptions.txt"), "exception=0xc0000005 parameter1=0x48");
            File.WriteAllText(Path.Combine(firstChance, "stacks.txt"), "ntdll.dll+0x11");
            File.WriteAllText(Path.Combine(firstChance, "process.dmp"), "handled-memory");
            File.WriteAllText(Path.Combine(session, "stderr.txt"), "Titanfall2VR: positional head tracking is required\n");
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
                Assert(archive.GetEntry("Capture/stderr.txt") != null, "game stderr missing");
                Assert(archive.GetEntry("Capture/diagnostics.txt") != null, "system diagnostics missing");
                Assert(archive.GetEntry("Capture/memory.txt") != null, "memory samples missing");
                Assert(archive.GetEntry("Capture/launcher.txt") != null, "captured launcher log missing");
                Assert(archive.GetEntry("Capture/first-chance-11-1/exceptions.txt") != null, "first exception missing");
                Assert(archive.GetEntry("Capture/first-chance-11-1/stacks.txt") != null, "first exception stacks missing");
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
                    Add(archive, "xr_probe.exe", "diagnostic-probe");
                    Add(archive, "crash_monitor.exe", File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
                    Add(archive, "launch.json", JsonUtil.Serialize(new TitanfallLaunchSettings {
                        arguments = new[] { "-profile={profile}", "-windowed", "-w", "{width}", "-h", "{height}", "+sound_without_focus", "{sound}" },
                        vrArguments = new[] { "+mat_vsync_mode", "0" }
                    }));
                    Add(archive, "mods/Titanfall2VR.Cockpit/mod.json", "{}");
                    Add(archive, "LICENSE.txt", "license");
                    Add(archive, "THIRD_PARTY_NOTICES.md", "notices");
                    Add(archive, "licenses/OpenXR.txt", "openxr");
                    Add(archive, "asset_patcher.exe", File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
                    Add(archive, "patches/manifest.json", "{}");
                }
                return stream.ToArray();
            }
        }

        static void Add(ZipArchive archive, string path, string contents) => Add(archive, path, Encoding.UTF8.GetBytes(contents));

        static void Add(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path);
            using (var output = entry.Open())
                output.Write(bytes, 0, bytes.Length);
        }

        static void Icacls(string path, string arguments)
        {
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls.exe", $"\"{path}\" {arguments}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            {
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert(process.ExitCode == 0, "icacls failed: " + arguments);
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
