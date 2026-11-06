using System;
using System.IO;

namespace TOHE.Modules;

// Host-local appearance tags. The first '=' separates the friend code from the
// formatted tag, which may itself contain '=' in a color attribute.
internal static class PrivateTagManager
{
    private static string TagFile => Path.Combine(DataDirectoryMigration.DataDirectory, "tags.txt");
    private static Dictionary<string, string> tags = new(StringComparer.Ordinal);

    internal static void Reset() => tags = new(StringComparer.Ordinal);

    internal static string GetSnapshot()
    {
        var file = new FileInfo(TagFile);
        return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : string.Empty;
    }

    internal static bool Reload(out string error)
    {
        error = string.Empty;
        var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(TagFile))
            {
                foreach (string line in File.ReadLines(TagFile))
                {
                    int separator = line.IndexOf('=');
                    if (separator < 1) continue;
                    string friendCode = line[..separator];
                    string tag = line[(separator + 1)..];
                    if (friendCode.Length > 0 && tag.Length > 0)
                        loaded[friendCode] = tag;
                }
            }
            tags = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Reset();
            error = "Local private tags could not be read.";
            return false;
        }
    }

    internal static bool TryGetTag(PlayerControl player, out string tag)
    {
        string friendCode = PlayerFriendCode.FromOwner(player);
        return tags.TryGetValue(friendCode, out tag);
    }

    internal static bool AddTag(PlayerControl player, string tag, out string error)
    {
        string friendCode = PlayerFriendCode.FromOwner(player);
        if (string.IsNullOrEmpty(friendCode))
        {
            error = "Player has no connected friend code.";
            return false;
        }
        if (!Reload(out error)) return false;
        tags[friendCode] = tag;
        if (Save(out error)) return true;
        Reload(out _);
        return false;
    }

    internal static bool DeleteTag(PlayerControl player, out string error)
    {
        string friendCode = PlayerFriendCode.FromOwner(player);
        if (string.IsNullOrEmpty(friendCode))
        {
            error = "Player has no connected friend code.";
            return false;
        }
        if (!Reload(out error)) return false;
        if (!tags.Remove(friendCode)) return true;
        if (Save(out error)) return true;
        Reload(out _);
        return false;
    }

    private static bool Save(out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TagFile));
            File.WriteAllText(TagFile, string.Join("\n", tags.Select(entry => $"{entry.Key}={entry.Value}")));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Local private tags could not be saved.";
            return false;
        }
    }
}
