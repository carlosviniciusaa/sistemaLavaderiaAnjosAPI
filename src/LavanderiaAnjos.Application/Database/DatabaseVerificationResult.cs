namespace LavanderiaAnjos.Application.Database;

public sealed record DatabaseVerificationResult(
    bool IsHealthy,
    string? Server,
    string? Database,
    string? Version,
    string? FailureCategory,
    int? ErrorCode)
{
    public static DatabaseVerificationResult Healthy(string server, string database, string version) =>
        new(true, server, database, version, null, null);

    public static DatabaseVerificationResult Unhealthy(string category, int? errorCode = null) =>
        new(false, null, null, null, category, errorCode);
}
