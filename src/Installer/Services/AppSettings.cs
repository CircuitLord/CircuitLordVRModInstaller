using System;
using System.IO;

namespace BigWalkVRInstaller.Services
{
    public class AppSettings
    {
        public const string ManifestUrl = "https://raw.githubusercontent.com/CircuitLord/CircuitLordVRModInstaller/main/manifest-v3.json";
        public string GamePath;
        public string BigWalkPath;
        public string Titanfall2Path;
        // selected channel ids, null is stable
        public string BigWalkChannel;
        public string Titanfall2Channel;
        public bool Titanfall2EaSignedIn;

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BigWalkVRInstaller");
        static string FilePath => Path.Combine(Dir, "settings.json");

        public static string LoadError;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = JsonUtil.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                    if (string.IsNullOrEmpty(settings.BigWalkPath)) settings.BigWalkPath = settings.GamePath;
                    return settings;
                }
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonUtil.Serialize(this));
        }
    }
}
