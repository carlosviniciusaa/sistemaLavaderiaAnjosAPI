using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Infrastructure.Authentication;

public sealed partial class BootstrapAdminInitializer(
    LavanderiaAnjosDbContext dbContext,
    IConfiguration configuration,
    IPasswordHasher<Usuario> passwordHasher,
    ILogger<BootstrapAdminInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var nome = configuration["BootstrapAdmin:Nome"]?.Trim();
        var login = AdministradorInputValidator.NormalizeLogin(configuration["BootstrapAdmin:Login"]);
        var password = configuration["BootstrapAdmin:Password"];

        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            LogSkippedConfiguration(logger);
            return;
        }

        if (await dbContext.Usuarios.AnyAsync(cancellationToken))
        {
            LogSkippedExisting(logger);
            return;
        }

        var usuario = new Usuario
        {
            Nome = nome,
            Login = login,
            LoginNormalizado = login,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            Versao = 1
        };
        usuario.SenhaHash = passwordHasher.HashPassword(usuario, password);
        dbContext.Usuarios.Add(usuario);
        await dbContext.SaveChangesAsync(cancellationToken);
        LogCreated(logger, usuario.Id);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Bootstrap do administrador ignorado: configuração externa incompleta.")]
    private static partial void LogSkippedConfiguration(ILogger logger);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Bootstrap do administrador ignorado: já existe usuário.")]
    private static partial void LogSkippedExisting(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Administrador inicial criado. Id: {UsuarioId}.")]
    private static partial void LogCreated(ILogger logger, int usuarioId);
}
