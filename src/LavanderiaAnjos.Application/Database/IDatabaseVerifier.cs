namespace LavanderiaAnjos.Application.Database;

public interface IDatabaseVerifier
{
    Task<DatabaseVerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}
