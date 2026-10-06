using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AIInvestmentWorkbench.Infrastructure.Logging;

/// <summary>Daily UTF-8 log. Callers must never log credentials, user content, SQL parameters or AI payloads.</summary>
public sealed partial class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    public static string Redact(string value) => SecretPattern().Replace(value, "$1[REDACTED]");
    [GeneratedRegex(@"(?i)((?:api[_ -]?key|password|secret|token)\s*[=:]\s*|Bearer\s+|sk-)[^\s,;""']+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        // Exception messages/Data may contain secrets. Preserve types and full stacks only.
        var text = new StringBuilder().Append(DateTimeOffset.UtcNow.ToString("O")).Append(' ')
            .Append(level).Append(' ').Append(category).Append(' ').AppendLine(Redact(message));
        for (var current = exception; current is not null; current = current.InnerException)
            text.AppendLine(current.GetType().FullName).AppendLine(current.StackTrace);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"workbench-{DateTime.UtcNow:yyyyMMdd}.log"), text.ToString(), Encoding.UTF8);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.WriteLine("Local log unavailable: " + error.GetType().Name);
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information && level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) owner.Write(category, level, formatter(state, exception), exception);
        }
    }
}
