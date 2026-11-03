using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;

namespace TOHE.Modules;

public abstract class GameOptionsSender
{
    #region Static
    public readonly static List<GameOptionsSender> AllSenders = new(100) { new NormalGameOptionsSender() };

    public static void SendAllGameOptions()
    {
        AllSenders.RemoveAll(s => !s.AmValid()); // .AmValid() has a virtual property, so it doesn't always return true
        var AllSendersArray = AllSenders.ToArray();
        var batch = new OfficialPacketBuilder.Batch();
        foreach (GameOptionsSender sender in AllSendersArray)
        {
            if (sender.IsDirty) sender.SendGameOptions(batch);
        }
        batch.Send();
        foreach (GameOptionsSender sender in AllSendersArray) sender.IsDirty = false;
    }
    #endregion

    public abstract IGameOptions BasedGameOptions { get; }
    public abstract bool IsDirty { get; protected set; }


    public virtual void SendGameOptions(OfficialPacketBuilder.Batch batch = null)
    {
        var opt = BuildGameOptions();
        ServerLimits.Normalize(opt);
        // Keep version framing identical to LogicOptions.Serialize in the game.
        var factory = new GameOptionsFactory(new UnityLogger().Cast<ILogger>());
        SendOptionsArray(factory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn), batch);
    }
    public virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray, OfficialPacketBuilder.Batch batch = null)
    {
        bool ownsBatch = batch == null;
        batch ??= new OfficialPacketBuilder.Batch();
        try
        {
            byte logicOptionsIndex = 0;
            foreach (var logicComponent in GameManager.Instance.LogicComponents.GetFastEnumerator())
            {
                if (logicComponent.TryCast<LogicOptions>(out _))
                {
                    SendOptionsArray(optionArray, logicOptionsIndex, -1, batch);
                }
                logicOptionsIndex++;
            }
        }
        catch (System.Exception error)
        {
            Logger.Fatal(error.ToString(), "GameOptionsSender.SendOptionsArray");
            throw;
        }
        if (ownsBatch) batch.Send();
    }
    protected virtual void SendOptionsArray(Il2CppStructArray<byte> optionArray, byte LogicOptionsIndex, int targetClientId,
        OfficialPacketBuilder.Batch batch)
    {
        batch.AddChild(targetClientId, writer =>
        {
            writer.StartMessage(1);
            {
                writer.WritePacked(GameManager.Instance.NetId);
                writer.StartMessage(LogicOptionsIndex);
                {
                    writer.WriteBytesAndSize(optionArray);
                }
                writer.EndMessage();
            }
            writer.EndMessage();
        });
    }
    public abstract IGameOptions BuildGameOptions();

    public virtual bool AmValid() => true;
}
