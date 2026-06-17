using AssetStudio;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioConsoleLogger : ILogger
{
    public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false)
    {
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
}
