using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TOHE.Modules;
using TOHE.Roles.Core;
using TOHE.Roles.Core.AssignManager;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.CoShowIntro))]
class CoShowIntroPatch
{
#if ANDROID
    public static bool Prepare() => AndroidIntroStateMachines.ShowIntroMoveNext == null;
#endif
    public static void Postfix(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result != null) __result = Run(__result, OfficialSessionContext.Capture()).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator Run(Il2CppSystem.Collections.IEnumerator original, OfficialSessionContext context)
    {
        // Coroutine construction is not execution. Start the phase/timers on
        // its first MoveNext, immediately before native intro execution.
        try
        {
            if (!context.IsCurrent() || RoleDistribution.IsActive && !RoleDistribution.NotifyIntroStarted()) yield break;
#if ANDROID
            AndroidIntroTextFallbackPatch.MarkIntroStarted();
#endif
            GameStartProgress.Reset();
            ScheduleIntro();
            while (context.IsCurrent() && original.MoveNext()) yield return original.Current;
        }
        finally { original.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
    }

    internal static void ScheduleIntro()
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsModHost || GameStates.IsHideNSeek) return;
        var generation = OnGameJoinedPatch.Generation;
        var currentGame = GameManager.Instance;
        bool IsCurrentIntro() => OnGameJoinedPatch.IsCurrentSession(generation)
            && AmongUsClient.Instance.AmHost && currentGame != null && GameManager.Instance == currentGame
            && !GameStates.IsEnded && !GameStates.IsLobby;

        if (RoleDistribution.IsActive)
        {
            AmongUsClient.Instance.StartCoroutine(RoleDistribution.AfterRestore(() =>
            {
                if (!IsCurrentIntro() || !DestroyableSingleton<HudManager>.InstanceExists) return;
                DestroyableSingleton<HudManager>.Instance.SetHudActive(true);
                foreach (var pc in Main.AllPlayerControls) pc.SetCustomIntro();
            }).WrapToIl2Cpp());
            AmongUsClient.Instance.StartCoroutine(RoleDistribution.AfterRestore(() =>
            {
                if (!IsCurrentIntro() || ShipStatus.Instance == null) return;
                BeginTasksAndSyncSettings();
            }, tasks: true).WrapToIl2Cpp());
            return;
        }

        _ = new LateTask(() =>
        {
            if (!IsCurrentIntro() || !DestroyableSingleton<HudManager>.InstanceExists) return;

            StartGameHostPatch.RpcSetDisconnected(disconnected: false);

            DestroyableSingleton<HudManager>.Instance.SetHudActive(true);

            foreach (var pc in PlayerControl.AllPlayerControls.GetFastEnumerator())
            {
                pc.SetCustomIntro();
            }
        }, 0.6f, "Set Disconnected");

        _ = new LateTask(() =>
        {
            if (!IsCurrentIntro() || ShipStatus.Instance == null) return;
            BeginTasksAndSyncSettings();
        }, 4f, "Assing Task For All");
    }

    private static void BeginTasksAndSyncSettings()
    {
        try
        {
            // Assign tasks after all roles. Native Begin also updates local
            // cosmetics after sending tasks, so an exception can occur late.
            ShipStatus.Instance.Begin();
        }
        catch (Exception error)
        {
            Logger.Error(error.ToString(), "ShipStatus.Begin");
        }

        // Always replace the temporary zero-cooldown startup options, even if
        // task initialization failed. Cooldown repair uses these senders too.
        GameOptionsSender.AllSenders.Clear();
        foreach (var pc in Main.AllPlayerControls)
            GameOptionsSender.AllSenders.Add(new PlayerGameOptionsSender(pc));
        Utils.SyncAllSettings();
    }
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
class CoBeginPatch
{
#if ANDROID
    public static bool Prepare() => AndroidIntroStateMachines.BeginMoveNext == null;

    public static void Postfix(ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (__result != null) __result = CompleteIntro(__result, OfficialSessionContext.Capture()).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator CompleteIntro(Il2CppSystem.Collections.IEnumerator original, OfficialSessionContext context)
    {
        try
        {
            while (context.IsCurrent() && original.MoveNext()) yield return original.Current;
            if (!context.IsCurrent()) yield break;
            IntroCutsceneDestroyPatch.Prefix();
            IntroCutsceneDestroyPatch.Postfix();
            AndroidIntroTextFallbackPatch.MarkIntroCompleted();
        }
        finally { original.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
    }
#endif
    public static void Prefix()
    {
#if ANDROID
        AndroidIntroTextFallbackPatch.MarkBeginStarted();
#endif
        GameStates.InGame = true;
        RPC.RpcVersionCheck();

        FFAManager.SetData();
    }
}
#if ANDROID
// Android can inline the iterator factories. Prefer the native iterator entry
// when the runtime interop exposes it; keep the factory patches as a fallback.
internal static class AndroidIntroStateMachines
{
    internal static readonly MethodInfo ShowIntroMoveNext = FindMoveNext(typeof(HudManager), "CoShowIntro");
    internal static readonly MethodInfo BeginMoveNext = FindMoveNext(typeof(IntroCutscene), "CoBegin");

    private static MethodInfo FindMoveNext(Type owner, string name)
    {
        var type = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name.StartsWith("_" + name + "_d__", StringComparison.Ordinal));
        var method = type == null ? null : AccessTools.DeclaredMethod(type, "MoveNext");
        if (method != null && AccessTools.Property(type, "__1__state") == null) method = null;
        if (method == null) Logger.Warn($"{owner.Name}.{name}.MoveNext is unavailable; using the coroutine factory", "AndroidIntro");
        return method;
    }
}

[HarmonyPatch]
internal static class AndroidShowIntroPatch
{
    private static readonly Dictionary<IntPtr, OfficialSessionContext> Contexts = new();
    private static PropertyInfo state;

    public static bool Prepare() => AndroidIntroStateMachines.ShowIntroMoveNext != null;
    public static MethodBase TargetMethod()
    {
        var method = AndroidIntroStateMachines.ShowIntroMoveNext;
        state = AccessTools.Property(method.DeclaringType, "__1__state");
        return method;
    }

    public static bool Prefix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance, ref bool __result)
    {
        if ((int)state.GetValue(__instance) == 0)
        {
            Contexts.Clear();
            var context = OfficialSessionContext.Capture();
            if (!context.IsCurrent() || RoleDistribution.IsActive && !RoleDistribution.NotifyIntroStarted())
            {
                __result = false;
                return false;
            }
            Contexts[__instance.Pointer] = context;
            GameStartProgress.Reset();
            CoShowIntroPatch.ScheduleIntro();
        }
        if (Contexts.TryGetValue(__instance.Pointer, out var current) && current.IsCurrent()) return true;
        Contexts.Remove(__instance.Pointer);
        __result = false;
        return false;
    }

    public static void Postfix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance, bool __result)
    {
        if (!__result) Contexts.Remove(__instance.Pointer);
    }
}

[HarmonyPatch]
internal static class AndroidIntroCompletionPatch
{
    private static PropertyInfo state;

    public static bool Prepare() => AndroidIntroStateMachines.BeginMoveNext != null;
    public static MethodBase TargetMethod()
    {
        var method = AndroidIntroStateMachines.BeginMoveNext;
        state = AccessTools.Property(method.DeclaringType, "__1__state");
        return method;
    }

    public static void Prefix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance)
    {
        if ((int)state.GetValue(__instance) == 0) CoBeginPatch.Prefix();
    }

    public static void Postfix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance, bool __result)
    {
        // Project Lotus and TownOfHostNU use CoBegin state -1 for intro cleanup.
        if (__result || (int)state.GetValue(__instance) != -1 || !GameStates.IsInGame || Main.IntroDestroyed) return;
        IntroCutsceneDestroyPatch.Prefix();
        IntroCutsceneDestroyPatch.Postfix();
        AndroidIntroTextFallbackPatch.MarkIntroCompleted();
    }
}
#endif
[HarmonyPatch]
internal static class ShowRoleMoveNextPatch
{
    private static readonly Type StateMachine = typeof(IntroCutscene).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(type => type.Name.StartsWith("_ShowRole_d__", StringComparison.Ordinal));
    internal static readonly MethodInfo MoveNext = StateMachine == null ? null : AccessTools.DeclaredMethod(StateMachine, "MoveNext");
    private static readonly PropertyInfo State = StateMachine == null ? null : AccessTools.Property(StateMachine, "__1__state");
    private static readonly PropertyInfo Owner = StateMachine == null ? null : AccessTools.Property(StateMachine, "__4__this");

    public static bool Prepare() => MoveNext != null && State != null && Owner != null;
    public static MethodBase TargetMethod() => MoveNext;

    public static void Postfix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance, bool __result)
    {
        // The native iterator has just assigned and shown the vanilla text.
        if (__result && (int)State.GetValue(__instance) == 1)
            SetUpRoleTextPatch.Postfix((IntroCutscene)Owner.GetValue(__instance));
    }
}

#if !ANDROID
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.ShowRole))]
internal static class ShowRoleFactoryFallbackPatch
{
    public static bool Prepare() => !ShowRoleMoveNextPatch.Prepare();
    public static void Postfix(IntroCutscene __instance) => SetUpRoleTextPatch.Postfix(__instance, roleInitialized: false);
}
#else
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class AndroidIntroTextFallbackPatch
{
    private static IntroCutscene intro;
    private static IntroCutscene styledTeam;
    private static Color styledTeamColor;
    private static bool applied;
    private static bool introStarted, beginStarted, sawIntro, completed;
    private static OfficialSessionContext introContext;

    internal static void MarkTeamApplied(IntroCutscene cutscene)
    {
        styledTeam = cutscene;
        styledTeamColor = cutscene.TeamTitle.color;
    }
    internal static void MarkIntroStarted() => introStarted = true;
    internal static void MarkIntroCompleted() => completed = true;
    internal static void MarkBeginStarted()
    {
        beginStarted = true;
        introContext = OfficialSessionContext.Capture();
    }

    public static void Postfix(HudManager __instance)
    {
        if (!__instance.IsIntroDisplayed)
        {
            if (sawIntro && !intro && AndroidIntroStateMachines.BeginMoveNext == null) FinishIntro();
            intro = null;
            styledTeam = null;
            applied = introStarted = beginStarted = sawIntro = completed = false;
            return;
        }
        if (!introStarted && AndroidIntroStateMachines.ShowIntroMoveNext == null)
        {
            var context = OfficialSessionContext.Capture();
            if (!context.IsCurrent() || RoleDistribution.IsActive && !RoleDistribution.NotifyIntroStarted()) return;
            introStarted = true;
            GameStartProgress.Reset();
            CoShowIntroPatch.ScheduleIntro();
        }
        if (!intro)
        {
            if (sawIntro)
            {
                if (AndroidIntroStateMachines.BeginMoveNext == null) FinishIntro();
                return;
            }
            intro = __instance.GetComponentInChildren<IntroCutscene>(true);
            if (intro) sawIntro = true;
        }
        if (intro && !beginStarted && AndroidIntroStateMachines.BeginMoveNext == null) CoBeginPatch.Prefix();
        if (!intro || !intro.RoleText || !intro.YouAreText) return;
        if (GameStates.IsNormalGame && styledTeam != intro && intro.ourCrewmate &&
            !intro.RoleText.gameObject.activeInHierarchy)
            BeginCrewmatePatch.Postfix(intro);
        if (ShowRoleMoveNextPatch.Prepare() || applied ||
            !intro.RoleText.gameObject.activeInHierarchy || !intro.YouAreText.gameObject.activeInHierarchy) return;
        applied = true;
        SetUpRoleTextPatch.Postfix(intro);
    }

    private static void FinishIntro()
    {
        if (completed || !introContext.IsCurrent()) return;
        completed = true;
        if (Main.IntroDestroyed) return;
        IntroCutsceneDestroyPatch.Prefix();
        IntroCutsceneDestroyPatch.Postfix();
    }

    internal static void LateUpdate()
    {
        if (!intro || styledTeam != intro || !intro.TeamTitle || !intro.RoleText ||
            intro.RoleText.gameObject.activeInHierarchy) return;
        var color = styledTeamColor;
        color.a = intro.TeamTitle.color.a;
        intro.TeamTitle.color = color;
    }
}
#endif

class SetUpRoleTextPatch
{
    public static bool IsInIntro = false;

    public static void Postfix(IntroCutscene __instance, bool roleInitialized = true)
    {
        if (!GameStates.IsModHost) return;

        if (AmongUsClient.Instance.AmHost)
        {
            // After showing team for non-modded clients update player names.
            IsInIntro = false;
            Utils.DoNotifyRoles(NoCache: true);
        }

        if (GameStates.IsNormalGame)
        {
            foreach (var player in Main.AllPlayerControls)
            {
                Main.PlayerStates[player.PlayerId].InitTask(player);
            }

            GameData.Instance.RecomputeTaskCounts();
            TaskState.InitialTotalTasks = GameData.Instance.TotalTasks;
        }

        var mapName = Utils.GetActiveMapName();
        Logger.Msg($"{mapName}", "Map");
        if (AmongUsClient.Instance.AmHost && RandomSpawn.IsRandomSpawn() && RandomSpawn.CanSpawnInFirstRound())
        {
            RandomSpawn.SpawnMap spawnMap = mapName switch
            {
                MapNames.Skeld => new RandomSpawn.SkeldSpawnMap(),
                MapNames.MiraHQ => new RandomSpawn.MiraHQSpawnMap(),
                MapNames.Polus => new RandomSpawn.PolusSpawnMap(),
                MapNames.Dleks => new RandomSpawn.DleksSpawnMap(),
                MapNames.Fungle => new RandomSpawn.FungleSpawnMap(),
                _ => null,
            };
            if (spawnMap != null) Main.AllPlayerControls.Do(spawnMap.RandomTeleport);
        }

        if (roleInitialized) ApplyRoleText(__instance);
        else _ = new LateTask(() => ApplyRoleText(__instance), 0.0001f, "Override Role Text");

        __instance.StartCoroutine(CoLoggerGameInfo().WrapToIl2Cpp());

        // Set normal name for modded
        _ = new LateTask(() =>
        {
            // Return if game is ended or player in lobby or player is null
            if (AmongUsClient.Instance.IsGameOver || GameStates.IsLobby || PlayerControl.LocalPlayer == null) return;

            var realName = Main.AllPlayerNames[PlayerControl.LocalPlayer.PlayerId];
            // Don't use RpcSetName because the modded client needs to set the name locally
            PlayerControl.LocalPlayer.SetName(realName);

            Utils.DoNotifyRoles(NoCache: true);
        }, 1f, "Reset Name For Modded Players");
    }

    private static void ApplyRoleText(IntroCutscene intro)
    {
        PlayerControl localPlayer = PlayerControl.LocalPlayer;
        CustomRoles role = localPlayer.GetCustomRole();
        if (Options.CurrentGameMode == CustomGameMode.FFA)
        {
            var color = ColorUtility.TryParseHtmlString("#00ffff", out var c) ? c : new(255, 255, 255, 255);
            intro.YouAreText.transform.gameObject.SetActive(false);
            intro.RoleText.text = GetString("Killer");
            intro.RoleText.color = color;
            intro.RoleBlurbText.color = color;
            intro.RoleBlurbText.text = GetString("KillerInfo");
        }
        else
        {
            if (!role.IsVanilla())
            {
                intro.YouAreText.color = Utils.GetRoleColor(role);
                intro.RoleText.text = Utils.GetRoleName(role);
                intro.RoleText.color = Utils.GetRoleColor(role);
                intro.RoleBlurbText.color = Utils.GetRoleColor(role);
                intro.RoleBlurbText.text = localPlayer.GetRoleInfo();
            }

            foreach (var subRole in Main.PlayerStates[localPlayer.PlayerId].SubRoles.ToArray())
                intro.RoleBlurbText.text += "\n" + Utils.ColorString(Utils.GetRoleColor(subRole), GetString($"{subRole}Info"));

            intro.RoleText.text += Utils.GetSubRolesText(localPlayer.PlayerId, false, true);
        }
    }
    private static byte[] EncryptDES(byte[] data, string key)
    {
        using SymmetricAlgorithm desAlg = DES.Create();

        // Incoming key must be 8 bit or will cause error
        desAlg.Key = Encoding.UTF8.GetBytes(key);
        desAlg.IV = Encoding.UTF8.GetBytes(key);

        using MemoryStream msEncrypt = new();
        using (CryptoStream csEncrypt = new(msEncrypt, desAlg.CreateEncryptor(), CryptoStreamMode.Write))
        {
            csEncrypt.Write(data, 0, data.Length);
        }
        return msEncrypt.ToArray();
    }
    private static System.Collections.IEnumerator CoLoggerGameInfo()
    {
        var allPlayerControlsArray = Main.AllPlayerControls;
        var sb = new StringBuilder();

        sb.Append("------------Client Options------------\n");
        sb.Append($"Game Master: {Main.EnableGM.Value}\n");
        sb.Append($"UnlockFPS: {Main.UnlockFPS.Value}\n");
        sb.Append($"Show FPS: {Main.ShowFPS.Value}\n");
        sb.Append($"Auto Start: {Main.AutoStart.Value}\n");
        sb.Append($"Dark Theme: {Main.DarkTheme.Value}\n");
        sb.Append($"Disable Lobby Music: {Main.DisableLobbyMusic.Value}\n");
        sb.Append($"Show Text Overlay: {Main.ShowTextOverlay.Value}\n");
        sb.Append($"Horse Mode: {Main.HorseMode.Value}\n");
        sb.Append($"Enable Custom Button: {Main.EnableCustomButton.Value}\n");
        sb.Append($"Enable Custom Sound Effect: {Main.EnableCustomSoundEffect.Value}\n");
        sb.Append($"Force Own Language: {Main.ForceOwnLanguage.Value}\n");
        sb.Append($"Force Own Language Role Name: {Main.ForceOwnLanguageRoleName.Value}\n");
        sb.Append($"Version Cheat: {Main.VersionCheat.Value}\n");
        sb.Append($"God Mode: {Main.GodMode.Value}\n");
        sb.Append($"Auto Rehost: {Main.AutoRehost.Value}\n");

        sb.Append("------------Player Names------------\n");
        foreach (var pc in allPlayerControlsArray)
        {
            if (pc == null) continue;
            sb.Append($"{(pc.AmOwner ? "[*]" : string.Empty),-3}{pc.PlayerId,-2}:{pc.name.PadRightV2(20)}:{Main.AllPlayerNames[pc.PlayerId]}({Palette.ColorNames[pc.Data.DefaultOutfit.ColorId].ToString().Replace("Color", string.Empty)})\n");
            pc.cosmetics.nameText.text = pc.name;
        }

        yield return null;

        sb.Append("------------Roles / Add-ons------------\n");
        if (PlayerControl.LocalPlayer.FriendCode.GetDevUser().DeBug || GameStates.IsLocalGame)
        {
            foreach (var pc in allPlayerControlsArray)
            {
                if (pc == null) continue;
                sb.Append($"{(pc.AmOwner ? "[*]" : string.Empty),-3}{pc.PlayerId,-2}:{Main.AllPlayerNames[pc.PlayerId].PadRightV2(20)}:{pc.GetAllRoleName().RemoveHtmlTags().Replace("\n", " + ")}\n");
            }
        }
        else
        {
            StringBuilder logStringBuilder = new();
            logStringBuilder.AppendLine("------------Roles / Add-ons------------");

            foreach (var pc in allPlayerControlsArray)
            {
                logStringBuilder.AppendLine($"{(pc.AmOwner ? "[*]" : string.Empty),-3}{pc.PlayerId,-2}:{pc?.Data?.PlayerName?.PadRight(20)}:{pc.GetAllRoleName().RemoveHtmlTags()}");
            }

            try
            {
                byte[] logBytes = Encoding.UTF8.GetBytes(logStringBuilder.ToString());
                byte[] encryptedBytes = EncryptDES(logBytes, $"TOHE{PlayerControl.LocalPlayer.PlayerId}00000000"[..8]);
                string encryptedString = Convert.ToBase64String(encryptedBytes);
                sb.Append(encryptedString + "\n");
            }
            catch (Exception ex)
            {
                Logger.Error($"Encryption error: {ex}", "SetUpRoleTextPatch.CoLoggerGameInfo");
            }
        }
        //https://www.toolhelper.cn/SymmetricEncryption/DES
        //mode CBC, PKCS7, 64bit, Key = IV= "TOHE" + playerid + 000/00 "to 8 bits

        yield return null;

        sb.Append("------------Player Platforms------------\n");
        foreach (var pc in allPlayerControlsArray)
        {
            try
            {
                var text = new StringBuilder();
                sb.Append(pc.AmOwner ? "[*]" : "   ");
                sb.Append($"{pc.PlayerId,-2}:{pc.Data?.PlayerName?.PadRightV2(20)}:{pc.GetClient()?.PlatformData?.Platform.ToString()?.Replace("Standalone", string.Empty),-11}");

                if (Main.playerVersion.TryGetValue(pc.GetClientId(), out PlayerVersion pv))
                {
                    sb.Append($":Mod({pv.forkId}/{pv.version}:{pv.tag}), ClientId :{pc.GetClientId()}");
                }
                else
                {
                    sb.Append($":Vanilla, ClientId :{pc.GetClientId()}");
                }
                sb.Append(text + "\n");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Intro.Platform");
            }
        }

        yield return null;

        sb.Append("------------Vanilla Settings------------\n");
        var tmp = GameOptionsManager.Instance.CurrentGameOptions.ToHudString(GameData.Instance ? GameData.Instance.PlayerCount : 10).Split("\r\n").Skip(1).ToArray();
        foreach (var text in tmp)
        {
            sb.Append(text + "\n");
        }

        yield return null;

        sb.Append("------------Modded Settings------------\n");
        foreach (OptionItem o in OptionItem.AllOptions)
        {
            if (!o.IsHiddenOn(Options.CurrentGameMode) && (o.Parent?.GetBool() ?? !o.GetString().Equals("0%")))
                sb.Append($"{(o.Parent == null ? o.GetName(true, true).RemoveHtmlTags().PadRightV2(40) : $"┗ {o.GetName(true, true).RemoveHtmlTags()}".PadRightV2(41))}:{o.GetString().RemoveHtmlTags()}\n");
        }

        yield return null;

        sb.Append("-------------Other Information-------------\n");
        sb.Append($"Number players: {allPlayerControlsArray.Length}");

        yield return null;

        Logger.Info(sb.ToString(), "GameInfo", multiLine: true);
    }
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate))]
class BeginCrewmatePatch
{
    public static bool Prefix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> teamToDisplay)
    {
        var role = PlayerControl.LocalPlayer.GetCustomRole();

        if (role.IsMadmate() || PlayerControl.LocalPlayer.Is(CustomRoles.Madmate))
        {
            teamToDisplay = new Il2CppSystem.Collections.Generic.List<PlayerControl>();
            teamToDisplay.Add(PlayerControl.LocalPlayer);
            __instance.BeginImpostor(teamToDisplay);
#if !ANDROID
            __instance.overlayHandle.color = Palette.ImpostorRed;
#endif
            return false;
        }
        else if (PlayerControl.LocalPlayer.IsNeutralApocalypse())
        {
            var apocTeam = new Il2CppSystem.Collections.Generic.List<PlayerControl>();
            apocTeam.Add(PlayerControl.LocalPlayer);
            foreach (var pc in Main.AllAlivePlayerControls)
            {
                if (pc.IsNeutralApocalypse() && pc != PlayerControl.LocalPlayer)
                    apocTeam.Add(pc);
            }
            teamToDisplay = apocTeam;
        }
        else if (PlayerControl.LocalPlayer.Is(Custom_Team.Neutral))
        {
            teamToDisplay = new Il2CppSystem.Collections.Generic.List<PlayerControl>();
            teamToDisplay.Add(PlayerControl.LocalPlayer);
        }

        if (PlayerControl.LocalPlayer.GetRoleClass() is Executioner ex)
        {
            var exeTeam = new Il2CppSystem.Collections.Generic.List<PlayerControl>();
            exeTeam.Add(PlayerControl.LocalPlayer);

            PlayerControl executing = ex.GetTargetId().GetPlayer();
            if (executing != null)
                exeTeam.Add(executing);

            teamToDisplay = exeTeam;
        }
        if (PlayerControl.LocalPlayer.GetRoleClass() is Lawyer lw)
        {
            var lawyerTeam = new Il2CppSystem.Collections.Generic.List<PlayerControl>();
            lawyerTeam.Add(PlayerControl.LocalPlayer);

            PlayerControl lawyerTarget = lw.GetTargetId().GetPlayer();
            if (lawyerTarget != null)
                lawyerTeam.Add(lawyerTarget);

            teamToDisplay = lawyerTeam;
        }

        return true;
    }
    public static void Postfix(IntroCutscene __instance)
    {
        CustomRoles role = PlayerControl.LocalPlayer.GetCustomRole();

        __instance.ImpostorText.gameObject.SetActive(false);

        switch (role.GetCustomRoleTeam())
        {
            case Custom_Team.Impostor:
                __instance.TeamTitle.text = GetString("TeamImpostor");
                __instance.TeamTitle.color = __instance.BackgroundBar.material.color = new Color32(255, 25, 25, byte.MaxValue);
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Impostor);
                __instance.ImpostorText.gameObject.SetActive(true);
                __instance.ImpostorText.text = GetString("SubText.Impostor");
                break;
            case Custom_Team.Crewmate:
                __instance.TeamTitle.text = GetString("TeamCrewmate");
                __instance.TeamTitle.color = __instance.BackgroundBar.material.color = new Color32(140, 255, 255, byte.MaxValue);
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Crewmate);
                __instance.ImpostorText.gameObject.SetActive(true);
                __instance.ImpostorText.text = GetString("SubText.Crewmate");
                break;
            case Custom_Team.Neutral:
                __instance.TeamTitle.text = GetString("TeamNeutral");
                __instance.TeamTitle.color = __instance.BackgroundBar.material.color = new Color32(127, 140, 141, byte.MaxValue);
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Shapeshifter);
                __instance.ImpostorText.gameObject.SetActive(true);
                __instance.ImpostorText.text = GetString("SubText.Neutral");
                break;
        }
        switch (role)
        {
            case CustomRoles.ShapeMaster:
            case CustomRoles.ShapeshifterTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Shapeshifter);
                break;
            case CustomRoles.PhantomTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Phantom);
                break;
            case CustomRoles.TrackerTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Tracker);
                break;
            case CustomRoles.DetectiveTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Detective);
                break;
            case CustomRoles.ViperTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Viper);
                break;
            case CustomRoles.NoisemakerTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Noisemaker);
                break;
            case CustomRoles.EngineerTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Engineer);
                break;
            case CustomRoles.Doctor:
            case CustomRoles.Medic:
            case CustomRoles.ScientistTOHE:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Scientist);
                break;

            case CustomRoles.Terrorist:
            case CustomRoles.Bomber:
                var sound = ShipStatus.Instance.CommonTasks.FirstOrDefault(task => task.TaskType == TaskTypes.FixWiring)
                .MinigamePrefab.OpenSound;
                PlayerControl.LocalPlayer.Data.Role.IntroSound = sound;
                break;

            case CustomRoles.Workaholic:
            case CustomRoles.Snitch:
            case CustomRoles.TaskManager:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = DestroyableSingleton<HudManager>.Instance.TaskCompleteSound;
                break;

            case CustomRoles.Opportunist:
            case CustomRoles.Hater:
            case CustomRoles.Revolutionist:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Crewmate);
                break;

            case CustomRoles.Addict:
            case CustomRoles.Ventguard:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = ShipStatus.Instance.VentEnterSound;
                break;

            case CustomRoles.Saboteur:
            case CustomRoles.Inhibitor:
            case CustomRoles.Mechanic:
            case CustomRoles.Provocateur:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = ShipStatus.Instance.SabotageSound;
                break;

            case CustomRoles.GM:
                __instance.TeamTitle.text = Utils.GetRoleName(role);
                __instance.TeamTitle.color = Utils.GetRoleColor(role);
                __instance.BackgroundBar.material.color = Utils.GetRoleColor(role);
                __instance.ImpostorText.gameObject.SetActive(false);
                PlayerControl.LocalPlayer.Data.Role.IntroSound = DestroyableSingleton<HudManager>.Instance.TaskCompleteSound;
                break;

            case CustomRoles.Sheriff:
            case CustomRoles.Veteran:
            case CustomRoles.Knight:
            case CustomRoles.KillingMachine:
            case CustomRoles.Reverie:
            case CustomRoles.NiceGuesser:
            case CustomRoles.Vigilante:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = PlayerControl.LocalPlayer.KillSfx;
                break;
            case CustomRoles.Swooper:
            case CustomRoles.Wraith:
            case CustomRoles.Chameleon:
                PlayerControl.LocalPlayer.Data.Role.IntroSound = PlayerControl.LocalPlayer.MyPhysics.ImpostorDiscoveredSound;
                break;
        }

        if (PlayerControl.LocalPlayer.Is(CustomRoles.Madmate) || role.IsMadmate())
        {
            __instance.TeamTitle.text = GetString("TeamMadmate");
            __instance.TeamTitle.color = __instance.BackgroundBar.material.color = new Color32(255, 25, 25, byte.MaxValue);
            PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Impostor);
            __instance.ImpostorText.gameObject.SetActive(true);
            __instance.ImpostorText.text = GetString("SubText.Madmate");
        }

        if (Options.CurrentGameMode == CustomGameMode.FFA)
        {
            __instance.TeamTitle.text = GetString("Killer");
            __instance.TeamTitle.color = __instance.BackgroundBar.material.color = new Color32(0, 255, 255, byte.MaxValue);
            PlayerControl.LocalPlayer.Data.Role.IntroSound = GetIntroSound(RoleTypes.Shapeshifter);
            __instance.ImpostorText.gameObject.SetActive(true);
            __instance.ImpostorText.text = GetString("KillerInfo");
        }

        // I hope no one notices this in code
        if (Input.GetKey(KeyCode.RightShift))
        {
            __instance.TeamTitle.text = "Damn!!";
            __instance.ImpostorText.gameObject.SetActive(true);
            __instance.ImpostorText.text = "You Found The Secret Intro";
            __instance.TeamTitle.color = new Color32(186, 3, 175, byte.MaxValue);
            StartFadeIntro(__instance, Color.yellow, Color.cyan);
        }
        if (Input.GetKey(KeyCode.RightControl))
        {
            __instance.TeamTitle.text = "Warning!";
            __instance.ImpostorText.gameObject.SetActive(true);
            __instance.ImpostorText.text = "Please stay away from all impostor based players";
            __instance.TeamTitle.color = new Color32(241, 187, 2, byte.MaxValue);
            StartFadeIntro(__instance, new Color32(241, 187, 2, byte.MaxValue), Color.red);
        }
#if ANDROID
        AndroidIntroTextFallbackPatch.MarkTeamApplied(__instance);
#endif
    }
    public static AudioClip GetIntroSound(RoleTypes roleType)
    {
        return RoleManager.Instance.GetRole(roleType)?.IntroSound;
    }
    private static async void StartFadeIntro(IntroCutscene __instance, Color start, Color end)
    {
        await Task.Delay(1000);
        int milliseconds = 0;
        while (true)
        {
            await Task.Delay(20);
            milliseconds += 20;
            float time = milliseconds / (float)500;
            Color LerpingColor = Color.Lerp(start, end, time);
            if (__instance == null || milliseconds > 500)
            {
                Logger.Info("Terminates the loop", "StartFadeIntro");
                break;
            }
            __instance.BackgroundBar.material.color = LerpingColor;
        }
    }
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor))]
class BeginImpostorPatch
{
    public static bool Prefix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> yourTeam)
    {
        var role = PlayerControl.LocalPlayer.GetCustomRole();

        if (role.IsMadmate() || PlayerControl.LocalPlayer.Is(CustomRoles.Madmate))
        {
            yourTeam = new();
            yourTeam.Add(PlayerControl.LocalPlayer);
#if !ANDROID
            __instance.overlayHandle.color = Palette.ImpostorRed;
#endif
            return true;
        }

        if (role.IsCrewmate() && role.GetDYRole() == RoleTypes.Impostor)
        {
            yourTeam = new();
            yourTeam.Add(PlayerControl.LocalPlayer);
            foreach (var pc in Main.AllPlayerControls.Where(x => !x.AmOwner)) yourTeam.Add(pc);
            __instance.BeginCrewmate(yourTeam);
#if !ANDROID
            __instance.overlayHandle.color = Palette.CrewmateBlue;
#endif
            return false;
        }

        if (role.IsNeutral())
        {
            yourTeam = new();
            yourTeam.Add(PlayerControl.LocalPlayer);
            foreach (var pc in Main.AllPlayerControls.Where(x => !x.AmOwner)) yourTeam.Add(pc);
            __instance.BeginCrewmate(yourTeam);
#if !ANDROID
            __instance.overlayHandle.color = new Color32(127, 140, 141, byte.MaxValue);
#endif
            return false;
        }

        BeginCrewmatePatch.Prefix(__instance, ref yourTeam);
        return true;
    }

    public static void Postfix(IntroCutscene __instance)
    {
        BeginCrewmatePatch.Postfix(__instance);
    }
}
#if !ANDROID
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
#endif
class IntroCutsceneDestroyPatch
{
    public static void Prefix()
    {
        if (RoleDistribution.IsActive && !RoleDistribution.RestoreComplete)
        {
            AmongUsClient.Instance.StartCoroutine(RoleDistribution.AfterRestore(Prefix).WrapToIl2Cpp());
            return;
        }
        if (AmongUsClient.Instance.AmHost && !AmongUsClient.Instance.IsGameOver)
        {
            // Host is desync role
            if (PlayerControl.LocalPlayer.HasDesyncRole())
            {
                PlayerControl.LocalPlayer.Data.Role.AffectedByLightAffectors = false;

                foreach (var target in PlayerControl.AllPlayerControls.GetFastEnumerator())
                {
                    // Set all players as killable players
                    target.Data.Role.CanBeKilled = true;

                    // When target is impostor, set name color as white
                    target.cosmetics.SetNameColor(Color.white);
                    target.Data.Role.NameColor = Color.white;
                }
            }

            if (Main.UnShapeShifter.Any())
            {
                var generation = OnGameJoinedPatch.Generation;
                var client = AmongUsClient.Instance;
                var gameId = client.GameId;
                var hostId = client.HostId;
                var currentGame = GameManager.Instance;
                var playerStates = Main.PlayerStates;
                var unShapeShifters = Main.UnShapeShifter.Select(id =>
                {
                    var pc = id.GetPlayer();
                    return (Id: id, Player: pc, Role: pc == null ? null : pc.GetRoleClass());
                }).ToArray();
                _ = new LateTask(() =>
                {
                    if (!OnGameJoinedPatch.IsCurrentSession(generation) || AmongUsClient.Instance != client
                        || !client.AmHost || client.HostId != hostId || client.GameId != gameId
                        || client.IsGameOver || GameStates.IsLobby || !GameStates.IsInGame
                        || currentGame == null || GameManager.Instance != currentGame
                        || Main.PlayerStates != playerStates) return;

                    foreach (var captured in unShapeShifters)
                    {
                        var id = captured.Id;
                        if (!Main.UnShapeShifter.Contains(id)) continue;
                        var PC = id.GetPlayer();
                        var role = PC == null ? null : PC.GetRoleClass();
                        // The ID may now belong to a new player or role registration.
                        // An old callback must not remove that registration either.
                        if (PC != null && (PC != captured.Player || role != captured.Role)) continue;
                        if (PC == null || PC.Data == null || PC.Data.Disconnected || role == null
                            || !Utils.IsMethodOverridden(role, "UnShapeShiftButton"))
                        {
                            Main.UnShapeShifter.Remove(id);
                            continue;
                        }
                        var firstPlayer = Main.AllPlayerControls.FirstOrDefault(x => x != PC
                            && x.Data != null && !x.Data.Disconnected);
                        if (firstPlayer == null) continue;
                        PC.RpcShapeshift(firstPlayer, false);
                        PC.RpcRejectShapeshift();
                        PC.ResetPlayerOutfit(force: true);
                        Main.CheckShapeshift[id] = false;
                    }
                    Main.GameIsLoaded = true;
                }, 3f, "Set UnShapeShift Button");
            }
        }
    }
    public static void Postfix()
    {
        if (!GameStates.IsInGame) return;

        Main.IntroDestroyed = true;

        // Set roleAssigned as false for override role for modded players
        // For override role for vanilla clients we use "Data.Disconnected" while assign
        Main.AllPlayerControls.Do(pc => pc.roleAssigned = false);

        if (RoleDistribution.IsActive && !RoleDistribution.RestoreComplete)
        {
            AmongUsClient.Instance.StartCoroutine(RoleDistribution.AfterRestore(AfterIntroDestroyed).WrapToIl2Cpp());
            return;
        }
        AfterIntroDestroyed();
    }

    private static void AfterIntroDestroyed()
    {
        var compatible = OfficialAnticheatPolicy.Enabled;
        var context = OfficialSessionContext.Capture();
        if (!GameStates.AirshipIsActive)
        {
            foreach (var state in Main.PlayerStates.Values)
            {
                state.HasSpawned = true;
            }
        }

        CustomRoleManager.Add();

        if (AmongUsClient.Instance.AmHost)
        {
            if (GameStates.IsNormalGame && !GameStates.AirshipIsActive)
            {
                foreach (var pc in PlayerControl.AllPlayerControls.GetFastEnumerator())
                {
                    pc.RpcResetAbilityCooldown();

                    if (Options.FixFirstKillCooldown.GetBool() && Options.CurrentGameMode != CustomGameMode.FFA)
                    {
                        _ = new LateTask(() =>
                        {
                            if (compatible && (!context.IsCurrent() || !RoleDistribution.GameplayReady
                                || !AmongUsClient.Instance.AmHost || pc == null || pc.Data == null
                                || pc.Data.Disconnected || !Main.AllPlayerControls.Contains(pc))) return;
                            if (pc != null)
                            {
                                pc.ResetKillCooldown();

                                if (Main.AllPlayerKillCooldown.TryGetValue(pc.PlayerId, out var killTimer) && (killTimer - 2f) > 0f)
                                {
                                    var firstCooldown = Options.ChangeFirstKillCooldown.GetBool()
                                        ? Options.FixKillCooldownValue.GetFloat()
                                        : killTimer;
                                    pc.SetKillCooldown(Math.Max(0f, firstCooldown - 2f));
                                }
                            }
                        }, 2f, $"Fix Kill Cooldown Task for playerId {pc.PlayerId}");
                    }
                }
            }

            foreach (var gm in Main.AllPlayerControls.Where(player => player && player.Data != null && !player.Data.Disconnected && player.Is(CustomRoles.GM)))
            {
                gm.RpcExile();
                Main.PlayerStates[gm.PlayerId].SetDead();
            }
            if (GhostRoleAssign.forceRole.Any())
            {
                var forcedGhosts = GhostRoleAssign.forceRole.Keys.Select(id =>
                {
                    var player = id.GetPlayer();
                    return (Id: id, Pointer: player == null ? IntPtr.Zero : player.Pointer);
                }).ToArray();
                // Needs to be delayed for the game to load it properly
                _ = new LateTask(() =>
                {
                    if (compatible && (!context.IsCurrent() || !RoleDistribution.GameplayReady
                        || !AmongUsClient.Instance.AmHost)) return;
                    GhostRoleAssign.forceRole.Do(x =>
                    {
                        var plr = x.Key.GetPlayer();
                        if (compatible && (plr == null || plr.Data == null || plr.Data.Disconnected
                            || !forcedGhosts.Any(captured => captured.Id == x.Key && captured.Pointer == plr.Pointer))) return;
                        plr.RpcExile();
                        Main.PlayerStates[x.Key].SetDead();

                    });
                }, 3f, "Set Dev Ghost-Roles");
            }

            bool chatVisible = Options.CurrentGameMode switch
            {
                CustomGameMode.FFA => true,
                _ => false
            };
            try
            {
                if (chatVisible) Utils.SetChatVisibleForEveryone();
            }
            catch (Exception error)
            {
                Logger.Error($"Error: {error}", "FFA chat visible");
            }

            Utils.CheckAndSetVentInteractions();
        }

        // ShowRole's iterator factory can be inlined by IL2CPP. The native intro
        // has now ended and any start mask has been restored, so release the
        // name-update gate even if the ShowRole postfix did not run.
        if (AmongUsClient.Instance.AmHost) SetUpRoleTextPatch.IsInIntro = false;
        if (AmongUsClient.Instance.AmHost && GameStates.IsHideNSeek && PlayerIdentifiers.Shows(false, false))
            PlayerIdentifiers.RefreshLobby(hideAndSeek: true);
        Utils.DoNotifyRoles(NoCache: true);
        Logger.Info("OnDestroy", "IntroCutscene");
    }
}
