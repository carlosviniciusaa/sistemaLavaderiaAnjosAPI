namespace LavanderiaAnjos.Application.Database;

public interface IDatabaseHealthService
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
