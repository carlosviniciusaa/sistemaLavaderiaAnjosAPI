using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Web.Logging;

public sealed class DailyFileLoggerProvider(string directoryPath) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lock _writeLock = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new DailyFileLogger(name, directoryPath, _writeLock));

    public void Dispose() => _loggers.Clear();

    private sealed class DailyFileLogger(string category, string logDirectory, Lock writeLock) : ILogger
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
            var exceptionType = exception is null ? string.Empty : $" TipoExceção={exception.GetType().Name}.";
            var line = $"{now:O} [{logLevel}] {category} ({eventId.Id}): {message}{exceptionType}{Environment.NewLine}";

            try
            {
                lock (writeLock)
                {
                    Directory.CreateDirectory(logDirectory);
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
        }
    }
}
