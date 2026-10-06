using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BigWalkVRInstaller.Installers;
using BigWalkVRInstaller.Services;

namespace BigWalkVRInstaller
{
    public class LogEntry
    {
        public string Time { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    public partial class MainWindow : Window
    {
        enum SelectedGame { None, BigWalk, Titanfall2 }

        const string RepoUrl = "https://github.com/CircuitLord/CircuitLordVRModInstaller";
        const string DiscordUrl = "https://discord.gg/MTKwud2cCP";
        const string SupportUrl = "https://ko-fi.com/circuitlord";

        readonly ObservableCollection<ModEntry> _mods = new ObservableCollection<ModEntry>();
        readonly ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();
        AppSettings _settings;
        BigWalkInstaller _bigWalk;
        Titanfall2Installer _titanfall;
        SelectedGame _selectedGame;
        ReleaseInfo _selfUpdate;
        ReleaseInfo _bepInExSource;
        ManifestMod _titanfallAvailable;
        ManifestMod _titanfallRelease;
        ModChannel _titanfallChannel;
        bool _bepInExBusy;
        bool _titanfallBusy;
        GameStartupWatcher _watcher;

        public MainWindow()
        {
            InitializeComponent();
            ModsList.ItemsSource = _mods;
            LogsList.ItemsSource = _logs;
            VersionText.Text = "v" + SelfUpdater.CurrentVersion;
        }

        bool HasGame => _bigWalk?.HasGame == true;

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = AppSettings.Load();
            _bigWalk = new BigWalkInstaller(_settings);
            _titanfall = new Titanfall2Installer(_settings);

            if (AppSettings.LoadError != null) Status($"Settings load failed, using defaults: {AppSettings.LoadError}", true);

            if (!HasGame) _bigWalk.DetectGamePath();
            if (!_titanfall.HasGame) _titanfall.DetectGamePath();
            UpdateGameCards();
            Status("Choose a game to continue");
        }

        void UpdateGameCards()
        {
            SetGameCard(_bigWalk, BigWalkCardStatus, BigWalkInstalledChip, BigWalkArt);
            SetGameCard(_titanfall, TitanfallCardStatus, TitanfallCardInstalledChip, TitanfallArt);
        }

        static void SetGameCard(IVrModInstaller installer, TextBlock status, ContentControl installedChip, Border art)
        {
            var installedVersion = installer.InstalledVersion;
            var installed = installedVersion != null;
            status.Text = !installer.HasGame ? "Game not found" : installed ? "Ready to play" : "Ready to install";
            installedChip.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            installedChip.Content = installed ? "v" + installedVersion : null;
            art.Opacity = installer.HasGame ? 1 : 0.45;
        }

        async void SelectBigWalk_Click(object sender, RoutedEventArgs e) => await OpenGame(SelectedGame.BigWalk);
        async void SelectTitanfall_Click(object sender, RoutedEventArgs e) => await OpenGame(SelectedGame.Titanfall2);

        async Task OpenGame(SelectedGame game)
        {
            _selectedGame = game;
            GameSelectionView.Visibility = Visibility.Collapsed;
            InstallerView.Visibility = Visibility.Visible;
            NavInstall.IsChecked = true;
            LogsView.Visibility = Visibility.Collapsed;
            InstallView.Visibility = Visibility.Visible;

            var bigWalk = game == SelectedGame.BigWalk;
            BigWalkContent.Visibility = bigWalk ? Visibility.Visible : Visibility.Collapsed;
            TitanfallContent.Visibility = bigWalk ? Visibility.Collapsed : Visibility.Visible;
            LaunchNonVrButton.Visibility = bigWalk ? Visibility.Visible : Visibility.Collapsed;
            LaunchButton.Content = "Launch in VR";
            SelectedGameName.Text = bigWalk ? "Big Walk VR" : "Titanfall 2 VR";
            SelectedGameImage.Source = new BitmapImage(new Uri(bigWalk ? "Assets/big-walk.jpg" : "Assets/titanfall-2.jpg", UriKind.Relative));

            Status($"Loading {SelectedGameName.Text}...");
            await Refresh();

            if (bigWalk && HasGame && BepInExInstaller.IsMelonLoaderInstalled(_bigWalk.GamePath))
                Status("Remove MelonLoader in step 2 to continue.");
            else if (bigWalk)
            {
                var vr = _mods.FirstOrDefault();
                if (vr?.CanUpdate == true) Status($"An update to v{vr.Remote.version} is available");
                else if (vr?.IsInstalled == true) Status("Everything is up to date");
            }
        }

        void BackToGames_Click(object sender, RoutedEventArgs e) => ShowGameSelection();

        void ShowGameSelection()
        {
            StopWatcher();
            LaunchOverlay.Visibility = Visibility.Collapsed;
            _selectedGame = SelectedGame.None;
            InstallerView.Visibility = Visibility.Collapsed;
            GameSelectionView.Visibility = Visibility.Visible;
            UpdateGameCards();
            Status("Choose a game to continue");
        }

        async Task Refresh()
        {
            await FetchManifest();
            if (_selectedGame == SelectedGame.BigWalk) RefreshBigWalkState();
            else if (_selectedGame == SelectedGame.Titanfall2) RefreshTitanfallState();
            UpdateGameCards();
        }

        async Task FetchManifest()
        {
            var previous = _mods.ToDictionary(mod => mod.Id, mod => mod);
            _mods.Clear();
            _selfUpdate = null;
            _bepInExSource = null;
            _titanfallAvailable = null;
            _titanfallChannel = null;
            _titanfallRelease = null;

            try
            {
                var manifest = await RepoClient.FetchManifest(AppSettings.ManifestUrl);
                // channels removed from the manifest fall back to stable
                var settingsChanged = false;
                if (_settings.BigWalkChannel != null && manifest.mods.All(mod => mod.FindChannel(_settings.BigWalkChannel) == null))
                {
                    _settings.BigWalkChannel = null;
                    settingsChanged = true;
                }
                if (_settings.Titanfall2Channel != null && manifest.titanfall2vr?.FindChannel(_settings.Titanfall2Channel) == null)
                {
                    _settings.Titanfall2Channel = null;
                    settingsChanged = true;
                }
                if (settingsChanged) _settings.Save();

                foreach (var available in manifest.mods)
                {
                    var channel = available.FindChannel(_settings.BigWalkChannel);
                    var entry = previous.TryGetValue(available.id, out var live) && live.Busy
                        ? live
                        : new ModEntry { Available = available, Remote = available.SelectRelease(channel), Channel = channel };
                    _mods.Add(entry);
                }
                _bepInExSource = manifest.bepinex;
                _titanfallAvailable = manifest.titanfall2vr;
                _titanfallChannel = _titanfallAvailable?.FindChannel(_settings.Titanfall2Channel);
                _titanfallRelease = _titanfallAvailable?.SelectRelease(_titanfallChannel);
                if (SelfUpdater.IsUpdateAvailable(manifest.installer)) _selfUpdate = manifest.installer;
            }
            catch (Exception ex)
            {
                Status($"Couldn't load the download list from {AppSettings.ManifestUrl}:\n{RepoClient.DescribeError(ex)}", true);
            }

            OfflineNotice.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelfUpdateBanner.Visibility = _selfUpdate != null ? Visibility.Visible : Visibility.Collapsed;
            if (_selfUpdate != null) SelfUpdateTitle.Text = $"Installer update available: v{_selfUpdate.version}";
        }

        void RefreshBigWalkState()
        {
            foreach (var mod in _mods)
            {
                var record = HasGame ? PackageInstaller.ReadRecord(_bigWalk.GamePath, mod.Id) : null;
                mod.InstalledChannel = record?.channel;
                mod.InstalledVersion = record?.version;
            }
            UpdateBigWalkSetupState();
        }

        void UpdateBigWalkSetupState()
        {
            var bepinex = HasGame && BepInExInstaller.IsInstalled(_bigWalk.GamePath);
            var melonLoader = HasGame && BepInExInstaller.IsMelonLoaderInstalled(_bigWalk.GamePath);
            var unknownBootstrap = HasGame && BepInExInstaller.HasUnknownBootstrap(_bigWalk.GamePath);
            var loaderReady = bepinex && !melonLoader && !unknownBootstrap;

            GamePathText.Text = HasGame ? _bigWalk.GamePath : "Not found. Press Change and pick your Big Walk folder.";
            OpenFolderButton.IsEnabled = HasGame;
            SetStep(GameBadge, GameBadgeText, HasGame);

            var bepinexVersion = bepinex ? BepInExInstaller.InstalledVersion(_bigWalk.GamePath) : null;
            BepInExText.Text = unknownBootstrap
                ? "version.dll is active but is not part of a complete MelonLoader install. Restore vanilla or remove it manually."
                : melonLoader
                    ? "MelonLoader must be removed before Big Walk can launch."
                    : bepinex
                        ? $"Ready{(bepinexVersion != null ? $"  •  v{bepinexVersion}" : "")}"
                        : "The mod loader Big Walk VR depends on.";
            BepInExButton.Content = melonLoader ? "Remove MelonLoader" : bepinex ? "Reinstall" : "Install";
            BepInExButton.IsEnabled = HasGame && _bepInExSource != null && !_bepInExBusy && !unknownBootstrap;
            SetStep(BepInExBadge, BepInExBadgeText, loaderReady);

            var canLaunch = HasGame && loaderReady && _mods.Any(mod => mod.IsInstalled);
            LaunchNonVrButton.IsEnabled = canLaunch;
            LaunchButton.IsEnabled = canLaunch;
            LaunchNonVrButton.ToolTip = canLaunch ? "Play normally while seeing VR players' tracked movement" : "Finish steps 1-3 first";
            LaunchButton.ToolTip = canLaunch ? "Launch Big Walk in VR, start SteamVR first" : "Finish steps 1-3 first";
            RestoreVanillaButton.IsEnabled = HasGame;
            CrashReportButton.IsEnabled = HasGame;
            SetStep(ModsBadge, ModsBadgeText, loaderReady && _mods.Any(mod => mod.IsCurrent));

            var ready = HasGame && loaderReady;
            ModsList.IsEnabled = ready;
            ModsList.Opacity = ready ? 1 : 0.4;
            SelectedGameStatus.Text = canLaunch ? "Ready to play" : HasGame ? "Setup required" : "Game not found";
        }

        void RefreshTitanfallState()
        {
            var hasGame = _titanfall.HasGame;
            var installed = _titanfall.IsInstalled;
            var update = installed && _titanfallRelease != null && _titanfall.CanUpdate(_titanfallRelease, _titanfallChannel?.id);

            TitanfallGamePathText.Text = hasGame ? _titanfall.GamePath : "Not found. Press Change and pick your Titanfall 2 folder.";
            TitanfallOpenFolderButton.IsEnabled = hasGame;
            SetStep(TitanfallGameBadge, TitanfallGameBadgeText, hasGame);
            SetStep(TitanfallInstallBadge, TitanfallInstallBadgeText, installed);
            var eaApp = File.Exists(Titanfall2Installer.EaAppPath());
            var signedIn = eaApp && _settings.Titanfall2EaSignedIn;
            SetStep(TitanfallEaBadge, TitanfallEaBadgeText, signedIn);
            TitanfallEaText.Text = eaApp
                ? "Titanfall 2 requires you to be signed into the EA app to play."
                : "EA app not found. Launch Titanfall 2 once from Steam to install it and sign in, then press Refresh.";
            TitanfallEaButton.Content = eaApp ? "Open EA app" : "Launch from Steam";
            TitanfallEaButton.IsEnabled = eaApp || hasGame;

            TitanfallVersionText.Text = installed
                ? $"Installed v{_titanfall.Record.version}{(_titanfall.Record.custom ? " custom" : _titanfall.Record.channel != null ? " " + _titanfall.Record.channel : "")}  •  Northstar v{_titanfall.Record.northstarVersion}"
                : _titanfallRelease != null
                    ? $"v{_titanfallRelease.version}{(_titanfallChannel != null ? " " + _titanfallChannel.id : "")}  •  Northstar v{Titanfall2Installer.NorthstarVersion}"
                    : "Download list unavailable";
            TitanfallUninstallButton.Visibility = installed && !_titanfallBusy ? Visibility.Visible : Visibility.Collapsed;
            TitanfallAction.Visibility = _titanfallBusy ? Visibility.Collapsed : Visibility.Visible;
            TitanfallAction.State = !installed ? InstallState.Install : update ? InstallState.Update : InstallState.Installed;
            TitanfallChannelTag.Visibility = _titanfallChannel != null && !(installed && _titanfall.Record.custom) ? Visibility.Visible : Visibility.Collapsed;
            TitanfallChannelTagText.Text = _titanfallChannel?.id.ToUpperInvariant();
            var channelMismatch = installed && _titanfallRelease != null && _titanfall.Record.channel != _titanfallChannel?.id;
            TitanfallAction.Label = channelMismatch
                ? $"Switch to {_titanfallChannel?.id ?? "stable"} v{_titanfallRelease.version}"
                : update
                    ? $"Update to v{_titanfallRelease.version}"
                    : $"Install{(_titanfallRelease == null ? "" : $" v{_titanfallRelease.version}")}";
            TitanfallAction.ActionEnabled = hasGame && _titanfallRelease != null;
            BackButton.IsEnabled = !_titanfallBusy;
            RefreshButton.IsEnabled = !_titanfallBusy;
            LaunchButton.IsEnabled = installed && !_titanfallBusy;
            LaunchButton.Content = "Launch in VR";
            TitanfallCrashReportButton.IsEnabled = hasGame;
            TitanfallSavesButton.IsEnabled = !_titanfallBusy;
            LaunchButton.ToolTip = installed ? "Launch Titanfall 2 VR" : "Install Titanfall 2 VR first";
            SelectedGameStatus.Text = installed ? update ? "Update available" : "Ready to play" : hasGame ? "Setup required" : "Game not found";
        }

        void SetStep(Border badge, TextBlock label, bool done)
        {
            badge.Background = Brush(done ? "GreenFill" : "Hover");
            badge.BorderBrush = Brush(done ? "GreenFillBorder" : "Stroke");
            label.Text = done ? "✓" : (string)label.Tag;
            label.Foreground = Brush(done ? "Green" : "TextDim");
        }

        Brush Brush(string key) => (Brush)FindResource(key);

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            var mod = (ModEntry)((FrameworkElement)sender).DataContext;
            await InstallMod(mod);
        }

        async Task InstallMod(ModEntry mod)
        {
            if (!Ready()) return;

            mod.Busy = true;
            mod.Progress = 0;
            mod.BusyText = "Downloading...";
            try
            {
                var progress = new Progress<double>(value =>
                {
                    mod.Progress = value;
                    mod.BusyText = $"Downloading  {value * 100:0}%";
                });
                var bytes = await RepoClient.Download(mod.Remote.url, mod.Remote.sha256, progress);

                mod.BusyText = "Installing...";
                mod.Progress = 1;
                await Task.Run(() => _bigWalk.InstallMod(mod.Remote, bytes, mod.Channel?.id));
                Status($"{mod.Name} v{mod.Remote.version} installed");
            }
            catch (Exception ex)
            {
                Status($"{mod.Name} install failed: {ex.Message}", true);
            }
            finally
            {
                mod.Busy = false;
                RefreshBigWalkState();
                UpdateGameCards();
            }
        }

        async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var mod = (ModEntry)((FrameworkElement)sender).DataContext;
            if (!Ready()) return;
            if (!await Confirm("Uninstall " + mod.Name, "All mod files and configuration will be removed.", "Uninstall")) return;

            try
            {
                _bigWalk.UninstallMod(mod.Id);
                Status($"{mod.Name} removed");
            }
            catch (Exception ex)
            {
                Status($"{mod.Name} uninstall failed: {ex.Message}", true);
            }
            await Refresh();
        }

        bool Ready()
        {
            if (_selectedGame == SelectedGame.Titanfall2)
            {
                if (!_titanfall.HasGame)
                {
                    Status("Pick your Titanfall 2 folder first", true);
                    return false;
                }
                if (Titanfall2Installer.IsRunning())
                {
                    Status("Close Titanfall 2 before changing the installation", true);
                    return false;
                }
                return true;
            }

            if (!HasGame)
            {
                Status("Pick your Big Walk folder first", true);
                return false;
            }
            if (GameLauncher.IsRunning())
            {
                Status("Close Big Walk before changing mods", true);
                return false;
            }
            return true;
        }

        async void RestoreVanilla_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready()) return;
            if (!await Confirm("Restore vanilla Big Walk", "Permanently removes every installed mod, BepInEx, and MelonLoader.", "Restore vanilla")) return;

            try
            {
                _bigWalk.RestoreVanilla();
                Status("Big Walk is back to vanilla");
            }
            catch (Exception ex)
            {
                Status($"Restore failed: {ex.Message}", true);
            }
            await Refresh();
        }

        async void InstallBepInEx_Click(object sender, RoutedEventArgs e)
        {
            if (await InstallBepInEx()) await Refresh();
        }

        async Task<bool> InstallBepInEx()
        {
            if (!Ready()) return false;
            if (BepInExInstaller.IsMelonLoaderInstalled(_bigWalk.GamePath) && !await Confirm(
                "Remove MelonLoader",
                "Installing BepInEx will permanently remove MelonLoader and everything in its Mods, Plugins, and UserLibs folders. BigWalkVR settings will be migrated. No backup will be created.",
                "Remove and install")) return false;

            var installed = false;
            _bepInExBusy = true;
            BepInExButton.IsEnabled = false;
            BepInExProgressPanel.Visibility = Visibility.Visible;
            BepInExProgress.Value = 0;
            BepInExProgressText.Text = "Downloading BepInEx...";
            Status("Downloading BepInEx...");
            try
            {
                var progress = new Progress<double>(value =>
                {
                    BepInExProgress.Value = value;
                    BepInExProgressText.Text = $"Downloading BepInEx  {value * 100:0}%";
                });
                var bytes = await RepoClient.Download(_bepInExSource.url, _bepInExSource.sha256, progress);

                BepInExProgressText.Text = "Installing BepInEx...";
                BepInExProgress.Value = 1;
                var migration = await Task.Run(() => _bigWalk.InstallLoader(bytes));
                Status(migration.Migrated ? migration.Details : $"BepInEx v{_bepInExSource.version} installed");
                installed = true;
            }
            catch (Exception ex)
            {
                Status($"BepInEx install failed: {ex.Message}", true);
            }
            finally
            {
                _bepInExBusy = false;
                BepInExProgressPanel.Visibility = Visibility.Collapsed;
                UpdateBigWalkSetupState();
            }
            return installed;
        }

        async void TitanfallInstall_Click(object sender, RoutedEventArgs e) => await InstallTitanfallPackage();

        async Task InstallTitanfallPackage(string packagePath = null)
        {
            if (!Ready() || (packagePath == null && _titanfallRelease == null)) return;
            SetTitanfallBusy(true);
            try
            {
                await InstallTitanfall(packagePath);
            }
            catch (Exception ex)
            {
                Status($"Titanfall 2 VR install failed: {ex.Message}", true);
            }
            finally
            {
                SetTitanfallBusy(false);
            }
        }

        void SetTitanfallBusy(bool busy)
        {
            _titanfallBusy = busy;
            RefreshTitanfallState();
            UpdateGameCards();
        }

        // downloads fill the first half of the bar by size, installing the rest
        const double TitanfallDownloadShare = 0.5;

        async Task InstallTitanfall(string packagePath = null)
        {
            TitanfallProgressPanel.Visibility = Visibility.Visible;
            TitanfallProgress.Value = 0;
            try
            {
                await EnsureTitanfallFolderAccess();
                var northstarShare = packagePath != null ? TitanfallDownloadShare
                    : TitanfallDownloadShare * Titanfall2Installer.NorthstarSize / (Titanfall2Installer.NorthstarSize + _titanfallRelease.size);
                Status("Downloading Northstar...");
                var northstarProgress = new Progress<double>(value =>
                {
                    TitanfallProgress.Value = northstarShare * value;
                    TitanfallProgressText.Text = $"Downloading Northstar  {value * 100:0}%";
                });
                var northstar = await RepoClient.CachedDownload(Titanfall2Installer.NorthstarUrl, Titanfall2Installer.NorthstarSha256,
                    Titanfall2Installer.NorthstarCache(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), northstarProgress);

                byte[] mod;
                if (packagePath != null)
                {
                    Status("Reading ZIP...");
                    mod = await Task.Run(() => File.ReadAllBytes(packagePath));
                }
                else
                {
                    Status("Downloading Titanfall 2 VR...");
                    var modProgress = new Progress<double>(value =>
                    {
                        TitanfallProgress.Value = northstarShare + (TitanfallDownloadShare - northstarShare) * value;
                        TitanfallProgressText.Text = $"Downloading Titanfall 2 VR  {value * 100:0}%";
                    });
                    mod = await RepoClient.Download(_titanfallRelease.url, _titanfallRelease.sha256, modProgress);
                }

                Status("Installing Titanfall 2 VR...");
                TitanfallProgressText.Text = "Installing  0%";
                var installProgress = new Progress<double>(value =>
                {
                    TitanfallProgress.Value = TitanfallDownloadShare + (1 - TitanfallDownloadShare) * value;
                    TitanfallProgressText.Text = $"Installing  {value * 100:0}%";
                });
                await Task.Run(() => _titanfall.Install(northstar, mod, packagePath == null ? _titanfallChannel?.id : null, installProgress, custom: packagePath != null));
                Status($"Titanfall 2 VR v{_titanfall.Record.version} installed");
            }
            finally
            {
                TitanfallProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        // asks for admin once when the game is in a protected folder
        async Task EnsureTitanfallFolderAccess()
        {
            if (_titanfall.CanWriteGameFolder()) return;
            if (!await Confirm("Allow access to the game folder",
                $"Titanfall 2 VR needs to write to {_titanfall.GamePath}. Windows will ask for permission once.",
                "Continue", danger: false))
                throw new Exception("the game folder needs write access.");
            Status("Approve the Windows prompt to allow access to the game folder.");
            await Task.Run(() => _titanfall.GrantGameFolderAccess());
        }

        async void TitanfallUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready()) return;
            if (!await Confirm("Uninstall Titanfall 2 VR", "Removes the TF2VR profile, renamed launcher, and files owned by this installer.", "Uninstall")) return;
            try
            {
                await EnsureTitanfallFolderAccess();
                _titanfall.Uninstall();
                Status("Titanfall 2 VR removed");
            }
            catch (Exception ex)
            {
                Status($"Titanfall 2 VR uninstall failed: {ex.Message}", true);
            }
            RefreshTitanfallState();
            UpdateGameCards();
        }

        async void TitanfallEa_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(Titanfall2Installer.EaAppPath()))
            {
                Open(() =>
                {
                    Titanfall2Installer.LaunchFromSteam();
                    Status("Launching Titanfall 2 from Steam. Sign in to EA when asked, close the game, then press Refresh.");
                });
                return;
            }
            try
            {
                Titanfall2Installer.OpenEaApp();
            }
            catch (Exception ex)
            {
                Status($"Couldn't open the EA app: {ex.Message}", true);
                return;
            }
            if (!await Confirm("Sign in to EA app", "Sign in and stay signed in so Titanfall 2 VR can start without asking in VR.", "I'm signed in", danger: false)) return;
            _settings.Titanfall2EaSignedIn = true;
            _settings.Save();
            Status("EA app sign in confirmed");
            RefreshTitanfallState();
        }

        void LaunchNonVr_Click(object sender, RoutedEventArgs e) => Launch(
            () => _bigWalk.PlayNonVr(), "Launching Big Walk in Non-VR mode", "");

        async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == SelectedGame.Titanfall2)
            {
                SetTitanfallBusy(true);
                try
                {
                    if (!await ConfirmLaunchPermissions(LaunchPermissions.GetElevatedApps())) return;
                    await EnsureTitanfallFolderAccess();
                    Status("Checking headset resolution...");
                    await Task.Run(() => _titanfall.Play());
                    Status("Launching Titanfall 2 VR");
                }
                catch (Exception ex)
                {
                    Status($"Launch failed: {ex.Message}", true);
                }
                finally
                {
                    SetTitanfallBusy(false);
                }
                return;
            }

            Launch(() => _bigWalk.Play(), "Launching Big Walk in VR", "Make sure SteamVR is running and your headset is connected.");
        }

        Task<bool> ConfirmLaunchPermissions(string[] elevatedApps)
        {
            if (elevatedApps.Length == 0) return Task.FromResult(true);
            return Confirm("Detected apps running as admin", string.Join("\n", elevatedApps.Select(app => "• " + app))
                + "\n\nVR may not start. Turn off \"Run this program as an administrator\" in Properties > Compatibility, then restart these apps.",
                "Launch anyway", danger: false);
        }

        void Launch(Action launch, string title, string description)
        {
            try
            {
                launch();
                Status(title);
                ShowLaunchModal(title + "...", description);
            }
            catch (Exception ex)
            {
                Status($"Launch failed: {ex.Message}", true);
            }
        }

        void ShowLaunchModal(string title, string description)
        {
            StopWatcher();
            LaunchTitle.Text = title;
            LaunchDescription.Text = description;
            LaunchDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
            GenPanel.Visibility = Visibility.Collapsed;
            LaunchOverlay.Visibility = Visibility.Visible;

            _watcher = new GameStartupWatcher(_bigWalk.GamePath, new Progress<LaunchPhase>(OnLaunchPhase));
            _watcher.Start();
        }

        async void OnLaunchPhase(LaunchPhase phase)
        {
            switch (phase)
            {
                case LaunchPhase.Generating:
                    GenPanel.Visibility = Visibility.Visible;
                    GenCheck.Visibility = Visibility.Collapsed;
                    GenTitle.Text = "Doing one-time setup for this game version";
                    GenText.Text = "This can take a minute, please wait.";
                    GenText.Visibility = Visibility.Visible;
                    Status("BepInEx is doing one-time setup for this game version");
                    break;
                case LaunchPhase.GenerationDone:
                    GenCheck.Visibility = Visibility.Visible;
                    GenTitle.Text = "Setup complete";
                    GenText.Visibility = Visibility.Collapsed;
                    Status("One-time setup finished");
                    break;
                case LaunchPhase.Ready:
                    LaunchTitle.Text = "Big Walk is running";
                    LaunchDescription.Visibility = Visibility.Collapsed;
                    Status("Big Walk is running");
                    break;
                case LaunchPhase.Exited:
                    CloseLaunchModal();
                    Status("Big Walk closed");
                    break;
                case LaunchPhase.Crashed:
                    CloseLaunchModal();
                    Status("Big Walk may have crashed", true);
                    await PromptCrashReport("Big Walk may have crashed");
                    break;
            }
        }

        void CloseLaunchModal()
        {
            StopWatcher();
            LaunchOverlay.Visibility = Visibility.Collapsed;
        }

        void StopWatcher()
        {
            _watcher?.Stop();
            _watcher = null;
        }

        void LaunchStop_Click(object sender, RoutedEventArgs e)
        {
            _watcher?.StopGame();
            CloseLaunchModal();
            Status("Stopping Big Walk");
        }

        async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Status("Refreshing...");
            await Refresh();
        }

        void Channels_Click(object sender, RoutedEventArgs e)
        {
            var mod = (ModEntry)((FrameworkElement)sender).DataContext;
            ShowChannels((FrameworkElement)sender, mod.Available, mod.Channel?.id, false,
                channel => SelectChannel(channel, value => _settings.BigWalkChannel = value));
        }

        void TitanfallChannels_Click(object sender, RoutedEventArgs e)
        {
            var custom = _titanfall.Record?.custom == true;
            ShowChannels((FrameworkElement)sender, _titanfallAvailable, _titanfallChannel?.id, custom, async channel =>
            {
                await SelectChannel(channel, value => _settings.Titanfall2Channel = value);
                if (custom) await InstallTitanfallPackage();
            }, InstallTitanfallZip);
        }

        async Task SelectChannel(ModChannel channel, Action<string> select)
        {
            select(channel?.id);
            _settings.Save();
            Status(channel != null ? $"{channel.Title} selected" : "Stable selected");
            await Refresh();
        }

        async Task InstallTitanfallZip()
        {
            if (!Ready()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Install from ZIP", Filter = "Mod package (*.zip)|*.zip" };
            if (dialog.ShowDialog(this) != true) return;
            if (!await Confirm("Install custom build", "Only install from sources you trust.\n\nSelect a release to replace this custom build.", "Install", danger: false)) return;
            await InstallTitanfallPackage(dialog.FileName);
        }

        void ShowChannels(FrameworkElement action, ManifestMod available, string current, bool custom, Func<ModChannel, Task> select, Func<Task> installZip = null)
        {
            var menu = CreateChannelMenu(available, current, custom, select, installZip);
            // right aligned under the chevron at the action's right edge
            menu.PlacementTarget = action;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popup, target, offset) =>
                new[] { new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height), PopupPrimaryAxis.Horizontal) };
            menu.IsOpen = true;
        }

        // custom installs check no channel
        ContextMenu CreateChannelMenu(ManifestMod available, string current, bool custom, Func<ModChannel, Task> select, Func<Task> installZip)
        {
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("ChannelMenu")
            };
            if (available != null)
            {
                menu.Items.Add(ChannelItem($"Stable v{available.version}", "Recommended", !custom && current == null, () => select(null)));
                foreach (var channel in available.AvailableChannels)
                    menu.Items.Add(ChannelItem($"{channel.Title} v{channel.version}", channel.description, !custom && current == channel.id, () => select(channel)));
            }
            if (installZip != null)
            {
                var zip = PackageItem("Install from ZIP…", "Choose a custom build");
                zip.Click += async (sender, e) => await installZip();
                menu.Items.Add(zip);
            }
            return menu;
        }

        MenuItem PackageItem(string title, string detail)
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(detail))
                header.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("TextDim"), Margin = new Thickness(0, 1, 0, 0) });
            return new MenuItem { Header = header };
        }

        MenuItem ChannelItem(string title, string detail, bool selected, Func<Task> select)
        {
            var item = PackageItem(title, detail);
            item.IsChecked = selected;
            item.Click += async (sender, e) =>
            {
                if (selected) return;
                await select();
            };
            return item;
        }

        async void ChangeGamePath_Click(object sender, RoutedEventArgs e)
        {
            if (PromptGamePath()) await Refresh();
        }

        bool PromptGamePath()
        {
            var titanfall = _selectedGame == SelectedGame.Titanfall2;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = titanfall ? "Select the Titanfall 2 game folder" : "Select the Big Walk game folder";
                if (titanfall && _titanfall.HasGame) dialog.SelectedPath = _titanfall.GamePath;
                else if (!titanfall && HasGame) dialog.SelectedPath = _bigWalk.GamePath;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;

                try
                {
                    if (titanfall) _titanfall.SetGamePath(dialog.SelectedPath);
                    else _bigWalk.SetGamePath(dialog.SelectedPath);
                    Status($"Game folder set to {(titanfall ? _titanfall.GamePath : _bigWalk.GamePath)}");
                    return true;
                }
                catch (Exception ex)
                {
                    Status(ex.Message, true);
                    return false;
                }
            }
        }

        void OpenGameFolder_Click(object sender, RoutedEventArgs e) => Open(() =>
            GameLauncher.OpenFolder(_selectedGame == SelectedGame.Titanfall2 ? _titanfall.GamePath : _bigWalk.GamePath));

        void TitanfallSaves_Click(object sender, RoutedEventArgs e) =>
            ShowTitanfallSaves(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        void ShowTitanfallSaves(string localAppData)
        {
            TitanfallVrSaveStatus.Text = SaveStatusText(Titanfall2Installer.GetCampaignSaveStatus(Titanfall2Installer.SaveDirectory(localAppData)));
            InstallerView.IsEnabled = false;
            TitanfallSavesOverlay.Visibility = Visibility.Visible;
            TitanfallSavesCloseButton.Focus();
        }

        void CloseTitanfallSaves_Click(object sender, RoutedEventArgs e)
        {
            TitanfallSavesOverlay.Visibility = Visibility.Collapsed;
            InstallerView.IsEnabled = true;
            TitanfallSavesButton.Focus();
        }

        static string SaveStatusText(CampaignSaveStatus status) =>
            status == CampaignSaveStatus.Available ? "Save found" : status == CampaignSaveStatus.Missing ? "No save" : "Incomplete save";

        void OpenTitanfallSaves_Click(object sender, RoutedEventArgs e) => Open(() =>
        {
            var path = Titanfall2Installer.SaveDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Directory.CreateDirectory(path);
            GameLauncher.OpenFolder(path);
        });

        void OpenModLogs_Click(object sender, RoutedEventArgs e) => Open(() =>
        {
            var log = Path.Combine(_bigWalk.GamePath, "BepInEx", "LogOutput.log");
            if (File.Exists(log)) GameLauncher.OpenUrl(log);
            else GameLauncher.OpenFolder(Path.Combine(_bigWalk.GamePath, "BepInEx"));
        });

        void OpenDiscord_Click(object sender, RoutedEventArgs e) => Open(() => GameLauncher.OpenUrl(DiscordUrl));
        async void OpenSupport_Click(object sender, RoutedEventArgs e)
        {
            // let the confetti show before the browser takes focus
            await Task.Delay(300);
            Open(() => GameLauncher.OpenUrl(SupportUrl));
        }
        void OpenRepo_Click(object sender, RoutedEventArgs e) => Open(() => GameLauncher.OpenUrl(RepoUrl));
        async void CreateCrashReport_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == SelectedGame.Titanfall2)
                await PromptCrashReport("Create crash report", "latest session logs and saved crash diagnostics", () => CrashReportService.CreateTitanfall(_titanfall.GamePath));
            else await PromptCrashReport("Create crash report");
        }

        Task PromptCrashReport(string title) => PromptCrashReport(title, "the BepInEx and Unity logs", () => CrashReportService.Create(_bigWalk.GamePath));

        async Task PromptCrashReport(string title, string contents, Func<string> create)
        {
            if (!await Confirm(title,
                $"Create a ZIP containing {contents}. Logs may contain personal or device details, so review them before sharing.",
                "Create report", false)) return;

            try
            {
                GameLauncher.SelectFile(create());
                Status("Crash report created on the Desktop.");
                if (await Confirm("Crash report ready",
                    "The ZIP is selected in Explorer. You can send it in the #support channel on the Discord.",
                    "Open Discord", false)) GameLauncher.OpenUrl(DiscordUrl);
            }
            catch (Exception ex)
            {
                Status($"Couldn't create the crash report: {ex.Message}", true);
            }
        }

        void Open(Action action)
        {
            try { action(); }
            catch (Exception ex) { Status($"Couldn't open that: {ex.Message}", true); }
        }

        async void SelfUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_selfUpdate == null) return;
            SelfUpdateButton.IsEnabled = false;
            SelfUpdateButton.Content = "Updating...";
            try
            {
                Status("Downloading installer update...");
                await SelfUpdater.ApplyUpdate(_selfUpdate);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                SelfUpdateButton.IsEnabled = true;
                SelfUpdateButton.Content = "Update now";
                Status($"Installer update failed: {ex.Message}", true);
            }
        }

        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (InstallView == null) return;
            InstallView.Visibility = NavInstall.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LogsView.Visibility = NavLogs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        TaskCompletionSource<bool> _confirm;

        Task<bool> Confirm(string title, string text, string okLabel, bool danger = true, string cancelLabel = "Cancel")
        {
            if (_confirm != null) return Task.FromResult(false);
            ConfirmTitle.Text = title;
            ConfirmText.Text = text;
            ConfirmOk.Content = okLabel;
            ConfirmCancel.Content = cancelLabel;
            ConfirmOk.Style = (Style)FindResource(danger ? "Danger" : "Primary");
            ConfirmOverlay.Visibility = Visibility.Visible;
            _confirm = new TaskCompletionSource<bool>();
            return _confirm.Task;
        }

        void CloseConfirm(bool result)
        {
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            var confirm = _confirm;
            _confirm = null;
            confirm?.TrySetResult(result);
        }

        void ConfirmOk_Click(object sender, RoutedEventArgs e) => CloseConfirm(true);
        void ConfirmCancel_Click(object sender, RoutedEventArgs e) => CloseConfirm(false);

        void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (_confirm != null)
            {
                CloseConfirm(false);
                e.Handled = true;
                return;
            }
            if (TitanfallSavesOverlay.Visibility == Visibility.Visible)
            {
                CloseTitanfallSaves_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (_selectedGame != SelectedGame.None && LaunchOverlay.Visibility != Visibility.Visible)
            {
                ShowGameSelection();
                e.Handled = true;
            }
        }

        void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Close_Click(object sender, RoutedEventArgs e) => Close();

        void Status(string text, bool error = false)
        {
            _logs.Add(new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Message = text, IsError = error });
            LogsView.ScrollToEnd();
        }
    }
}
