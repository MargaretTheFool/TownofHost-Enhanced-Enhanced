using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TOHE.Modules;

namespace TOHE.Roles.Core.AssignManager;

// Host-owned round state. Only final roles/add-ons travel over the existing RPCs;
// a client never needs to learn the seed (and predict other players' hidden roles).
public static class RoundAssignment
{
    public static int? NextSeed { get; private set; }
    public static int? CurrentSeed { get; private set; }
    public static RoleAssignmentRequest RoleInput { get; internal set; }
    public static AddonCandidate[] AddonInput { get; internal set; } = [];
    public static AddonAssignmentDecision[] AddonDecisions { get; internal set; } = [];
    public static byte[] PlayerIds { get; private set; } = [];

    public static void SetNextSeed(int? seed) => NextSeed = seed;

    internal static void Reset()
    {
        NextSeed = null;
        CurrentSeed = null;
        RoleInput = null;
        AddonInput = [];
        AddonDecisions = [];
        PlayerIds = [];
    }

    internal static int Begin(IEnumerable<byte> playerIds)
    {
        if (!AmongUsClient.Instance.AmHost) throw new InvalidOperationException("Only the host may assign roles.");
        CurrentSeed = NextSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        NextSeed = null; // /seed is a one-round override.
        PlayerIds = playerIds.OrderBy(id => id).ToArray();
        RoleInput = null;
        AddonInput = [];
        AddonDecisions = [];
        Logger.Info($"v{AssignmentRandom.Version}; seed={CurrentSeed}; PlayerIds=[{string.Join(",", PlayerIds)}]", "RoundAssignment");
        return CurrentSeed.Value;
    }

    internal static void SaveResult()
    {
        // Local replay evidence contains IDs/options/results only, never account identifiers.
        try
        {
            string directory = Path.Combine(DataDirectoryMigration.DataDirectory, "Assignments");
            Directory.CreateDirectory(directory);
            var record = new
            {
                Version = AssignmentRandom.Version,
                Seed = CurrentSeed,
                PlayerIds,
                Map = Utils.GetActiveMapName().ToString(),
                Mode = Options.CurrentGameMode.ToString(),
                RoleInput,
                AddonInput,
                Options = OptionItem.AllOptions.OrderBy(option => option.Id)
                    .Select(option => new { option.Id, Value = option.GetValue() }).ToArray(),
                Roles = RoleAssign.RoleResult.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value),
                Addons = PlayerIds.Where(Main.PlayerStates.ContainsKey).ToDictionary(id => id,
                    id => Main.PlayerStates[id].SubRoles.OrderBy(role => role).ToArray()),
                AddonDecisions
            };
            string path = Path.Combine(directory, "last.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception error)
        {
            Logger.Warn($"Could not save assignment replay: {error.GetType().Name}", "RoundAssignment");
            Logger.Exception(error, "RoundAssignment.SaveResult");
        }
    }
}
