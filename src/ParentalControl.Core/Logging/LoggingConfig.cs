using Serilog;

namespace ParentalControl.Core.Logging;

public static class LoggingConfig
{
    private const string DefaultLogDirectory = @"C:\ProgramData\ParentalControl\logs";

    public static ILogger CreateLogger(string componentName, string? customLogDirectory = null)
    {
        var logDir = customLogDirectory ?? DefaultLogDirectory;
        try
        {
            Directory.CreateDirectory(logDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            logDir = Path.Combine(Path.GetTempPath(), "ParentalControl", "logs");
            Directory.CreateDirectory(logDir);
        }
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, $"{componentName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
