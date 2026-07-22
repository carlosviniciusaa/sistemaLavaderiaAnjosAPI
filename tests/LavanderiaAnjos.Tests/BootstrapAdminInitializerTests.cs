using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Authentication;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LavanderiaAnjos.Tests;

public sealed class BootstrapAdminInitializerTests
{
    [Fact]
    public async Task CriaPrimeiroAdministradorComSenhaProtegida()
    {
        await using var dbContext = CreateDbContext();
        var hasher = new PasswordHasher<Usuario>();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Nome"] = "Administrador",
            ["BootstrapAdmin:Login"] = "ADMIN",
            ["BootstrapAdmin:Password"] = "Senha-Externa-123!"
        });
        var initializer = new BootstrapAdminInitializer(
            dbContext,
            configuration,
            hasher,
            NullLogger<BootstrapAdminInitializer>.Instance);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        var usuario = Assert.Single(await dbContext.Usuarios.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("admin", usuario.Login);
        Assert.NotEqual("Senha-Externa-123!", usuario.SenhaHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, "Senha-Externa-123!"));
    }

    [Fact]
    public async Task NaoCriaSemConfiguracaoCompletaOuQuandoJaExisteUsuario()
    {
        await using var missingConfigurationDb = CreateDbContext();
        var hasher = new PasswordHasher<Usuario>();
        var missingInitializer = new BootstrapAdminInitializer(
            missingConfigurationDb,
            Configuration(new Dictionary<string, string?> { ["BootstrapAdmin:Nome"] = "Administrador" }),
            hasher,
            NullLogger<BootstrapAdminInitializer>.Instance);
        await missingInitializer.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await missingConfigurationDb.Usuarios.ToListAsync(TestContext.Current.CancellationToken));

        await using var existingUserDb = CreateDbContext();
        existingUserDb.Usuarios.Add(new Usuario
        {
            Nome = "Existente",
            Login = "existente",
            SenhaHash = "hash-existente",
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });
        await existingUserDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        var completeInitializer = new BootstrapAdminInitializer(
            existingUserDb,
            Configuration(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Nome"] = "Novo",
                ["BootstrapAdmin:Login"] = "novo",
                ["BootstrapAdmin:Password"] = "Outra-Senha-123!"
            }),
            hasher,
            NullLogger<BootstrapAdminInitializer>.Instance);
        await completeInitializer.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Single(await existingUserDb.Usuarios.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static LavanderiaAnjosDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LavanderiaAnjosDbContext>()
            .UseInMemoryDatabase($"BootstrapAdminTests-{Guid.NewGuid():N}")
            .Options);

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
