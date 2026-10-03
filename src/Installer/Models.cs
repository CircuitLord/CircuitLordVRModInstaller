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
        // optional release channels, a channel shows only while newer than stable
        public List<ModChannel> channels;
        // files kept if they already exist, user calibration and configs
        public List<string> preserve = new List<string>();
        // files with {{GAMEDIR}} / {{GAMEDIR_JSON}} placeholders filled in on install
        public List<string> tokenize = new List<string>();

        public List<ModChannel> AvailableChannels =>
            channels?.Where(channel => VersionUtil.IsNewer(channel.version, version)).ToList() ?? new List<ModChannel>();

        public ModChannel FindChannel(string id) => AvailableChannels.FirstOrDefault(channel => channel.id == id);

        // null channel is stable
        public ManifestMod SelectRelease(ModChannel channel)
        {
            if (channel == null) return this;
            return new ManifestMod
            {
                id = id,
                name = name,
                author = author,
                version = channel.version,
                description = description,
                url = channel.url,
                sha256 = channel.sha256,
                size = channel.size,
                preserve = preserve,
                tokenize = tokenize
            };
        }
    }

    public class ModChannel : ReleaseInfo
    {
        public string id;
        public string description;

        public string Title => char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    // written into the game folder so installs are tracked per game install
    public class InstallRecord
    {
        public string id;
        public string version;
        public string runtime;
        public string northstarVersion;
        // null is stable
        public string channel;
        public bool custom;
        public List<string> files = new List<string>();
    }

    public enum InstallState { Install, Update, Installed }

    public class ModEntry : ObservableObject
    {
        // manifest entry with both channels, Remote is the selected one
        public ManifestMod Available;
        public ManifestMod Remote;

        public string Id => Remote.id;
        public string Name => string.IsNullOrEmpty(Remote.name) ? Remote.id : Remote.name;
        public string Description => Remote.description;
        public bool HasDescription => !string.IsNullOrEmpty(Remote.description);

        // null is stable
        public ModChannel Channel { get; set; }
        public bool HasChannel => Channel != null;
        public string ChannelTag => Channel?.id.ToUpperInvariant();

        string _installedVersion;
        public string InstalledVersion
        {
            get => _installedVersion;
            set { Set(ref _installedVersion, value); NotifyState(); }
        }

        string _installedChannel;
        public string InstalledChannel
        {
            get => _installedChannel;
            set { Set(ref _installedChannel, value); NotifyState(); }
        }

        bool _busy;
        public bool Busy { get => _busy; set { Set(ref _busy, value); NotifyState(); } }

        double _progress;
        public double Progress { get => _progress; set => Set(ref _progress, value); }

        string _busyText;
        public string BusyText { get => _busyText; set => Set(ref _busyText, value); }

        public bool IsInstalled => InstalledVersion != null;
        public bool ChannelMismatch => IsInstalled && InstalledChannel != Channel?.id;
        public bool CanUpdate => IsInstalled && (ChannelMismatch || VersionUtil.IsNewer(Remote.version, InstalledVersion));
        public bool IsCurrent => IsInstalled && !CanUpdate;

        public InstallState State => !IsInstalled ? InstallState.Install : CanUpdate ? InstallState.Update : InstallState.Installed;
        public bool ShowUpdate => !Busy && CanUpdate;
        public bool ShowUninstall => !Busy && IsInstalled;
        public bool ShowChannels => Available.AvailableChannels.Count > 0;

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

        public string ActionLabel => CanUpdate ? UpdateLabel : HasChannel ? $"Install {Channel.id} v{Remote.version}" : $"Install v{Remote.version}";
        public string UpdateLabel => ChannelMismatch
            ? $"Switch to {Channel?.id ?? "stable"} v{Remote.version}"
            : $"Update to v{Remote.version}";

        void NotifyState()
        {
            foreach (var name in new[] { nameof(IsInstalled), nameof(ChannelMismatch), nameof(CanUpdate), nameof(IsCurrent),
                nameof(State), nameof(ShowUpdate), nameof(ShowUninstall), nameof(Subtitle), nameof(ActionLabel), nameof(UpdateLabel) })
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
