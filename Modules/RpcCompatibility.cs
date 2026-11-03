namespace TOHE;

internal static class RpcCompatibility
{
    private const byte ProtocolVersion = 3;
    private static readonly Dictionary<int, int> PackedClients = new();
    private static readonly HashSet<int> IncompatibleClients = new();
    private static bool declared;
    private static byte[] BuildFingerprint => System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        $"{Main.PluginVersion}\n{Main.ForkId}\n{ThisAssembly.Git.Commit}\n{ThisAssembly.Git.Branch}"));
    internal static void ResetCapabilities() { PackedClients.Clear(); IncompatibleClients.Clear(); declared = false; }
    internal static void ResetDeclaration() => declared = false;
    internal static byte[] CreateDeclarationPayload()
    {
        var payload = new byte[33];
        payload[0] = ProtocolVersion;
        BuildFingerprint.CopyTo(payload, 1);
        return payload;
    }
    internal static bool SupportsPackedRpc(PlayerControl player)
        => player != null && (player.AmOwner ||
            PackedClients.TryGetValue(player.OwnerId, out var identity) && identity == player.GetInstanceID());

    internal static void EnsureDeclared()
    {
        if (!declared) SendCapabilities();
    }
    internal static void ValidateRecipient(int target)
    {
        if (target < 0 ? IncompatibleClients.Count != 0 : IncompatibleClients.Contains(target))
            throw new System.InvalidOperationException("Custom RPC recipient declared an incompatible protocol build");
        foreach (var player in Main.AllPlayerControls.Where(player => player != null && !player.AmOwner &&
            (target < 0 || player.OwnerId == target)))
            if (player.IsModded() && !SupportsPackedRpc(player))
                throw new System.InvalidOperationException("Known mod client has not declared the required protocol build");
    }
    internal static void SendCapabilities()
    {
        CustomRpcTransport.Send(CustomRPC.ProtocolCapabilities, writer =>
        {
            writer.Write(ProtocolVersion);
            writer.Write(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(BuildFingerprint));
        });
        declared = true;
    }

    internal static void ReceiveCapabilities(PlayerControl sender, Hazel.MessageReader reader)
    {
        if (reader.BytesRemaining != 33) throw new System.IO.InvalidDataException("Invalid capability declaration length");
        byte protocol = reader.ReadByte();
        var fingerprint = new byte[32];
        for (int index = 0; index < fingerprint.Length; index++) fingerprint[index] = reader.ReadByte();
        if (protocol != ProtocolVersion || !fingerprint.SequenceEqual(BuildFingerprint))
        {
            PackedClients.Remove(sender.OwnerId);
            IncompatibleClients.Add(sender.OwnerId);
            Logger.Warn($"Rejected incompatible RPC protocol declaration from owner {sender.OwnerId}", "CustomRPC");
            return;
        }
        IncompatibleClients.Remove(sender.OwnerId);
        PackedClients[sender.OwnerId] = sender.GetInstanceID();
    }

    // A known build is required for native buttons repurposed by this protocol.
    // VersionCheat and other forks are deliberately not capabilities.
    internal static bool IsCurrentClient(PlayerControl player)
    {
        if (player == null) return false;
        if (player.AmOwner) return true;
        return SupportsPackedRpc(player) && Main.playerVersion.TryGetValue(player.OwnerId, out var version) &&
            version.version == Main.version && version.forkId == Main.ForkId &&
            version.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})";
    }
}
