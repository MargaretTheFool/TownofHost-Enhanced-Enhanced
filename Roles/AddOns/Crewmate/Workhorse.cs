using AmongUs.InnerNet.GameDataMessages;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using static TOHE.Options;

namespace TOHE.Roles.AddOns.Crewmate;

public class Workhorse : IAddon
{
    private const int Id = 23730;
    public AddonTypes Type => AddonTypes.Misc;
    private static readonly HashSet<byte> playerIdList = [];
    public static bool IsEnable = false;

    public static Color RoleColor = Utils.GetRoleColor(CustomRoles.Workhorse);

    private static OptionItem OptionAssignOnlyToCrewmate;
    private static OptionItem OptionNumLongTasks;
    private static OptionItem OptionNumShortTasks;
    private static OptionItem OptionSnitchCanBeWorkhorse;

    private static bool AssignOnlyToCrewmate;
    private static int NumLongTasks;
    private static int NumShortTasks;

    public void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Workhorse, zeroOne: true);
        OptionAssignOnlyToCrewmate = BooleanOptionItem.Create(Id + 10, "AssignOnlyToCrewmate", true, TabGroup.Addons, false).SetParent(CustomRoleSpawnChances[CustomRoles.Workhorse]);
        OptionNumLongTasks = IntegerOptionItem.Create(Id + 11, "WorkhorseNumLongTasks", new(0, 5, 1), 1, TabGroup.Addons, false).SetParent(CustomRoleSpawnChances[CustomRoles.Workhorse])
            .SetValueFormat(OptionFormat.Pieces);
        OptionNumShortTasks = IntegerOptionItem.Create(Id + 12, "WorkhorseNumShortTasks", new(0, 5, 1), 1, TabGroup.Addons, false).SetParent(CustomRoleSpawnChances[CustomRoles.Workhorse])
            .SetValueFormat(OptionFormat.Pieces);
        OptionSnitchCanBeWorkhorse = BooleanOptionItem.Create(Id + 14, "SnitchCanBeWorkhorse", false, TabGroup.Addons, false).SetParent(CustomRoleSpawnChances[CustomRoles.Workhorse]);
    }
    public void Init()
    {
        IsEnable = false;
        playerIdList.Clear();

        AssignOnlyToCrewmate = OptionAssignOnlyToCrewmate.GetBool();
        NumLongTasks = OptionNumLongTasks.GetInt();
        NumShortTasks = OptionNumShortTasks.GetInt();
    }
    public void Add(byte playerId, bool gameIsLoading = true)
    {
        AddMidGame(playerId);
    }
    public static void AddMidGame(byte playerId)
    {
        playerIdList.Add(playerId);
        IsEnable = true;
    }
    public void Remove(byte playerId)
    {
        playerIdList.Remove(playerId);

        if (!playerIdList.Any())
            IsEnable = false;
    }
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
    public static (bool, int, int) TaskData => (false, NumLongTasks, NumShortTasks);
    private static bool IsAssignTarget(PlayerControl pc)
    {
        if (!pc.IsAlive() || IsThisRole(pc.PlayerId)) return false;
        if (pc.Is(CustomRoles.Snitch) && !OptionSnitchCanBeWorkhorse.GetBool()) return false;
        if (pc.Is(CustomRoles.LazyGuy) || pc.Is(CustomRoles.Lazy)) return false;

        var taskState = pc.GetPlayerTaskState();
        if (taskState.CompletedTasksCount < taskState.AllTasksCount) return false;

        if (AssignOnlyToCrewmate)
            return pc.Is(Custom_Team.Crewmate);

        return Utils.HasTasks(pc.Data) //Player has task
            && !OverrideTasksData.AllData.ContainsKey(pc.GetCustomRole()); //Has the ability to overwrite tasks
    }
    public static bool OnAddTask(PlayerControl pc)
    {
        // Only the host chooses recipients and extends their native task list.
        if (!AmongUsClient.Instance.AmHost) return true;
        if (!CustomRoles.Workhorse.IsEnable() || playerIdList.Count >= CustomRoles.Workhorse.GetCount()) return true;
        if (!IsAssignTarget(pc)) return true;

        int originalTaskCount = pc.Data.Tasks.Count;
        var oldTaskTypes = new Il2CppStructArray<byte>(originalTaskCount);
        var oldTasks = pc.myTasks.ToArray();
        for (int i = 0; i < originalTaskCount; i++)
        {
            uint taskId = pc.Data.Tasks[i].Id;
            var task = oldTasks.First(t => t.Id == taskId && t.TryCast<NormalPlayerTask>() != null);
            oldTaskTypes[i] = (byte)task.Index;
        }

        pc.RpcSetCustomRole(CustomRoles.Workhorse);
        var taskState = pc.GetPlayerTaskState();
        pc.Data.RpcSetTasks(oldTaskTypes);
        taskState.AllTasksCount = pc.Data.Tasks.Count;
        RestoreCompletedTasks(pc, originalTaskCount);
        pc.Data.MarkDirty();
        GameData.Instance.RecomputeTaskCounts();
        Logger.Info($"Player {pc.PlayerId}: requested {NumLongTasks} long + {NumShortTasks} short, added {pc.Data.Tasks.Count - originalTaskCount}, progress {taskState.CompletedTasksCount}/{taskState.AllTasksCount}", "Workhorse");
        CustomRpcTransport.Send(CustomRPC.SyncWorkhorseTasks, writer =>
        {
            writer.Write(pc.PlayerId);
            writer.WritePacked(taskState.CompletedTasksCount);
            writer.WritePacked(taskState.AllTasksCount);
            writer.WritePacked(originalTaskCount);
        });
        CustomRpcTransport.FlushPending();
        for (uint taskId = 0; taskId < originalTaskCount; taskId++)
        {
            var message = new RpcCompleteTaskMessage(pc.NetId, taskId);
            AmongUsClient.Instance.LateBroadcastReliableMessage(message.Cast<IGameDataMessage>());
        }
        pc.SyncSettings();
        Utils.NotifyRoles(SpecifySeer: pc);

        // The original completion is already represented in the restored list.
        return false;
    }

    public static void ReceiveRPC(MessageReader reader)
    {
        byte playerId = reader.ReadByte();
        int completed = reader.ReadPackedInt32();
        int total = reader.ReadPackedInt32();
        int originalTaskCount = reader.ReadPackedInt32();
        var taskState = Main.PlayerStates[playerId].TaskState;
        taskState.CompletedTasksCount = completed;
        taskState.AllTasksCount = total;
        RestoreCompletedTasks(Utils.GetPlayerById(playerId), originalTaskCount);
    }

    private static void RestoreCompletedTasks(PlayerControl pc, int originalTaskCount)
    {
        var tasks = pc.myTasks.ToArray();
        for (int i = 0; i < originalTaskCount; i++)
        {
            var taskInfo = pc.Data.Tasks[i];
            taskInfo.Complete = true;
            tasks.First(t => t.Id == taskInfo.Id && t.TryCast<NormalPlayerTask>() != null).Complete();
        }
    }
}
