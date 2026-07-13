using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using LavanderiaAnjos.Application.Database;

namespace LavanderiaAnjos.Tests;

public sealed class ApplicationStartupTests
{
    [Fact]
    public async Task AplicacaoIniciaComConfiguracaoValida()
    {
        using var factory = new TestWebApplicationFactory(
            DatabaseVerificationResult.Healthy("servidor", "banco", "versao"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void AplicacaoFalhaComMensagemClaraSemConnectionString()
    {
        using var factory = new MissingConnectionStringFactory();

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateClient();
            using var response = client
                .GetAsync("/", TestContext.Current.CancellationToken)
                .GetAwaiter()
                .GetResult();
        });
        var fullMessage = exception.ToString();

        Assert.Contains("ConnectionStrings:LavanderiaAnjos", fullMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", fullMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id=", fullMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MissingConnectionStringFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:LavanderiaAnjos", string.Empty);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LavanderiaAnjos"] = string.Empty
                });
            });
        }
    }
}
