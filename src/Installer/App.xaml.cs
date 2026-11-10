using System;
using System.Windows;
using BigWalkVRInstaller.Installers;
using BigWalkVRInstaller.Services;

namespace BigWalkVRInstaller
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // elevated copy started by Titanfall2Installer.GrantGameFolderAccess, exits without showing a window
            if (e.Args.Length == 3 && e.Args[0] == Titanfall2Installer.GrantAccessArgument)
            {
                try { Titanfall2Installer.ApplyGameFolderAccess(e.Args[1], e.Args[2]); }
                catch { Environment.Exit(1); }
                Environment.Exit(0);
            }
            SelfUpdater.CleanupOldExe();
            // 按系统 UI 语言加载界面语言包；缺文件时回退英文
            var uiLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
                ? "zh-Hans" : "en";
            try
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Localization/Strings.{uiLang}.xaml", UriKind.Absolute)
                });
            }
            catch { }
            base.OnStartup(e);
        }
    }
}
