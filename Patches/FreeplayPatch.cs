using AmongUs.GameOptions;
using System;
using TOHE.Modules;
using TOHE.Patches;
using TOHE.Roles.Core;
using UnityEngine;

namespace TOHE;

// TutorialManager.RunTutorial owns native role and task allocation. Initialize the
// mod only after its first ShipStatus.Begin, without running multiplayer startup.
internal static class FreeplayInitialization
{
    private static TutorialManager tutorial;
    private static ShipStatus ship;
    public static bool IsReady => GameStates.IsFreePlay && tutorial != null && ship != null
        && ship == ShipStatus.Instance && Main.GameIsLoaded;

    public static void Reset(TutorialManager owner)
    {
        tutorial = owner;
        ship = null;
        Main.GameIsLoaded = false;
        Main.IntroDestroyed = false;
        Main.RealOptionsData = null;
        Main.PlayerStates.Clear();
        GameStates.InGame = false;
        GameEndCheckerForNormal.ShouldNotCheck = true;
    }

    public static void Initialize(ShipStatus owner)
    {
        if (!GameStates.IsFreePlay || tutorial == null || owner == null || ship == owner) return;
        var options = GameOptionsManager.Instance.CurrentGameOptions;
        Main.RealOptionsData = new OptionBackupData(options);
        AURoleOptions.SetOpt(options);
        Main.DefaultCrewmateVision = options.GetFloat(FloatOptionNames.CrewLightMod);
        Main.DefaultImpostorVision = options.GetFloat(FloatOptionNames.ImpostorLightMod);
        Options.DefaultKillCooldown = options.GetFloat(FloatOptionNames.KillCooldown);
        Main.PlayerStates.Clear();
        Main.AllPlayerKillCooldown.Clear();
        Main.AllPlayerSpeed.Clear();
        Main.AllPlayerCustomRoles.Clear();
        Main.AllPlayerNames.Clear();
        Main.PlayerColors.Clear();
        Main.LastNotifyNames.Clear();
        Main.LastEnteredVent.Clear();
        Main.LastEnteredVentLocation.Clear();
        Main.DesyncPlayerList.Clear();
        Main.TasklessCrewmate.Clear();
        Main.UnreportableBodies.Clear();
        Main.MurderedThisRound.Clear();
        CheckMurderPatch.LastKillAttemptTime.Clear();
        Main.FirstDied = "";
        Main.FirstDiedPrevious = "";
        Main.PlayersDiedInMeeting.Clear();
        Main.AfterMeetingDeathPlayers.Clear();
        Main.CheckShapeshift.Clear();
        Main.ShapeshiftTarget.Clear();
        Main.UnShapeShifter.Clear();
        Main.AllKillers.Clear();
        Main.OverDeadPlayerList.Clear();
        Main.OvverideOutfit.Clear();
        Main.clientIdList.Clear();
        Main.MeetingIsStarted = false;
        Main.MeetingsPassed = 0;
        Main.RefixCooldownDelay = 0;
        Main.VisibleTasksCount = true;
        Main.DoBlockNameChange = false;
        GameStates.AlreadyDied = false;
        GameEndCheckerForNormal.GameIsEnded = false;
        GameEndCheckerForNormal.predicate = null;
        ReportDeadBodyPatch.CanReport.Clear();
        ReportDeadBodyPatch.WaitReport.Clear();
        VentSystemDeterioratePatch.LastClosestVent.Clear();
        KillTimerManager.Initializate();
        CustomRoleManager.Initialize();
        Camouflage.Init();
        TargetArrow.Init();
        LocateArrow.Init();
        DoubleTrigger.Init();
        NameNotifyManager.Reset();
        CustomWinnerHolder.Reset();
        MeetingTimeManager.Init();
        foreach (var role in CustomRoleManager.RoleClass.Values) role.OnInit();
        foreach (var addon in CustomRoleManager.AddonClasses.Values) addon.Init();

        foreach (var player in Main.AllPlayerControls)
        {
            if (player == null || player.Data == null) continue;
            var outfit = player.Data.DefaultOutfit;
            Main.AllPlayerNames[player.PlayerId] = outfit.PlayerName;
            Main.PlayerColors[player.PlayerId] = outfit.ColorId >= 0 && outfit.ColorId < Palette.PlayerColors.Length
                ? Palette.PlayerColors[outfit.ColorId] : Color.white;
            var savedOutfit = new NetworkedPlayerInfo.PlayerOutfit().Set(outfit.PlayerName,
                outfit.ColorId, outfit.HatId, outfit.SkinId, outfit.VisorId, outfit.PetId, outfit.NamePlateId);
            var state = new PlayerState(player.PlayerId) { NormalOutfit = savedOutfit, HasSpawned = true };
            Main.PlayerStates[player.PlayerId] = state;
            state.SetMainRole(CustomRoles.Crewmate);
            state.RoleClass.OnAdd(player.PlayerId);
            state.InitTask(player);
            Main.AllPlayerKillCooldown[player.PlayerId] = Options.DefaultKillCooldown;
            Main.AllPlayerSpeed[player.PlayerId] = options.GetFloat(FloatOptionNames.PlayerSpeedMod);
            ReportDeadBodyPatch.CanReport[player.PlayerId] = true;
            ReportDeadBodyPatch.WaitReport[player.PlayerId] = [];
            VentSystemDeterioratePatch.LastClosestVent[player.PlayerId] = 0;
            CustomRoleManager.BlockedVentsList[player.PlayerId] = [];
            CustomRoleManager.DoNotUnlockVentsList[player.PlayerId] = [];
            Camouflage.PlayerSkins[player.PlayerId] = savedOutfit;
            Main.CheckShapeshift[player.PlayerId] = false;
            foreach (var seer in Main.AllPlayerControls)
                if (seer != null) Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = outfit.PlayerName;
        }
        CustomRoleManager.Add();
        TaskState.InitialTotalTasks = GameData.Instance.TotalTasks;
        MeetingStates.MeetingCalled = false;
        MeetingStates.FirstMeeting = true;
        Main.IntroDestroyed = true;
        GameStates.InGame = true;
        ship = owner;
        Main.GameIsLoaded = true;
        Logger.Info($"Initialized offline FreePlay state for {Main.PlayerStates.Count} players", "FreePlay");
    }

    public static void ChangeRole(PlayerControl player, CustomRoles role, bool setNativeRole)
    {
        if (!IsReady || player == null || !Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return;
        if (state.MainRole == role) return;
        var previousRole = state.MainRole;
        state.RoleClass.OnRemove(player.PlayerId);
        Main.DesyncPlayerList.Remove(player.PlayerId);
        Main.UnShapeShifter.Remove(player.PlayerId);
        Main.CheckShapeshift[player.PlayerId] = false;
        Main.ShapeshiftTarget.Remove(player.PlayerId);
        DoubleTrigger.PlayerIdList.Remove(player.PlayerId);
        DoubleTrigger.FirstTriggerTimer.Remove(player.PlayerId);
        DoubleTrigger.FirstTriggerTarget.Remove(player.PlayerId);
        DoubleTrigger.FirstTriggerAction.Remove(player.PlayerId);
        state.SetMainRole(role);
        if (!Main.PlayerStates.Values.Any(other => other.MainRole == previousRole))
            previousRole.GetStaticRoleClass().OnInit();
        if (setNativeRole) player.RpcSetRole(role.GetRoleTypes(), true);
        state.RoleClass.OnAdd(player.PlayerId);
        state.InitTask(player);
        CustomRoleManager.Add();
        // The native HUD rebuilds task text on its next dirty tick. A role switch
        // must also invalidate that text even when the native tasks stay the same.
        if (player.AmOwner && HudManager.Instance != null)
            HudManager.Instance.taskDirtyTimer = 0.25f;
    }

    public static void Release(TutorialManager owner)
    {
        if (tutorial != owner) return;
        tutorial = null;
        ship = null;
        if (!GameStates.IsFreePlay) return;
        Main.GameIsLoaded = false;
        Main.IntroDestroyed = false;
        Main.RealOptionsData = null;
        GameStates.InGame = false;
        GameEndCheckerForNormal.ShouldNotCheck = false;
    }
}

[HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.Awake))]
internal static class FreeplayTutorialStartPatch
{
    public static void Prefix(TutorialManager __instance)
    {
        if (GameStates.IsFreePlay) FreeplayInitialization.Reset(__instance);
    }
}

[HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.OnDestroy))]
internal static class FreeplayTutorialClosePatch
{
    public static void Prefix(TutorialManager __instance) => FreeplayInitialization.Release(__instance);
}

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
internal static class FreeplayShipBeginPatch
{
    public static void Postfix(ShipStatus __instance)
    {
        if (!GameStates.IsFreePlay) return;
        try { FreeplayInitialization.Initialize(__instance); }
        catch (Exception error) { Logger.Error(error.ToString(), "FreePlay.Initialize"); }
    }
}
