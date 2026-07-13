using LavanderiaAnjos.Application.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LavanderiaAnjos.Tests;

public sealed class TestWebApplicationFactory(DatabaseVerificationResult databaseResult)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LavanderiaAnjos"] =
                    "Server=localhost;Database=LavanderiaAnjosTests;Integrated Security=True;Encrypt=False;"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDatabaseVerifier>();
            services.AddSingleton<IDatabaseVerifier>(new FakeDatabaseVerifier(databaseResult));
        });
    }

    private sealed class FakeDatabaseVerifier(DatabaseVerificationResult result) : IDatabaseVerifier
    {
        public Task<DatabaseVerificationResult> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
