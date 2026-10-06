using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BigWalkVRInstaller.Services
{
    public static class LaunchPermissions
    {
        public const string TestElevatedAppsEnvironment = "CIRCUITLORD_TEST_ELEVATED_APPS";
        const uint ProcessQueryLimitedInformation = 0x1000;
        const uint TokenQuery = 0x0008;
        const int TokenElevation = 20;

        public static string[] GetElevatedApps()
        {
            var testApps = Environment.GetEnvironmentVariable(TestElevatedAppsEnvironment);
            if (!string.IsNullOrWhiteSpace(testApps)) return testApps.Split(',').Select(app => app.Trim()).Where(app => app.Length > 0).ToArray();
            var apps = new List<string>();
            using (var current = Process.GetCurrentProcess())
            {
                if (IsElevated(current.Id)) apps.Add("Installer");
                foreach (var app in new[] { (process: "steam", label: "Steam"), (process: "EADesktop", label: "EA app") })
                {
                    var elevated = false;
                    foreach (var process in Process.GetProcessesByName(app.process))
                    {
                        using (process)
                            if (process.SessionId == current.SessionId && IsElevated(process.Id)) elevated = true;
                    }
                    if (elevated) apps.Add(app.label);
                }
            }
            return apps.ToArray();
        }

        public static bool IsElevated(int processId)
        {
            using (var process = OpenProcess(ProcessQueryLimitedInformation, false, processId))
            {
                if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!OpenProcessToken(process, TokenQuery, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (token)
                {
                    if (!GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return elevated != 0;
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returnLength);
    }
}
