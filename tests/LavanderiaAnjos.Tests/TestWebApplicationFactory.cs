using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LavanderiaAnjos.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DatabaseVerificationResult _databaseResult;
    private readonly bool _usuarioAtivo;
    private readonly Action<IServiceCollection>? _configureServices;

    public const string Login = "admin.teste";
    public const string Password = "Teste-Seguro-123!";

    public TestWebApplicationFactory(
        DatabaseVerificationResult databaseResult,
        bool usuarioAtivo = true,
        Action<IServiceCollection>? configureServices = null)
    {
        _databaseResult = databaseResult;
        _usuarioAtivo = usuarioAtivo;
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LavanderiaAnjos"] =
                    "Server=localhost;Database=LavanderiaAnjosTests;Integrated Security=True;Encrypt=False;",
                ["Importacao:TemporaryPath"] = Path.Combine(Path.GetTempPath(), "LavanderiaAnjosTests", Guid.NewGuid().ToString("N"))
            });
        });

        builder.ConfigureServices(services =>
        {
            var databaseName = $"LavanderiaAnjosTests-{Guid.NewGuid():N}";
            services.RemoveAll<DbContextOptions<LavanderiaAnjosDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LavanderiaAnjosDbContext>>();
            services.RemoveAll<LavanderiaAnjosDbContext>();
            services.AddDbContext<LavanderiaAnjosDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.RemoveAll<IDatabaseVerifier>();
            services.AddSingleton<IDatabaseVerifier>(new FakeDatabaseVerifier(_databaseResult));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            _configureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        var usuario = new Usuario
        {
            Nome = "Administrador de Teste",
            Login = Login,
            LoginNormalizado = Login,
            Ativo = _usuarioAtivo,
            CriadoEm = DateTime.UtcNow,
            Versao = 1
        };
        usuario.SenhaHash = hasher.HashPassword(usuario, Password);
        dbContext.Usuarios.Add(usuario);
        dbContext.SaveChanges();
        return host;
    }

    private sealed class FakeDatabaseVerifier(DatabaseVerificationResult result) : IDatabaseVerifier
    {
        public Task<DatabaseVerificationResult> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
