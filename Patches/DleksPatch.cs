using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace TOHE.Patches;

// ShouldFlipSkeld returns a constant in this build. Native callers can inline
// that value, so load the selected HnS ship before the vanilla routine.
#if !ANDROID
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGameHost))]
#endif
public static class DleksPatch
{
    internal static void Postfix(AmongUsClient __instance, ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (!__instance.AmHost || !GameStates.IsModHost || !GameStates.IsHideNSeek ||
            GameStates.IsFreePlay || GameOptionsManager.Instance.CurrentGameOptions.MapId != 3) return;
        __result = LoadSelectedShip(__instance, __result).WrapToIl2Cpp();
    }

    private static System.Collections.IEnumerator LoadSelectedShip(AmongUsClient client, Il2CppSystem.Collections.IEnumerator original)
    {
        var generation = OnGameJoinedPatch.Generation;
        if (!ShipStatus.Instance)
        {
            client.ShipLoadingAsyncHandle = client.ShipPrefabs[3].InstantiateAsync(null, false);
            yield return client.ShipLoadingAsyncHandle;
            if (!OnGameJoinedPatch.IsCurrentSession(generation) || !client.AmHost) yield break;
            var result = client.ShipLoadingAsyncHandle.Result;
            client.ShipLoadingAsyncHandle = default;
            ShipStatus.Instance = result.GetComponent<ShipStatus>();
            client.Spawn(ShipStatus.Instance, -2, InnerNet.SpawnFlags.None);
        }
        // Preserve vanilla HnS readiness, role selection and Begin behavior.
        while (OnGameJoinedPatch.IsCurrentSession(generation) && original.MoveNext())
            yield return original.Current;
    }
}
[HarmonyPatch(typeof(GameStartManager))]
class AllMapIconsPatch
{
    // Vanilla players getting error when trying get dleks map icon
    [HarmonyPatch(nameof(GameStartManager.Start)), HarmonyPostfix]
    public static void Postfix_AllMapIcons(GameStartManager __instance)
    {
        if (__instance == null) return;

        if (GameStates.IsNormalGame && Main.NormalOptions.MapId == 3)
        {
            Main.NormalOptions.MapId = 0;
            __instance.UpdateMapImage(MapNames.Skeld);

            if (!Options.RandomMapsMode.GetBool())
                CreateOptionsPickerPatch.SetDleks = true;
        }
        else if (GameStates.IsHideNSeek && Main.HideNSeekOptions.MapId == 3)
        {
            Main.HideNSeekOptions.MapId = 0;
            __instance.UpdateMapImage(MapNames.Skeld);

            if (!Options.RandomMapsMode.GetBool())
                CreateOptionsPickerPatch.SetDleks = true;
        }

        foreach (var icon in __instance.AllMapIcons)
            if (icon.Name == MapNames.Dleks) return;
        // Clone only data: cloning GameStartManager also duplicates Start,
        // lobby controls and network handlers.
        __instance.AllMapIcons.Add(new MapIconByName
        {
            Name = MapNames.Dleks,
            MapIcon = __instance.AllMapIcons[0].MapIcon,
            MapImage = Utils.LoadSprite("TOHE.Resources.Images.DleksBanner.png", 100f),
            NameImage = Utils.LoadSprite("TOHE.Resources.Images.DleksBanner-Wordart.png", 100f)
        });
    }
}
[HarmonyPatch(typeof(StringOption), nameof(StringOption.Initialize))]
class AutoSelectDleksPatch
{
    private static void Postfix(StringOption __instance)
    {
        // Start is only a forwarding wrapper on the source build and can be
        // stripped/inlined. Initialize also runs when the native menu refreshes.
        if (__instance == null || __instance.Title != StringNames.GameMapName ||
            ModGameOptionsMenu.OptionList.ContainsKey(__instance)) return;
        var options = GameOptionsManager.Instance?.CurrentGameOptions;
        if (options == null || __instance.Values == null || options.MapId >= __instance.Values.Length) return;

        // Restore the displayed selection without UpdateValue's settings write
        // or an OnValueChanged callback. Keep its label/buttons in the same state.
        __instance.Value = options.MapId;
        __instance.oldValue = __instance.Value;
        __instance.ValueText.text = DestroyableSingleton<TranslationController>.Instance.GetString(__instance.Values[__instance.Value]);
        __instance.AdjustButtonsActiveState();
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.SetButtons))]
public static class VentSetButtonsPatch
{
    public static bool ShowButtons = false;
    // Fix arrows buttons in vent on Dleks map and "Index was outside the bounds of the array" errors
    private static bool Prefix(/*Vent __instance, */[HarmonyArgument(0)] ref bool enabled)
    {
        // if map is Dleks
        if (GameStates.DleksIsActive && Main.IntroDestroyed)
        {
            enabled = false;
            if (GameStates.IsMeeting)
                ShowButtons = false;
        }
        return true;
    }
    public static void Postfix(Vent __instance, [HarmonyArgument(0)] bool enabled)
    {
        if (!GameStates.DleksIsActive) return;
        if (enabled || !Main.IntroDestroyed) return;

        var setActive = ShowButtons || !PlayerControl.LocalPlayer.inVent && !GameStates.IsMeeting;
        switch (__instance.Id)
        {
            case 0:
            case 1:
            case 2:
            case 3:
            case 5:
            case 6:
                __instance.Buttons[0].gameObject.SetActive(setActive);
                __instance.Buttons[1].gameObject.SetActive(setActive);
                break;
            case 7:
            case 12:
            case 13:
                __instance.Buttons[0].gameObject.SetActive(setActive);
                break;
            case 4:
            case 8:
            case 9:
            case 10:
            case 11:
                __instance.Buttons[1].gameObject.SetActive(setActive);
                break;
        }
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.TryMoveToVent))]
class VentTryMoveToVentPatch
{
    // Update arrows buttons when player move to vents
    private static void Postfix(Vent __instance, [HarmonyArgument(0)] Vent otherVent)
    {
        if (__instance == null || otherVent == null || !GameStates.DleksIsActive) return;

        VentSetButtonsPatch.ShowButtons = true;
        VentSetButtonsPatch.Postfix(otherVent, false);
        VentSetButtonsPatch.ShowButtons = false;
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.UpdateArrows))]
class VentUpdateArrowsPatch
{
    // Fixes "Index was outside the bounds of the array" errors when arrows updates in vent on Dleks map
    private static bool Prefix()
    {
        // if map is not Dleks
        return !GameStates.DleksIsActive;
    }
}
