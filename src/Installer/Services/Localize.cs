using System;
using System.Collections.Generic;
using System.Globalization;

namespace BigWalkVRInstaller.Services
{
    /// <summary>
    /// 服务端下发文案、渠道名与代码内动态文本的中文映射。
    /// 界面静态文本走 Localization/Strings.*.xaml，这里只处理 XAML 拿不到的部分。
    /// </summary>
    public static class Localize
    {
        private static readonly bool IsChinese =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            // 状态与按钮
            { "Game not found", "未找到游戏" },
            { "Ready to play", "可以开玩" },
            { "Ready to install", "可以安装" },
            { "Setup required", "需要设置" },
            { "Update available", "有可用更新" },
            { "Launch in VR", "启动 VR 模式" },
            { "Join multiplayer beta", "加入多人测试版" },

            // EA app 区块
            { "Titanfall 2 requires you to be signed into the EA app to play.",
              "Titanfall 2 需要登录 EA app 才能游玩。" },
            { "EA app not found. Launch Titanfall 2 once from Steam to install it and sign in, then press Refresh.",
              "未找到 EA app。请先从 Steam 启动一次 Titanfall 2 完成安装并登录，然后点刷新。" },
            { "Open EA app", "打开 EA app" },
            { "Launch from Steam", "从 Steam 启动" },

            // 版本行与渠道菜单
            { "Installed", "已安装" },
            { "Download list unavailable", "下载列表不可用" },
            { "Stable", "稳定版" },
            { "Recommended", "推荐" },
            { "selected", "已选择 " },
            { "Stable selected", "已选择稳定版" },
            { "Install from ZIP", "从 ZIP 安装" },
            { "Install from ZIP…", "从 ZIP 安装…" },
            { "Choose a custom build", "选择自定义版本" },
            { "Install custom build", "安装自定义版本" },
            { "Only install from sources you trust.\n\nSelect a release to replace this custom build.",
              "请只安装来自可信来源的文件。\n\n选择正式版本可替换此自定义版本。" },
            { "Install", "安装" },
            { "Not found. Press Change and pick your Big Walk folder.", "未找到。点「更改」选择 Big Walk 文件夹。" },
            { "Not found. Press Change and pick your Titanfall 2 folder.", "未找到。点「更改」选择《泰坦陨落 2》文件夹。" },
            { "Remove MelonLoader", "移除 MelonLoader" },
            { "Restore vanilla Big Walk", "恢复 Big Walk 原版" },
            { "Permanently removes every installed mod, BepInEx, and MelonLoader.", "将永久移除所有已安装的 Mod、BepInEx 与 MelonLoader。" },
            { "Restore vanilla", "恢复原版" },
            { "Installing BepInEx...", "正在安装 BepInEx…" },
            { "Allow access to the game folder", "允许访问游戏文件夹" },
            { "Uninstall Titanfall 2 VR", "卸载 Titanfall 2 VR" },
            { "Removes the TF2VR profile, renamed launcher, and files owned by this installer.", "将移除 TF2VR 配置、重命名的启动器，以及本安装器创建的文件。" },
            { "Sign in to EA app", "登录 EA app" },
            { "Sign in and stay signed in so Titanfall 2 VR can start without asking in VR.", "请登录并保持登录状态，这样在 VR 中启动时不会再次询问。" },
            { "I'm signed in", "我已登录" },
            { "Detected apps running as admin", "检测到以管理员权限运行的程序" },
            { "VR may not start. Turn off \"Run this program as an administrator\" in Properties > Compatibility, then restart these apps.",
              "VR 可能无法启动。请在「属性 > 兼容性」中取消勾选「以管理员身份运行此程序」，然后重启这些程序。" },
            { "Launch anyway", "仍然启动" },
            { "Doing one-time setup for this game version", "正在为此游戏版本进行一次性设置" },
            { "This can take a minute, please wait.", "可能需要一分钟，请稍候。" },
            { "Setup complete", "设置完成" },
            { "Crash report ready", "崩溃报告已就绪" },
            { "Update now", "立即更新" },
            { "The mod loader Big Walk VR depends on.", "Big Walk VR 所依赖的 Mod 加载器。" },
            { "Play normally while seeing VR players' tracked movement", "正常游玩，同时看到 VR 玩家的手部追踪" },
            { "Finish steps 1-3 first", "请先完成第 1-3 步" },
            { "Launch Big Walk in VR, start SteamVR first", "以 VR 模式启动 Big Walk，请先开启 SteamVR" },
            { "Launch Titanfall 2 VR", "启动 Titanfall 2 VR" },
            { "Install Titanfall 2 VR first", "请先安装 Titanfall 2 VR" },
            { "MelonLoader must be removed before Big Walk can launch.", "必须先移除 MelonLoader 才能启动 Big Walk。" },
            { "Installing BepInEx will permanently remove MelonLoader and everything in its Mods, Plugins, and UserData folders.", "安装 BepInEx 将永久移除 MelonLoader 及其 Mods、Plugins、UserData 文件夹中的所有内容。" },
            { "Remove and install", "移除并安装" },
            { "Select the Titanfall 2 game folder", "选择《泰坦陨落 2》游戏文件夹" },
            { "Select the Big Walk game folder", "选择 Big Walk 游戏文件夹" },
            { "changelog", "更新日志" },
            { "Latest", "最新" },
            { "Cancel", "取消" },
            { "Installer update available:", "安装器有可用更新：" },
            { "Downloading", "正在下载" },
            { "Installing", "正在安装" },
            { "Approve the Windows prompt to allow access to the game folder.", "请批准 Windows 权限提示，以允许访问游戏文件夹。" },
            { "BepInEx is doing one-time setup for this game version", "BepInEx 正在为此游戏版本进行一次性设置" },
            { "Big Walk closed", "Big Walk 已关闭" },
            { "Big Walk is back to vanilla", "Big Walk 已恢复为原版" },
            { "Big Walk is running", "Big Walk 正在运行" },
            { "Big Walk may have crashed", "Big Walk 可能已崩溃" },
            { "Checking headset resolution...", "正在检查头显分辨率…" },
            { "Choose a game to continue", "请先选择游戏以继续" },
            { "Close Big Walk before changing mods", "更改 Mod 前请先关闭 Big Walk" },
            { "Close Titanfall 2 before changing the installation", "更改安装前请先关闭 Titanfall 2" },
            { "Crash report created on the Desktop.", "崩溃报告已创建在桌面上。" },
            { "Downloading BepInEx...", "正在下载 BepInEx…" },
            { "Downloading Northstar...", "正在下载 Northstar…" },
            { "Downloading Titanfall 2 VR...", "正在下载 Titanfall 2 VR…" },
            { "Downloading installer update...", "正在下载安装器更新…" },
            { "EA app sign in confirmed", "EA app 登录已确认" },
            { "Everything is up to date", "全部已是最新" },
            { "Installing Titanfall 2 VR...", "正在安装 Titanfall 2 VR…" },
            { "Launching Titanfall 2 VR", "正在启动 Titanfall 2 VR" },
            { "One-time setup finished", "一次性设置已完成" },
            { "Pick your Big Walk folder first", "请先选择 Big Walk 文件夹" },
            { "Pick your Titanfall 2 folder first", "请先选择 Titanfall 2 文件夹" },
            { "Reading ZIP...", "正在读取 ZIP…" },
            { "Refreshing...", "正在刷新…" },
            { "Remove MelonLoader in step 2 to continue.", "请在第 2 步移除 MelonLoader 以继续。" },
            { "Stopping Big Walk", "正在关闭 Big Walk" },
            { "Titanfall 2 VR removed", "Titanfall 2 VR 已移除" },
            { "Not installed", "未安装" },
            { "Loading", "正在加载" },
        };

        private static readonly Dictionary<string, string> ZhChannel = new Dictionary<string, string>
        {
            { "New features, may be unstable", "包含新功能，可能不稳定" },
            { "Multiplayer open beta", "多人游戏公开测试" },
        };

        private static readonly Dictionary<string, string> ZhChannelTitle = new Dictionary<string, string>
        {
            { "beta", "测试版" },
            { "Beta", "测试版" },
            { "Multiplayer-beta", "多人测试版" },
            { "multiplayer-beta", "多人测试版" },
            { "stable", "稳定版" },
        };

        private static readonly Dictionary<string, string> ZhChangeNotes = new Dictionary<string, string>
        {
            { "1.0.25", "变更：\n"
                      + "- 新增对 Tick 与炸药包的支援\n"
                      + "- 修正双手持用弓、雷霆炮或电弧枪时的偏移\n"
                      + "- 修正浪人剑核心导致技能无法使用的问题\n"
                      + "- 修正 EPG 弹匣的抓取姿势" },
            { "1.0.24", "迄今为止 TF2VR 最大的一次更新！\n\n"
                      + "重点：\n"
                      + "- 全新的头部驱动泰坦操作方案\n"
                      + "- 改进并统一了所有枪械的弹匣抓取姿势\n"
                      + "- 「闪烁抑制」图形设置，以极小的性能代价大幅减少锯齿\n"
                      + "- 大量其他打磨与错误修正！\n\n"
                      + "加入 Discord 查看完整更新日志！" },
        };

        /// <summary>动态拼接的短文本（状态栏、按钮、菜单项）。缺条目时回退英文。</summary>
        public static string T(string english)
        {
            if (!IsChinese || english == null) return english;
            return Zh.TryGetValue(english, out var zh) ? zh : english;
        }

        /// <summary>渠道名（beta / multiplayer-beta）在界面上以标题形式出现。</summary>
        public static string ChannelTitle(string id)
        {
            if (!IsChinese || id == null) return id;
            return ZhChannelTitle.TryGetValue(id, out var zh) ? zh : id;
        }

        public static string ModDescription(string id, string text)
        {
            if (!IsChinese) return text;
            switch (id)
            {
                case "BigWalkVR": return "为 Big Walk 提供完整、兼容多人游戏的 SteamVR 支持。";
                case "Titanfall2VR": return "为《泰坦陨落 2》战役提供 OpenXR VR 支持。";
                default: return text;
            }
        }

        public static string ChannelDetail(string text)
        {
            if (!IsChinese || text == null) return text;
            return ZhChannel.TryGetValue(text, out var zh) ? zh : text;
        }

        /// <summary>更新日志随 manifest 下发，只能按版本号查表；未收录的版本原样显示。</summary>
        public static string ChangeNotes(string version, string notes)
        {
            if (!IsChinese || string.IsNullOrEmpty(notes)) return notes;
            return ZhChangeNotes.TryGetValue(version, out var zh) ? zh : notes;
        }
    }
}
