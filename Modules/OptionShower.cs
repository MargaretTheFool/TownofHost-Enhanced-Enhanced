using System;
using System.Text;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

public static class OptionShower
{
    public static int currentPage;
    public static List<string> pages = [];
    private static int[] optionValues = [];
    private static string vanillaSettings, renderedText;
    private static int renderedPage = -1, cachedPreset = -1, cachedLanguage = -1;
    private static bool cachedHidden, cachedHost, cachedOwnLanguage, cachedRoleLanguage;
    private static float nextProbe;
    private static Func<string, bool> pageFits;

    public static void Reset()
    {
        currentPage = 0;
        pages.Clear();
        optionValues = [];
        renderedText = vanillaSettings = null;
        renderedPage = cachedPreset = cachedLanguage = -1;
        nextProbe = 0;
        pageFits = null;
    }

    internal static void ConfigurePagination(Func<string, bool> fits)
    {
        pageFits = fits;
        pages.Clear();
        renderedPage = -1;
        nextProbe = 0;
    }

    public static string GetTextNoFresh()
    {
        bool host = AmongUsClient.Instance.AmHost;
        bool hidden = Options.HideGameSettings.GetBool() && !host;
        // Privacy changes bypass the sampling timer and discard every cached page.
        bool changed = pages.Count == 0 || hidden != cachedHidden || host != cachedHost;
        if (changed || Time.unscaledTime >= nextProbe)
        {
            nextProbe = Time.unscaledTime + 0.5f;
            string vanilla = hidden ? string.Empty : GetVanillaSettings();
            int language = TranslationController.InstanceExists
                ? (int)TranslationController.Instance.currentLanguage.languageID : -1;
            changed |= vanilla != vanillaSettings || cachedPreset != OptionItem.CurrentPreset ||
                cachedLanguage != language || cachedOwnLanguage != Main.ForceOwnLanguage.Value ||
                cachedRoleLanguage != Main.ForceOwnLanguageRoleName.Value ||
                optionValues.Length != OptionItem.AllOptions.Count;
            if (!changed)
                for (int i = 0; i < optionValues.Length; i++)
                    if (optionValues[i] != OptionItem.AllOptions[i].CurrentValue) { changed = true; break; }
            if (changed) Rebuild(vanilla, language, hidden, host);
        }
        return CurrentText();
    }

    public static string GetText()
    {
        bool host = AmongUsClient.Instance.AmHost;
        bool hidden = Options.HideGameSettings.GetBool() && !host;
        int language = TranslationController.InstanceExists
            ? (int)TranslationController.Instance.currentLanguage.languageID : -1;
        Rebuild(hidden ? string.Empty : GetVanillaSettings(), language, hidden, host);
        return CurrentText();
    }

    private static string GetVanillaSettings()
    {
        string label = DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.GameConfirmImpostor) + ": ";
        return string.Join("\n", GameOptionsManager.Instance.CurrentGameOptions
            .ToHudString(GameData.Instance ? GameData.Instance.PlayerCount : 10).Split('\n')
            .Where(line => !line.StartsWith(label, StringComparison.Ordinal)));
    }

    private static void Rebuild(string vanilla, int language, bool hidden, bool host)
    {
        cachedHidden = hidden;
        cachedHost = host;
        cachedPreset = OptionItem.CurrentPreset;
        cachedLanguage = language;
        cachedOwnLanguage = Main.ForceOwnLanguage.Value;
        cachedRoleLanguage = Main.ForceOwnLanguageRoleName.Value;
        vanillaSettings = vanilla;
        optionValues = new int[OptionItem.AllOptions.Count];
        for (int i = 0; i < optionValues.Length; i++) optionValues[i] = OptionItem.AllOptions[i].CurrentValue;

        List<string> sections = [];
        if (hidden)
            sections.Add($"<color=#ff0000>{GetString("Message.HideGameSettings")}</color>");
        else
        {
            sections.Add(vanilla);
            StringBuilder sb = new();
            sb.Append($"{Options.GameMode.GetName()}: {Options.GameMode.GetString()}\n\n");
            if (Options.CurrentGameMode == CustomGameMode.Standard)
            {
                sb.Append(GetString("ActiveRolesList")).Append('\n');
                foreach (var kvp in Options.CustomRoleSpawnChances)
                {
                    if (kvp.Value.GameMode is not (CustomGameMode.Standard or CustomGameMode.All) || !kvp.Value.GetBool()) continue;
                    string mode = kvp.Value.GetString();
                    if (kvp.Key is CustomRoles.Lovers) mode = Utils.GetChance(Options.LoverSpawnChances.GetInt());
                    else if (kvp.Key.IsAdditionRole() && Options.CustomAdtRoleSpawnRate.TryGetValue(kvp.Key, out var rate))
                        mode = Utils.GetChance(rate.GetFloat());
                    sb.Append($"{Utils.ColorString(Utils.GetRoleColor(kvp.Key), Utils.GetRoleName(kvp.Key))}: {mode}×{kvp.Key.GetCount()}\n");
                }
            }
            sections.Add(sb.ToString());
            sb.Clear();
            foreach (var kvp in Options.CustomRoleSpawnChances)
            {
                if (!kvp.Key.IsEnable() || kvp.Value.IsHiddenOn(Options.CurrentGameMode)) continue;
                sb.Append($"{Utils.ColorString(Utils.GetRoleColor(kvp.Key), Utils.GetRoleName(kvp.Key))}: {kvp.Value.GetString()}×{kvp.Key.GetCount()}\n");
                ShowChildren(kvp.Value, sb, Utils.GetRoleColor(kvp.Key).ShadeColor(-0.5f), 1);
                sections.Add(sb.ToString());
                sb.Clear();
            }
            foreach (var opt in OptionItem.AllOptions)
            {
                if (opt.Id <= 59999 || opt.IsHiddenOn(Options.CurrentGameMode) || opt.Parent != null || opt.IsText) continue;
                sb.Append($"{opt.GetName()}: {opt.GetString()}\n");
                if (opt.GetBool()) ShowChildren(opt, sb, Color.white, 1);
                sections.Add(sb.ToString());
                sb.Clear();
            }
        }
        pages = SettingsPreviewPagination.Build(sections, 32, pageFits);
        currentPage = Math.Clamp(currentPage, 0, pages.Count - 1);
        renderedPage = -1;
    }

    private static string CurrentText()
    {
        currentPage = Math.Clamp(currentPage, 0, pages.Count - 1);
        if (renderedPage != currentPage)
        {
            renderedPage = currentPage;
            string footer = Application.platform == RuntimePlatform.Android
                ? $"({currentPage + 1}/{pages.Count})"
                : $"{GetString("PressTabToNextPage")} ({currentPage + 1}/{pages.Count})";
            renderedText = $"{pages[currentPage]}\n\n{footer}";
        }
        return renderedText;
    }

    public static void Next() => SelectPage((currentPage + 1) % Math.Max(1, pages.Count));

    public static void Previous() => SelectPage((currentPage + Math.Max(1, pages.Count) - 1) % Math.Max(1, pages.Count));

    public static void SelectPage(int page)
    {
        if (page >= 0 && page < pages.Count) currentPage = page;
    }

    private static void ShowChildren(OptionItem option, StringBuilder sb, Color color, int depth)
    {
        for (int i = 0; i < option.Children.Count; i++)
        {
            var child = option.Children[i];
            if (child.Name == "Maximum" || child.IsHiddenOn(Options.CurrentGameMode)) continue;
            for (int indent = 1; indent < depth; indent++) sb.Append(Utils.ColorString(color, "┃"));
            sb.Append(Utils.ColorString(color, i == option.Children.Count - 1 ? "┗ " : "┣ "));
            sb.Append($"{child.GetName()}: {child.GetString()}\n");
            if (child.GetBool()) ShowChildren(child, sb, color, depth + 1);
        }
    }
}
