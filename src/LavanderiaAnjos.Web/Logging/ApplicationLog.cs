using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Web.Logging;

internal static partial class ApplicationLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Inicializando Lavanderia Anjos no ambiente {EnvironmentName}.")]
    public static partial void Starting(ILogger logger, string environmentName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Lavanderia Anjos iniciada.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Encerrando Lavanderia Anjos.")]
    public static partial void Stopping(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Erro não tratado. Código: {ErrorCode}.")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string errorCode);
}
