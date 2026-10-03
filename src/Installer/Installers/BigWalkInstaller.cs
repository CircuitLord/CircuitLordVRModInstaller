using System;
using BigWalkVRInstaller.Services;

namespace BigWalkVRInstaller.Installers
{
    public sealed class BigWalkInstaller : IVrModInstaller
    {
        readonly AppSettings _settings;

        public BigWalkInstaller(AppSettings settings) => _settings = settings;

        public string Id => "big-walk-vr";
        public string Name => "Big Walk VR";
        public string GamePath => _settings.BigWalkPath;
        public bool HasGame => GameLocator.IsValidGamePath(GamePath);
        public string InstalledVersion => HasGame ? PackageInstaller.ReadRecord(GamePath, "BigWalkVR")?.version : null;
        public bool IsInstalled => InstalledVersion != null;

        public string DetectGamePath()
        {
            var path = GameLocator.DetectGamePath();
            if (path != null) SetGamePath(path);
            return path;
        }

        public void SetGamePath(string path)
        {
            if (!GameLocator.IsValidGamePath(path)) throw new Exception($"That folder doesn't contain {GameLocator.ExeName}");
            _settings.BigWalkPath = GameLocator.Canonical(path);
            _settings.Save();
        }

        public void InstallMod(ManifestMod mod, byte[] package, string channel) => PackageInstaller.Install(GamePath, mod, package, channel);

        public void UninstallMod(string id) => PackageInstaller.Uninstall(GamePath, id);

        public LoaderMigrationResult InstallLoader(byte[] package) => BepInExInstaller.Extract(GamePath, package);

        public void RestoreVanilla()
        {
            foreach (var record in PackageInstaller.ReadRecords(GamePath)) PackageInstaller.Uninstall(GamePath, record.id);
            BepInExInstaller.Remove(GamePath);
        }

        public void Uninstall() => RestoreVanilla();

        public void Play() => GameLauncher.LaunchVr(GamePath);

        public void PlayNonVr() => GameLauncher.LaunchNonVr(GamePath);
    }
}
