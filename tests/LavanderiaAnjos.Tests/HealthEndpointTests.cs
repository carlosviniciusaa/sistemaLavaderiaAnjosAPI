using System.Net;
using System.Net.Http.Json;
using LavanderiaAnjos.Application.Database;

namespace LavanderiaAnjos.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthRetornaHealthyQuandoBancoResponde()
    {
        using var factory = new TestWebApplicationFactory(
            DatabaseVerificationResult.Healthy("servidor", "banco", "versao"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
        Assert.Equal("Healthy", payload.Database);
    }

    [Fact]
    public async Task HealthRetornaRespostaGenericaQuandoBancoFalha()
    {
        using var factory = new TestWebApplicationFactory(
            DatabaseVerificationResult.Unhealthy("autenticacao", 18456));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Unhealthy", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("18456", body, StringComparison.Ordinal);
    }

    private sealed record HealthResponse(string Status, string Database);
}
