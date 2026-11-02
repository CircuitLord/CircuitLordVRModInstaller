using System;
using System.Collections.Generic;
using System.Globalization;

namespace BigWalkVRInstaller.Services
{
    /// <summary>
    /// 服务端下发文案与少量动态文本的中文映射。
    /// 界面静态文本走 Localization/Strings.*.xaml，这里只处理 manifest 与代码里拼出来的字符串。
    /// </summary>
    public static class Localize
    {
        private static readonly bool IsChinese =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            { "Game not found", "未找到游戏" },
            { "Ready to play", "可以开玩" },
            { "Ready to install", "可以安装" },
            { "Setup required", "需要设置" },
            { "Update available", "有可用更新" },
        };

        private static readonly Dictionary<string, string> ZhChannel = new Dictionary<string, string>
        {
            { "New features, may be unstable", "包含新功能，可能不稳定" },
            { "Multiplayer open beta", "多人游戏公开测试" },
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

        /// <summary>动态拼接的短文本（状态栏等）。缺条目时回退英文。</summary>
        public static string T(string english)
        {
            if (!IsChinese || english == null) return english;
            return Zh.TryGetValue(english, out var zh) ? zh : english;
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
