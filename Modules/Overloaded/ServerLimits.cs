using System;
using AmongUs.GameOptions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;

namespace TOHE.Modules;

internal static class ServerLimits
{
    private static bool arraysInitialized;
    private static bool arraysOfficial;
    private static Il2CppStructArray<int> normalMaxImpostors;
    private static Il2CppStructArray<int> normalMinPlayers;
    private static Il2CppStructArray<int> hideNSeekMaxImpostors;
    private static Il2CppStructArray<int> hideNSeekMinPlayers;
    private static Il2CppStructArray<int> privateMaxImpostors;
    private static Il2CppStructArray<int> privateMinPlayers;

    // A selected region matters before a connection; a connected region is
    // retained by ServerRegion even when a game-code lookup restores the menu.
    public static bool IsOfficial => !(AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected &&
                                       AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame) &&
                                     ServerRegion.IsOfficialConnection;
    public static int MinPlayers => 4;
    public static int MaxPlayers => IsOfficial ? 15 : 127;
    public static int MinImpostors => IsOfficial ? 1 : 0;
    public static int MaxImpostors(int players) => Math.Min(players / 2, IsOfficial ? 3 : 63);

    public static void Normalize(IGameOptions options)
    {
        if (options == null) return;
        bool official = IsOfficial;
        int players = Math.Clamp(options.MaxPlayers, 4, official ? 15 : 127);
        int impostors = Math.Clamp(options.NumImpostors, official ? 1 : 0, MaxImpostors(players));
        if (players != options.MaxPlayers) options.SetInt(Int32OptionNames.MaxPlayers, players);
        if (impostors != options.NumImpostors) options.SetInt(Int32OptionNames.NumImpostors, impostors);
        var client = AmongUsClient.Instance;
        if ((client == null || !client.AmConnected || client.AmHost) && options.GetBool(BoolOptionNames.ConfirmImpostor))
            options.SetBool(BoolOptionNames.ConfirmImpostor, false);
    }

    public static void NormalizeHideNSeekImpostors(int players, bool doSave = true, bool doSync = true)
    {
        if (Options.IsLoaded && Options.NumImpostorsHnS is IntegerOptionItem option && option.GetInt() > players / 2)
            option.SetValue(option.Rule.GetNearestIndex(players / 2), doSave, doSync);
    }

    public static void Refresh()
    {
        var manager = GameOptionsManager.Instance;
        if (manager == null) return;
        bool official = IsOfficial;
        if (!arraysInitialized)
        {
            normalMaxImpostors = NormalGameOptionsV12.MaxImpostors;
            normalMinPlayers = NormalGameOptionsV12.MinPlayers;
            hideNSeekMaxImpostors = HideNSeekGameOptionsV12.MaxImpostors;
            hideNSeekMinPlayers = HideNSeekGameOptionsV12.MinPlayers;
            privateMaxImpostors = new Il2CppStructArray<int>(128);
            privateMinPlayers = new Il2CppStructArray<int>(64);
            for (int i = 0; i < privateMaxImpostors.Length; i++) privateMaxImpostors[i] = i / 2;
            for (int i = 0; i < privateMinPlayers.Length; i++) privateMinPlayers[i] = 4;
            arraysInitialized = true;
            arraysOfficial = !official;
        }
        if (official != arraysOfficial)
        {
            NormalGameOptionsV12.MaxImpostors = official ? normalMaxImpostors : privateMaxImpostors;
            NormalGameOptionsV12.MinPlayers = official ? normalMinPlayers : privateMinPlayers;
            HideNSeekGameOptionsV12.MaxImpostors = official ? hideNSeekMaxImpostors : privateMaxImpostors;
            HideNSeekGameOptionsV12.MinPlayers = official ? hideNSeekMinPlayers : privateMinPlayers;
            arraysOfficial = official;
        }

        Normalize(manager.currentNormalGameOptions?.Cast<IGameOptions>());
        Normalize(manager.currentHideNSeekGameOptions?.Cast<IGameOptions>());
        Normalize(manager.normalGameHostOptions?.Cast<IGameOptions>());
        Normalize(manager.hideNSeekGameHostOptions?.Cast<IGameOptions>());
        Normalize(manager.CurrentGameOptions);
        Normalize(manager.GameHostOptions);
    }
}

[HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.GameHostOptions), MethodType.Setter)]
internal static class ServerLimitsHostOptionsPatch
{
    private static void Prefix([HarmonyArgument(0)] IGameOptions options)
    {
        ServerLimits.Normalize(options);
        var client = AmongUsClient.Instance;
        if ((client == null || !client.AmConnected || client.AmHost) && options.GameMode is (GameModes.HideNSeek or GameModes.SeekFools))
            ServerLimits.NormalizeHideNSeekImpostors(options.MaxPlayers);
    }
}

[HarmonyPatch(typeof(GameOptionsFactory), nameof(GameOptionsFactory.ToBytes))]
internal static class ServerLimitsSerializePatch
{
    private static void Prefix([HarmonyArgument(0)] IGameOptions options)
    {
        var client = AmongUsClient.Instance;
        if (client != null && client.AmConnected && client.AmHost) ServerLimits.Normalize(options);
    }
}

[HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.Serialize))]
internal static class ServerLimitsLogicOptionsPatch
{
    private static void Prefix(LogicOptions __instance) => ServerLimits.Normalize(__instance.currentGameOptions);
}
