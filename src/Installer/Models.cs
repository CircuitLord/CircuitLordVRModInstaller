using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BigWalkVRInstaller
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Notify(name);
        }

        protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // manifest-v3.json shapes, v3 Titanfall 2 VR packages ship game derived files as patches
    public class Manifest
    {
        public int schemaVersion;
        public string name;
        public ReleaseInfo installer;
        public ReleaseInfo bepinex;
        public ManifestMod titanfall2vr;
        public List<ManifestMod> mods = new List<ManifestMod>();
    }

    public class ReleaseInfo
    {
        public string version;
        public string url;
        public string sha256;
        public long size;
    }

    public class ManifestMod
    {
        public string id;
        public string name;
        public string author;
        public string version;
        public string description;
        public string url;
        public string sha256;
        public long size;
        public ModRelease beta;
        // files kept if they already exist, user calibration and configs
        public List<string> preserve = new List<string>();
        // files with {{GAMEDIR}} / {{GAMEDIR_JSON}} placeholders filled in on install
        public List<string> tokenize = new List<string>();

        public bool HasNewerBeta => beta != null && VersionUtil.IsNewer(beta.version, version);

        public ManifestMod SelectRelease(bool betaUpdates)
        {
            if (!betaUpdates || !HasNewerBeta) return this;
            return new ManifestMod
            {
                id = id,
                name = name,
                author = author,
                version = beta.version,
                description = description,
                url = beta.url,
                sha256 = beta.sha256,
                size = beta.size,
                preserve = preserve,
                tokenize = tokenize
            };
        }
    }

    public class ModRelease
    {
        public string version;
        public string url;
        public string sha256;
        public long size;
    }

    // written into the game folder so installs are tracked per game install
    public class InstallRecord
    {
        public string id;
        public string version;
        public string runtime;
        public string northstarVersion;
        public bool beta;
        public bool custom;
        public List<string> files = new List<string>();
    }

    public class ModEntry : ObservableObject
    {
        // manifest entry with both channels, Remote is the selected one
        public ManifestMod Available;
        public ManifestMod Remote;

        public string Id => Remote.id;
        public string Name => string.IsNullOrEmpty(Remote.name) ? Remote.id : Remote.name;
        public string Description => Remote.description;
        public bool HasDescription => !string.IsNullOrEmpty(Remote.description);

        public bool IsBeta { get; set; }

        string _installedVersion;
        public string InstalledVersion
        {
            get => _installedVersion;
            set { Set(ref _installedVersion, value); NotifyState(); }
        }

        bool _installedBeta;
        public bool InstalledBeta
        {
            get => _installedBeta;
            set { Set(ref _installedBeta, value); NotifyState(); }
        }

        bool _busy;
        public bool Busy { get => _busy; set { Set(ref _busy, value); NotifyState(); } }

        double _progress;
        public double Progress { get => _progress; set => Set(ref _progress, value); }

        string _busyText;
        public string BusyText { get => _busyText; set => Set(ref _busyText, value); }

        public bool IsInstalled => InstalledVersion != null;
        public bool ChannelMismatch => IsInstalled && InstalledBeta != IsBeta;
        public bool CanUpdate => IsInstalled && (ChannelMismatch || VersionUtil.IsNewer(Remote.version, InstalledVersion));
        public bool IsCurrent => IsInstalled && !CanUpdate;

        public bool ShowInstall => !Busy && !IsInstalled;
        public bool ShowUpdate => !Busy && CanUpdate;
        public bool ShowCurrent => !Busy && IsCurrent;
        public bool ShowUninstall => !Busy && IsInstalled;
        public bool ShowChannels => !Busy && Available.HasNewerBeta;

        public string Subtitle
        {
            get
            {
                var author = string.IsNullOrEmpty(Remote.author) ? "unknown" : Remote.author;
                var version = !IsInstalled ? "Not installed" : CanUpdate ? $"v{InstalledVersion} → v{Remote.version}" : $"v{InstalledVersion}";
                var size = Remote.size > 0 ? $"  •  {FormatSize(Remote.size)}" : "";
                return $"{author}  •  {version}{size}";
            }
        }

        static string FormatSize(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        public string InstallLabel => IsBeta ? $"Install beta v{Remote.version}" : $"Install v{Remote.version}";
        public string UpdateLabel => ChannelMismatch
            ? $"Switch to {(IsBeta ? "beta" : "stable")} v{Remote.version}"
            : $"Update to v{Remote.version}";

        void NotifyState()
        {
            foreach (var name in new[] { nameof(IsInstalled), nameof(ChannelMismatch), nameof(CanUpdate), nameof(IsCurrent),
                nameof(ShowInstall), nameof(ShowUpdate), nameof(ShowCurrent), nameof(ShowUninstall), nameof(ShowChannels), nameof(Subtitle),
                nameof(UpdateLabel) })
                Notify(name);
        }
    }

    public static class VersionUtil
    {
        public static bool IsNewer(string remote, string local)
        {
            var coreComparison = Parse(remote).CompareTo(Parse(local));
            if (coreComparison != 0) return coreComparison > 0;

            var remotePre = Prerelease(remote);
            var localPre = Prerelease(local);
            if (remotePre == null) return localPre != null;
            if (localPre == null) return false;
            return ComparePrerelease(remotePre, localPre) > 0;
        }

        static Version Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new Version(0, 0, 0, 0);
            s = s.Trim().TrimStart('v', 'V');
            var core = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            if (!core.Contains(".")) core += ".0";
            if (!Version.TryParse(core, out var v)) return new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        static string Prerelease(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            var dash = version.IndexOf('-');
            if (dash < 0) return null;
            var end = version.IndexOf('+', dash);
            return version.Substring(dash + 1, (end < 0 ? version.Length : end) - dash - 1);
        }

        static int ComparePrerelease(string remote, string local)
        {
            var remoteParts = remote.Split('.');
            var localParts = local.Split('.');
            for (var i = 0; i < Math.Min(remoteParts.Length, localParts.Length); i++)
            {
                if (remoteParts[i] == localParts[i]) continue;
                var remoteNumber = int.TryParse(remoteParts[i], out var r);
                var localNumber = int.TryParse(localParts[i], out var l);
                if (remoteNumber && localNumber) return r.CompareTo(l);
                if (remoteNumber != localNumber) return remoteNumber ? -1 : 1;
                return string.Compare(remoteParts[i], localParts[i], StringComparison.OrdinalIgnoreCase);
            }
            return remoteParts.Length.CompareTo(localParts.Length);
        }
    }
}
