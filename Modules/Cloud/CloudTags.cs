using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TOHE.Modules;

// Requests and JSON parsing run on a worker. Cache changes and suffix updates
// happen from Tick/SetEnabled on the Unity thread.
internal static class CloudTags
{
    private static ConfigEntry<bool> enabled;
    private static Dictionary<string, DevUser> cache = new(StringComparer.Ordinal);
    private static Task<Dictionary<string, JsonElement>> pending;
    private static int generation;
    private static int pendingGeneration;
    private static bool startupPending = true;
    private static bool refreshAfterPending;
    private static bool refreshWhenOriginAvailable;

    internal static bool Enabled => enabled?.Value ?? true;

    internal static DevUser GetEffective(string code) => Enabled && code != null && cache.TryGetValue(code.Trim().ToLowerInvariant(), out var user) ? user : null;

    internal static void Initialize(ConfigFile config)
    {
        enabled = config.Bind("TOHEE Cloud", "Use cloud tags", true,
            "Apply the cached Cloud tags to player displays. Independent of room sharing.");
        cache = new(StringComparer.Ordinal);
        pending = null;
        generation = 0;
        pendingGeneration = 0;
        startupPending = true;
        refreshAfterPending = false;
        refreshWhenOriginAvailable = false;
    }

    internal static void SetEnabled(bool value, Uri origin)
    {
        if (enabled == null || enabled.Value == value) return;

        enabled.Value = value;
        if (!value)
        {
            generation++;
            refreshAfterPending = false;
            refreshWhenOriginAvailable = false;
            RefreshNames();
            return;
        }

        // Make a retained snapshot visible before starting the next request.
        RefreshNames();
        if (origin == null) refreshWhenOriginAvailable = true;
        else Refresh(origin);
    }

    internal static void Refresh(Uri origin)
    {
        if (!Enabled)
        {
            CloudService.Notify(Translator.GetString("CloudTagsDisabled"));
            return;
        }

        if (origin == null)
        {
            CloudService.Notify(Translator.GetString("CloudTagsUnavailable"));
            return;
        }

        startupPending = false;
        refreshWhenOriginAvailable = false;
        if (pending == null)
        {
            pendingGeneration = generation;
            pending = Task.Run(() => Query(origin));
        }
        else if (pendingGeneration != generation)
        {
            // A request started before disabling is stale. Do not apply it or
            // issue a parallel request; refresh once that request has drained.
            refreshAfterPending = true;
        }
        CloudService.Notify(Translator.GetString("CloudTagsLoading"));
    }

    internal static void Tick(Uri origin)
    {
        if (pending is { IsCompleted: true })
        {
            bool currentRequest = pendingGeneration == generation;
            bool refreshQueued = refreshAfterPending;
            var result = pending.IsCompletedSuccessfully ? pending.Result : null;
            pending = null;
            refreshAfterPending = false;

            if (currentRequest && Enabled)
            {
                if (result == null)
                {
                    CloudService.Notify(Translator.GetString("CloudTagsFailed"));
                }
                else
                {
                    var replacement = new Dictionary<string, DevUser>(StringComparer.Ordinal);
                    foreach (var (code, fields) in result)
                    {
                        var source = DevManager.GetBuiltInDevUser(code);
                        // Only local built-ins may use their local sponsor-color file.
                        // Unknown Cloud codes are allowed by the Cloud DTO and must not
                        // become path components for a local file lookup.
                        string localCode = source.Code == code ? source.Code : string.Empty;
                        var user = new DevUser(localCode, source.Color, source.UserType, source.Tag, source.IsUp,
                            source.IsDev, source.DeBug, source.ColorCmd, source.NameCmd, source.UpName);
                        foreach (var field in fields.EnumerateObject())
                        {
                            switch (field.Name)
                            {
                                case "color": user.Color = ReadNullableString(field.Value, "null"); break;
                                case "user_type": user.UserType = ReadNullableString(field.Value, "null"); break;
                                case "tag": user.Tag = ReadNullableString(field.Value, "null"); break;
                                case "is_up": user.IsUp = field.Value.GetBoolean(); break;
                                case "is_dev": user.IsDev = field.Value.GetBoolean(); break;
                                case "debug": user.DeBug = field.Value.GetBoolean(); break;
                                case "color_cmd": user.ColorCmd = field.Value.GetBoolean(); break;
                                case "name_cmd": user.NameCmd = field.Value.GetBoolean(); break;
                                case "up_name": user.UpName = ReadNullableString(field.Value, null); break;
                            }
                        }
                        replacement.Add(code, user);
                    }
                    cache = replacement;
                    RefreshNames();
                    CloudService.Notify(Translator.GetString("CloudTagsLoaded"));
                }
            }

            if (refreshQueued && Enabled && origin != null)
                Refresh(origin);
        }

        if (Enabled && pending == null && origin != null)
        {
            if (refreshWhenOriginAvailable)
                Refresh(origin);
            else if (startupPending)
                Refresh(origin);
        }
    }

    private static void RefreshNames()
    {
        if (GameStates.IsLobby)
            foreach (var player in Main.AllPlayerControls) Utils.ApplySuffix(player);
    }

    private static string ReadNullableString(JsonElement value, string nullValue) =>
        value.ValueKind == JsonValueKind.Null ? nullValue : value.GetString();

    private static async Task<Dictionary<string, JsonElement>> Query(Uri origin)
    {
        var response = await CloudService.Send(origin, HttpMethod.Get, "/api/mod/tags/query", null).ConfigureAwait(false);
        if (response.Status != HttpStatusCode.OK) return null;

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (root.GetProperty("revision").GetInt64() < 0 || root.GetProperty("refresh_after_seconds").GetInt32() < 0)
                return null;

            var items = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                string code = item.GetProperty("friend_code").GetString();
                var fields = item.GetProperty("overrides");
                if (string.IsNullOrEmpty(code)) return null;
                code = code.Trim().ToLowerInvariant();
                int separator = code.IndexOf('#');
                if (code.Length > 128 || separator <= 0 || separator == code.Length - 1
                    || separator != code.LastIndexOf('#') || fields.ValueKind != JsonValueKind.Object || items.ContainsKey(code)) return null;
                foreach (char character in code)
                    if (char.IsWhiteSpace(character) || char.IsControl(character)) return null;
                foreach (var field in fields.EnumerateObject())
                {
                    bool valid = field.Name switch
                    {
                        "color" or "user_type" or "tag" or "up_name" => field.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                        "is_up" or "is_dev" or "debug" or "color_cmd" or "name_cmd" => field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                        _ => true
                    };
                    if (!valid) return null;
                }
                items.Add(code, fields.Clone());
            }
            return items;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
