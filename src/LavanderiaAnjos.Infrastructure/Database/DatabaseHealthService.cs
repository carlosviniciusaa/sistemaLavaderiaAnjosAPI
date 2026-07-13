using LavanderiaAnjos.Application.Database;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Infrastructure.Database;

public sealed partial class DatabaseHealthService(
    IDatabaseVerifier verifier,
    ILogger<DatabaseHealthService> logger) : IDatabaseHealthService
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var result = await verifier.VerifyAsync(cancellationToken);
        if (result.IsHealthy)
        {
            LogHealthy(logger);
            return true;
        }

        LogUnhealthy(logger, result.FailureCategory, result.ErrorCode);
        return false;
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Health check do banco concluído com sucesso.")]
    private static partial void LogHealthy(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Health check do banco falhou. Categoria: {FailureCategory}; código: {ErrorCode}.")]
    private static partial void LogUnhealthy(ILogger logger, string? failureCategory, int? errorCode);
}
