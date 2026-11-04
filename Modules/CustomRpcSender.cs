using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using System;

namespace TOHE;

public class CustomRpcSender : IDisposable
{
    public MessageWriter stream;
    public readonly string name;
    public readonly SendOption sendOption;
    public bool isUnsafe;
    public delegate void onSendDelegateType();
    public onSendDelegateType onSendDelegate;

    public State CurrentState
    {
        get { return currentState; }
        set
        {
            if (isUnsafe) currentState = value;
            else Logger.Warn("CurrentStateはisUnsafeがtrueの時のみ上書きできます", "CustomRpcSender");
        }
    }
    private State currentState = State.BeforeInit;

    //0~: targetClientId (GameDataTo)
    //-1: 全プレイヤー (GameData)
    //-2: 未設定
    private int currentRpcTarget;
    private readonly int sessionGameId;
    private readonly OfficialSessionContext session;
    private readonly OfficialPacketBuilder.BoundedWriter writerOwner;
    private static readonly HashSet<CustomRpcSender> Active = new();
    private readonly List<OfficialPacketCodec.Record> records = new();
    internal int RecordCount => records.Count;
    private int childOffset;

    private CustomRpcSender() { }
    public CustomRpcSender(string name, SendOption sendOption, bool isUnsafe)
    {
        if (sendOption != SendOption.Reliable) throw new ArgumentOutOfRangeException(nameof(sendOption));
        sessionGameId = AmongUsClient.Instance.GameId;
        session = OfficialSessionContext.Capture();
        writerOwner = new OfficialPacketBuilder.BoundedWriter(sendOption);
        stream = writerOwner.Writer;
        Active.Add(this);

        this.name = name;
        this.sendOption = sendOption;
        this.isUnsafe = isUnsafe;
        this.currentRpcTarget = -2;
        onSendDelegate = () => Logger.Info($"{this.name}'s onSendDelegate =>", "CustomRpcSender");

        currentState = State.Ready;
        Logger.Info($"\"{name}\" is ready", "CustomRpcSender");
    }
    public static CustomRpcSender Create(string name = "No Name Sender", SendOption sendOption = SendOption.Reliable, bool isUnsafe = false)
    {
        return new CustomRpcSender(name, sendOption, isUnsafe);
    }

    #region Start/End Message
    public CustomRpcSender StartMessage(int targetClientId = -1)
    {
        if (currentState != State.Ready)
        {
            string errorMsg = $"Messageを開始しようとしましたが、StateがReadyではありません (in: \"{name}\")";
            if (isUnsafe)
            {
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            }
            else
            {
                throw new InvalidOperationException(errorMsg);
            }
        }

        currentRpcTarget = targetClientId;
        currentState = State.InRootMessage;
        return this;
    }
    public CustomRpcSender EndMessage()
    {
        if (currentState != State.InRootMessage)
        {
            string errorMsg = $"Messageを終了しようとしましたが、StateがInRootMessageではありません (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }
        currentRpcTarget = -2;
        currentState = State.Ready;
        return this;
    }
    #endregion
    #region Start/End Rpc
    public CustomRpcSender StartRpc(uint targetNetId, RpcCalls rpcCall)
        => StartRpc(targetNetId, (byte)rpcCall);
    public CustomRpcSender StartRpc(
        uint targetNetId,
        byte callId)
    {
        if (currentState != State.InRootMessage)
        {
            string errorMsg = $"RPCを開始しようとしましたが、StateがInRootMessageではありません (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }

        childOffset = stream.Position;
        stream.StartMessage(2);
        stream.WritePacked(targetNetId);
        stream.Write(callId);

        currentState = State.InRpc;
        return this;
    }
    public CustomRpcSender EndRpc()
    {
        if (currentState != State.InRpc)
        {
            string errorMsg = $"RPCを終了しようとしましたが、StateがInRpcではありません (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }

        stream.EndMessage();
        if (stream.Length - childOffset > ChildBudget)
            throw new InvalidOperationException("Vanilla RPC cannot fit the packet contract");
        records.Add(new OfficialPacketCodec.Record(currentRpcTarget, OfficialPacketBuilder.Snapshot(stream, childOffset)));
        stream.Clear(sendOption);
        currentState = State.InRootMessage;
        return this;
    }
    #endregion
    public CustomRpcSender AutoStartRpc(
        uint targetNetId,
        byte callId,
        int targetClientId = -1)
    {
        if (targetClientId == -2) targetClientId = -1;
        if (currentState is not State.Ready and not State.InRootMessage)
        {
            string errorMsg = $"RPCを自動で開始しようとしましたが、StateがReadyまたはInRootMessageではありません (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }
        if (currentRpcTarget != targetClientId)
        {
            //StartMessage処理
            if (currentState == State.InRootMessage) this.EndMessage();
            this.StartMessage(targetClientId);
        }
        this.StartRpc(targetNetId, callId);

        return this;
    }
    public void SendMessage()
    {
        if (currentState == State.InRootMessage) this.EndMessage();
        if (currentState != State.Ready)
        {
            string errorMsg = $"Attempted to send RPC, but State is not Ready  (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }

        // The writer belongs to this sender, even when a transport or callback fails.
        currentState = State.Finished;
        try
        {
            if (!session.IsCurrent())
                throw new InvalidOperationException("RPC sender belongs to an expired room");
            OfficialPacketBuilder.Send(records, sendOption);
            onSendDelegate?.Invoke();
            Logger.Info($"\"{name}\" is finished", "CustomRpcSender");
        }
        finally
        {
            Release();
        }
    }

    private int ChildBudget => OfficialPacketCodec.MaxChildLength(sessionGameId, currentRpcTarget,
        sendOption == SendOption.Reliable ? 3 : 1);

    private void Release()
    {
        records.Clear();
        Active.Remove(this);
        writerOwner.Dispose();
    }

    public void Dispose()
    {
        if (currentState == State.Finished) return;
        currentState = State.Finished;
        Release();
    }

    internal static void CancelPending()
    {
        foreach (var sender in Active.ToArray()) sender.Dispose();
    }

    // Write
    #region PublicWriteMethods
    public CustomRpcSender Write(float val) => Write(w => w.Write(val));
    public CustomRpcSender Write(string val)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(val ?? "") > ChildBudget)
            throw new InvalidOperationException("Vanilla RPC string requires business-level splitting");
        return Write(w => w.Write(val));
    }
    public CustomRpcSender Write(ulong val) => Write(w => w.Write(val));
    public CustomRpcSender Write(int val) => Write(w => w.Write(val));
    public CustomRpcSender Write(uint val) => Write(w => w.Write(val));
    public CustomRpcSender Write(ushort val) => Write(w => w.Write(val));
    public CustomRpcSender Write(byte val) => Write(w => w.Write(val));
    public CustomRpcSender Write(sbyte val) => Write(w => w.Write(val));
    public CustomRpcSender Write(bool val) => Write(w => w.Write(val));
    public CustomRpcSender Write(Il2CppStructArray<byte> bytes) => Write(w => w.Write(bytes));
    public CustomRpcSender Write(Il2CppStructArray<byte> bytes, int offset, int length) => Write(w => w.Write(bytes, offset, length));
    public CustomRpcSender WriteBytesAndSize(Il2CppStructArray<byte> bytes) => Write(w => w.WriteBytesAndSize(bytes));
    public CustomRpcSender WritePacked(int val) => Write(w => w.WritePacked(val));
    public CustomRpcSender WritePacked(uint val) => Write(w => w.WritePacked(val));
    public CustomRpcSender WriteNetObject(InnerNetObject obj) => Write(w => w.WriteNetObject(obj));
    public CustomRpcSender WriteMessageType(byte val) => Write(w => w.StartMessage(val));
    public CustomRpcSender WriteEndMessage() => Write(w => w.EndMessage());
    #endregion

    private CustomRpcSender Write(Action<MessageWriter> action)
    {
        if (currentState != State.InRpc)
        {
            string errorMsg = $"RPCを書き込もうとしましたが、StateがWrite(書き込み中)ではありません (in: \"{name}\")";
            if (isUnsafe)
                Logger.Warn(errorMsg, "CustomRpcSender.Warn");
            else
                throw new InvalidOperationException(errorMsg);
        }
        action(stream);
        if (stream.Length - childOffset > ChildBudget)
            throw new InvalidOperationException("Vanilla RPC payload exceeds its bounded child budget");
        return this;
    }

    public enum State
    {
        BeforeInit = 0, //初期化前 何もできない
        Ready, //送信準備完了 StartMessageとSendMessageを実行可能
        InRootMessage, //StartMessage～EndMessageの間の状態 StartRpcとEndMessageを実行可能
        InRpc, //StartRpc～EndRpcの間の状態 WriteとEndRpcを実行可能
        Finished, //送信後 何もできない
    }
}
public static class CustomRpcSenderExtensions
{
    public static void RpcSetRole(this CustomRpcSender sender, PlayerControl player, RoleTypes role, int targetClientId = -1)
    {
        sender.AutoStartRpc(player.NetId, (byte)RpcCalls.SetRole, targetClientId)
            .Write((ushort)role)
            .Write(true) // canOverride
            .EndRpc();
    }
}
