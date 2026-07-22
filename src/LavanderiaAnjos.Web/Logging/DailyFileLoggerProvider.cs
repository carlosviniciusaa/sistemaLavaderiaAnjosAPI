using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Web.Logging;

public sealed class DailyFileLoggerProvider(string directoryPath, int retentionDays = 30) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lock _writeLock = new();
    private readonly RetentionState _retentionState = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name =>
            new DailyFileLogger(name, directoryPath, retentionDays, _writeLock, _retentionState));

    public void Dispose() => _loggers.Clear();

    private sealed class DailyFileLogger(
        string category,
        string logDirectory,
        int retainedDays,
        Lock writeLock,
        RetentionState retentionState) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var now = DateTimeOffset.Now;
            var message = SensitiveDataSanitizer.Redact(formatter(state, exception));
            var exceptionDetails = exception is null
                ? string.Empty
                : Environment.NewLine + SensitiveDataSanitizer.Redact(exception.ToString());
            var line = $"{now:O} [{logLevel}] {category} ({eventId.Id}): {message}{exceptionDetails}{Environment.NewLine}";

            try
            {
                lock (writeLock)
                {
                    Directory.CreateDirectory(logDirectory);
                    DeleteExpiredLogs(logDirectory, now, retainedDays, retentionState);
                    File.AppendAllText(Path.Combine(logDirectory, $"lavanderia-{now:yyyyMMdd}.log"), line);
                }
            }
            catch (IOException)
            {
                // O logger nunca interrompe a aplicação por falha de escrita.
            }
            catch (UnauthorizedAccessException)
            {
                // A permissão da pasta deve ser ajustada na instalação do serviço.
            }
            catch (System.Security.SecurityException)
            {
                // A política de segurança do host não pode interromper a aplicação.
            }
        }

        private static void DeleteExpiredLogs(
            string directory,
            DateTimeOffset now,
            int days,
            RetentionState state)
        {
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            if (state.LastCleanup == today)
            {
                return;
            }

            var cutoff = now.UtcDateTime.AddDays(-Math.Clamp(days, 1, 365));
            foreach (var path in Directory.EnumerateFiles(directory, "lavanderia-*.log"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            state.LastCleanup = today;
        }
    }

    private sealed class RetentionState
    {
        public DateOnly? LastCleanup { get; set; }
    }
}
