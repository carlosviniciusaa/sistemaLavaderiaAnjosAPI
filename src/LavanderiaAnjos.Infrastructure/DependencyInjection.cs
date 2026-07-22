using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Application.Importacoes;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Authentication;
using LavanderiaAnjos.Infrastructure.Administradores;
using LavanderiaAnjos.Infrastructure.Clientes;
using LavanderiaAnjos.Infrastructure.Database;
using LavanderiaAnjos.Infrastructure.Importacoes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LavanderiaAnjos.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "LavanderiaAnjos";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A conexão com o banco não está configurada. Configure ConnectionStrings:LavanderiaAnjos externamente.");
        }

        services.AddDbContext<LavanderiaAnjosDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.CommandTimeout(15);
                sqlServer.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            }));

        services.AddScoped<IDatabaseVerifier, SqlServerDatabaseVerifier>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
        services.AddScoped<IClienteService, ClienteService>();
        services.AddScoped<IAdministradorService, AdministradorService>();
        services.AddScoped<IClienteImportacaoService, ClienteImportacaoService>();
        services.AddSingleton<IClientePlanilhaReader, ClientePlanilhaReader>();
        services.AddSingleton<IImportacaoFileStore, ImportacaoFileStore>();
        services.AddScoped<BootstrapAdminInitializer>();
        services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        services.AddSingleton<IPlanilhaClienteInspector, PlanilhaClienteInspector>();

        return services;
    }
}
