using AssetStudio;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioConsoleLogger : ILogger
{
    private readonly int minSeverity;

    public AssetStudioConsoleLogger(string minLevel = "warning")
    {
        minSeverity = minLevel.Trim().ToLowerInvariant() switch
        {
            "debug" => 1,
            "info" => 2,
            _ => 3,
        };
    }

    public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false)
    {
        if (!ignoreLevel && Severity(loggerEvent) < minSeverity)
        {
            return;
        }

        var prefix = loggerEvent switch
        {
            LoggerEvent.Verbose => "V",
            LoggerEvent.Debug => "D",
            LoggerEvent.Info => "I",
            LoggerEvent.Warning => "W",
            LoggerEvent.Error => "E",
            _ => "?",
        };

        Console.Error.WriteLine($"[AssetStudio {prefix}] {message}");
    }

    private static int Severity(LoggerEvent loggerEvent)
    {
        return loggerEvent switch
        {
            LoggerEvent.Verbose => 0,
            LoggerEvent.Debug => 1,
            LoggerEvent.Info => 2,
            LoggerEvent.Warning => 3,
            LoggerEvent.Error => 4,
            _ => 2,
        };
    }
}
