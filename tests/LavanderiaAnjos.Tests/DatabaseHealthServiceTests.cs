using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace LavanderiaAnjos.Tests;

public sealed class DatabaseHealthServiceTests
{
    [Fact]
    public async Task TrataSucessoDoVerificador()
    {
        var service = CreateService(DatabaseVerificationResult.Healthy("servidor", "banco", "versao"));

        Assert.True(await service.IsHealthyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TrataFalhaDoVerificadorSemPropagarExcecao()
    {
        var service = CreateService(DatabaseVerificationResult.Unhealthy("instancia_ou_porta", -2));

        Assert.False(await service.IsHealthyAsync(TestContext.Current.CancellationToken));
    }

    private static DatabaseHealthService CreateService(DatabaseVerificationResult result) =>
        new(new StubVerifier(result), NullLogger<DatabaseHealthService>.Instance);

    private sealed class StubVerifier(DatabaseVerificationResult result) : IDatabaseVerifier
    {
        public Task<DatabaseVerificationResult> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
