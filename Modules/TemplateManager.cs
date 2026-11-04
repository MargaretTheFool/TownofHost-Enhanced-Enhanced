using AmongUs.Data;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static TOHE.Translator;

namespace TOHE;

public static class TemplateManager
{
    private static readonly string TEMPLATE_FILE_PATH = "./TOHEE-DATA/template.txt";
    private static readonly Dictionary<string, Func<string>> _replaceDictionaryNormalOptions = new()
    {
        ["RoomCode"] = () => InnerNet.GameCode.IntToGameName(AmongUsClient.Instance.GameId),
        ["HostName"] = () => DataManager.Player.Customization.Name,
        ["AmongUsVersion"] = () => UnityEngine.Application.version,
        ["InternalVersion"] = () => Main.PluginVersion,
        ["ModVersion"] = () => Main.PluginDisplayVersion,
        ["Map"] = () => Constants.MapNames[Main.NormalOptions.MapId],
        ["NumEmergencyMeetings"] = () => Main.NormalOptions.NumEmergencyMeetings.ToString(),
        ["EmergencyCooldown"] = () => Main.NormalOptions.EmergencyCooldown.ToString(),
        ["DiscussionTime"] = () => Main.NormalOptions.DiscussionTime.ToString(),
        ["VotingTime"] = () => Main.NormalOptions.VotingTime.ToString(),
        ["PlayerSpeedMod"] = () => Main.NormalOptions.PlayerSpeedMod.ToString(),
        ["CrewLightMod"] = () => Main.NormalOptions.CrewLightMod.ToString(),
        ["ImpostorLightMod"] = () => Main.NormalOptions.ImpostorLightMod.ToString(),
        ["KillCooldown"] = () => Main.NormalOptions.KillCooldown.ToString(),
        ["NumCommonTasks"] = () => Main.NormalOptions.NumCommonTasks.ToString(),
        ["NumLongTasks"] = () => Main.NormalOptions.NumLongTasks.ToString(),
        ["NumShortTasks"] = () => Main.NormalOptions.NumShortTasks.ToString(),
        ["Date"] = () => DateTime.Now.ToShortDateString(),
        ["Time"] = () => DateTime.Now.ToShortTimeString(),
        ["PlayerName"] = () => "",
        ["LobbyTimer"] = () =>
        {
            if (GameStates.IsLobby)
            {
                int timer = (int)GameStartManagerPatch.timer;
                int minutes = timer / 60;
                int seconds = timer % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
            else
            {
                return string.Empty;
            }
        }
    };

    private static readonly Dictionary<string, Func<string>> _replaceDictionaryHideNSeekOptions = new()
    {
        ["RoomCode"] = () => InnerNet.GameCode.IntToGameNameV2(AmongUsClient.Instance.GameId),
        ["HostName"] = () => DataManager.Player.Customization.Name,
        ["AmongUsVersion"] = () => UnityEngine.Application.version,
        ["InternalVersion"] = () => Main.PluginVersion,
        ["ModVersion"] = () => Main.PluginDisplayVersion,
        ["Map"] = () => Constants.MapNames[Main.HideNSeekOptions.MapId],
        ["PlayerSpeedMod"] = () => Main.HideNSeekOptions.PlayerSpeedMod.ToString(),
        ["Date"] = () => DateTime.Now.ToShortDateString(),
        ["Time"] = () => DateTime.Now.ToShortTimeString(),
        ["PlayerName"] = () => "",
        ["LobbyTimer"] = () =>
        {
            if (GameStates.IsLobby)
            {
                int timer = (int)GameStartManagerPatch.timer;
                int minutes = timer / 60;
                int seconds = timer % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
            else
            {
                return string.Empty;
            }
        }
    };

    public static void Init()
    {
        CreateIfNotExists(refreshDefault: true);
    }

    public static void CreateIfNotExists(bool refreshDefault = false)
    {

        try
        {
            string fileName;
            string name = CultureInfo.CurrentCulture.Name;
            if (name.Length >= 2)
                fileName = name switch // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-lcid/a9eac961-e77d-41a6-90a5-ce1a8b0cdb9c
                {
                    var lang when lang.StartsWith("ru") => "Russian",
                    "zh-Hans" or "zh" or "zh-CN" or "zn-SG" => "SChinese",
                    "zh-Hant" or "zh-HK" or "zh-MO" or "zh-TW" => "TChinese",
                    "pt-BR" => "Brazilian",
                    "es-419" => "Latam",
                    var lang when lang.StartsWith("es") => "Spanish",
                    var lang when lang.StartsWith("fr") => "French",
                    "ja" or "ja-JP" => "Japanese",
                    var lang when lang.StartsWith("nl") => "Dutch",
                    var lang when lang.StartsWith("it") => "Italian",
                    _ => "English"
                };
            else fileName = "English";
            if (!Directory.Exists(@"TOHEE-DATA")) Directory.CreateDirectory(@"TOHEE-DATA");
            var defaultTemplateMsg = GetResourcesTxt($"TOHE.Resources.Config.template.{fileName}.txt");
            const string defaultTemplatePath = @"./TOHEE-DATA/Default_Teamplate.txt";
            var defaultTemplateExists = File.Exists(defaultTemplatePath);
            if (!defaultTemplateExists)
                Logger.Warn("Creating Default_Template.txt", "TemplateManager");
            if (refreshDefault || !defaultTemplateExists)
                File.WriteAllText(defaultTemplatePath, defaultTemplateMsg);
            if (!File.Exists(TEMPLATE_FILE_PATH))
            {
                if (File.Exists(@"./template.txt")) File.Copy(@"./template.txt", TEMPLATE_FILE_PATH);
                else
                {
                    Logger.Warn($"Creating a new Template file from: {fileName}", "TemplateManager");
                    File.WriteAllText(TEMPLATE_FILE_PATH, defaultTemplateMsg);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "TemplateManager");
        }
    }

    private static string GetResourcesTxt(string path)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
        stream.Position = 0;
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static void SendTemplate(string str = "", byte playerId = 0xff, bool noErr = false)
    {
        CreateIfNotExists();
        using StreamReader sr = new(TEMPLATE_FILE_PATH, Encoding.GetEncoding("UTF-8"));
        string text;
        string[] tmp = [];
        List<string> sendList = [];
        HashSet<string> tags = [];
        Func<string> playerName = () => "";
        if (playerId != 0xff)
        {
            playerName = () => Main.AllPlayerNames[playerId];
        }

        _replaceDictionaryNormalOptions["PlayerName"] = playerName;
        _replaceDictionaryHideNSeekOptions["PlayerName"] = playerName;

        while ((text = sr.ReadLine()) != null)
        {
            tmp = text.Split(":");
            if (tmp.Length > 1 && tmp[1] != "")
            {
                tags.Add(tmp[0]);
                if (tmp[0].ToLower() == str.ToLower()) sendList.Add(tmp.Skip(1).Join(delimiter: ":").Replace("\\n", "\n"));
            }
        }
        if (sendList.Count == 0 && !noErr)
        {
            if (playerId == 0xff)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, string.Format(GetString("Message.TemplateNotFoundHost"), str, tags.Join(delimiter: ", ")));
            else Utils.SendMessage(string.Format(GetString("Message.TemplateNotFoundClient"), str), playerId, noReplay: true);
        }
        else foreach (string x in sendList.ToArray())
        {
            var (title, body) = ParseTemplate(x);
            Utils.SendMessage(ApplyReplaceDictionary(body), playerId, ApplyReplaceDictionary(title), noReplay: true);
        }
    }

    private static string ApplyReplaceDictionary(string text)
    {
        try
        {
            if (GameStates.IsNormalGame)
            {
                foreach (var kvp in _replaceDictionaryNormalOptions.ToArray())
                {
                    text = Regex.Replace(text, "{{" + kvp.Key + "}}", kvp.Value.Invoke() ?? "", RegexOptions.IgnoreCase);
                }
            }
            else if (GameStates.IsHideNSeek)
            {
                foreach (var kvp in _replaceDictionaryHideNSeekOptions.ToArray())
                {
                    text = Regex.Replace(text, "{{" + kvp.Key + "}}", kvp.Value.Invoke() ?? "", RegexOptions.IgnoreCase);
                }
            }
            return text;
        }
        catch //(Exception ex)
        {
            //Logger.Exception(ex, "TemplateManager.ApplyReplaceDictionary");
            return text;
        }
    }

    internal static (string Title, string Body) ParseTemplate(string text)
    {
        const string open = "<title>";
        const string close = "</title>";
        int start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return (string.Empty, text);
        int contentStart = start + open.Length;
        int end = text.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (end < 0) return (string.Empty, text);

        // Compatibility for the exact typo shipped in the old default template.
        // Do not overwrite the user's file or rewrite other rich-text tags.
        string title = text[contentStart..end].Replace(
            "<color=#ffc0cb【欢迎来到 Town of Host Enhanced】",
            "<color=#ffc0cb>【欢迎来到 Town of Host Enhanced】", StringComparison.Ordinal);
        return (title, text.Remove(start, end + close.Length - start));
    }
}
