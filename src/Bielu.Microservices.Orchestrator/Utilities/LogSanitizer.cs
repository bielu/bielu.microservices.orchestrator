namespace Bielu.Microservices.Orchestrator.Utilities;

/// <summary>
/// Sanitizes user-provided values before they are passed to logging calls
/// to prevent log-forging attacks (injecting fake log entries via newlines).
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Removes carriage-return and line-feed characters from a string so that
    /// the value cannot forge additional log entries when written to a text sink.
    /// </summary>
    public static string Sanitize(string? value) =>
        value?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
}
