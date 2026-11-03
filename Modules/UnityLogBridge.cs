using System;
using BepInEx.Logging;

namespace TOHE.Modules;

// BepInEx excludes its Unity source from LogOutput.log by default. Write it to
// disk through the TOHE source without broadcasting it to other listeners.
internal sealed class UnityLogBridge : ILogListener
{
    public LogLevel LogLevelFilter => LogLevel.All;

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source?.SourceName != "Unity" || Main.Logger == null) return;

        string message = (eventArgs.Data?.ToString() ?? string.Empty)
            .Replace("\r", "\\r").Replace("\n", "\\n");
        var forwardedEvent = new LogEventArgs(
            $"[{DateTime.Now:HH:mm:ss}][Unity] {message}", eventArgs.Level, Main.Logger);
        foreach (var listener in BepInEx.Logging.Logger.Listeners)
        {
            if (listener is DiskLogListener diskLogListener &&
                (forwardedEvent.Level & diskLogListener.LogLevelFilter) != LogLevel.None)
                diskLogListener.LogEvent(Main.Logger, forwardedEvent);
        }
    }

    public void Dispose() { }
}
