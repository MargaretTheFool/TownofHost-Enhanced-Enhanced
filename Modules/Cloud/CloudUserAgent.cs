using System;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TOHE.Modules;

public static class CloudUserAgent
{
    private sealed class InitializationState(bool isReady, string value)
    {
        public bool IsReady { get; } = isReady;
        public string Value { get; } = value;
    }

    private static InitializationState state = new(false, "CloudUaNotInitialized");

    /// <summary>
    /// Reads Unity and build metadata. Call this on the Unity thread before starting Cloud HTTP work.
    /// </summary>
    public static void Initialize()
    {
        string pluginVersion = Main.PluginVersion;
        string gameVersion = Application.version;
        string repositoryUrl = ThisAssembly.Git.RepositoryUrl;
        string branch = ThisAssembly.Git.Branch;
        string sha = ThisAssembly.Git.Sha;
        bool isDirty = ThisAssembly.Git.IsDirty;

        if (!IsNumericVersion(pluginVersion))
        {
            PublishFailure("CloudUaPluginVersion");
            return;
        }

        if (!IsNumericVersion(gameVersion))
        {
            PublishFailure("CloudUaGameVersion");
            return;
        }

        if (!TryGetRepositoryOwner(repositoryUrl, out string owner))
        {
            PublishFailure("CloudUaOrigin");
            return;
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            PublishFailure("CloudUaBranch");
            return;
        }

        if (!IsFullSha(sha))
        {
            PublishFailure("CloudUaCommit");
            return;
        }

        string commitPrefix = sha[..9].ToLowerInvariant();
        string encodedBranch = EncodeRfc3986Component(branch);
        string dirty = isDirty ? "true" : "false";
        string value = $"TOHEE/{pluginVersion} AmongUs/{gameVersion} (Git={owner}/{encodedBranch}/{commitPrefix}/{dirty})";
        Volatile.Write(ref state, new InitializationState(true, value));
    }

    /// <summary>
    /// Returns the cached User-Agent without accessing Unity. On failure, value explains why it is unavailable.
    /// </summary>
    public static bool TryGet(out string value)
    {
        InitializationState current = Volatile.Read(ref state);
        value = current.Value;
        return current.IsReady;
    }

    private static void PublishFailure(string message)
    {
        Volatile.Write(ref state, new InitializationState(false, message));
    }

    private static bool IsNumericVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return false;

        int components = 1;
        bool hasDigit = false;
        foreach (char character in version)
        {
            if (character is >= '0' and <= '9')
            {
                hasDigit = true;
                continue;
            }

            if (character != '.' || !hasDigit) return false;
            components++;
            hasDigit = false;
        }

        return hasDigit && components is >= 2 and <= 4;
    }

    private static bool TryGetRepositoryOwner(string repositoryUrl, out string owner)
    {
        owner = null;
        if (string.IsNullOrWhiteSpace(repositoryUrl)) return false;

        string path;
        bool scpStyle = repositoryUrl.IndexOf("://", StringComparison.Ordinal) < 0 && repositoryUrl.Contains('@');
        if (scpStyle)
        {
            // Git also accepts scp-style remotes such as git@github.com:owner/repository.git.
            int separator = repositoryUrl.IndexOf(':');
            if (separator < 0) return false;
            path = repositoryUrl[(separator + 1)..];
        }
        else if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Host))
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        string[] segments = path.Trim('/').Split('/');
        if (segments.Length < 2) return false;

        owner = Uri.UnescapeDataString(segments[^2]);
        if (owner.Length == 0) return false;

        foreach (char character in owner)
        {
            if (!(character is >= 'A' and <= 'Z')
                && !(character is >= 'a' and <= 'z')
                && !(character is >= '0' and <= '9')
                && character != '-')
            {
                owner = null;
                return false;
            }
        }

        return true;
    }

    private static bool IsFullSha(string sha)
    {
        if (sha?.Length != 40) return false;

        foreach (char character in sha)
        {
            if (!(character is >= '0' and <= '9')
                && !(character is >= 'a' and <= 'f')
                && !(character is >= 'A' and <= 'F')) return false;
        }

        return true;
    }

    private static string EncodeRfc3986Component(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        var encoded = new StringBuilder(bytes.Length);
        foreach (byte current in bytes)
        {
            if (IsUnreserved((char)current))
            {
                encoded.Append((char)current);
            }
            else
            {
                encoded.Append('%').Append(current.ToString("X2"));
            }
        }

        return encoded.ToString();
    }

    private static bool IsUnreserved(char character) =>
        character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-' or '.' or '_' or '~';
}
