using System.Security.Claims;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LavanderiaAnjos.Web.Endpoints;

public static partial class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Autenticação");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        auth.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue("usuario_id"),
            nome = user.FindFirstValue(ClaimTypes.Name),
            login = user.FindFirstValue(ClaimTypes.NameIdentifier)
        })).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LavanderiaAnjosDbContext dbContext,
        IPasswordHasher<Usuario> passwordHasher,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Authentication");
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { message = "Informe login e senha." });
        }

        var login = AdministradorInputValidator.NormalizeLogin(request.Login);
        var usuario = await dbContext.Usuarios.SingleOrDefaultAsync(item => item.LoginNormalizado == login, cancellationToken);
        if (usuario is null || !usuario.Ativo)
        {
            LogLoginFailed(logger);
            return Results.Unauthorized();
        }

        var verification = passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            LogLoginFailed(logger);
            return Results.Unauthorized();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.SenhaHash = passwordHasher.HashPassword(usuario, request.Password);
            usuario.AtualizadoEm = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var claims = CreateClaims(usuario);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        LogLoginSucceeded(logger, usuario.Id);

        return Results.Ok(new { usuario.Id, usuario.Nome, usuario.Login });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Authentication");
        var usuarioId = context.User.FindFirstValue("usuario_id") ?? "indisponível";
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        LogLogout(logger, usuarioId);
        return Results.NoContent();
    }

    public sealed record LoginRequest(string? Login, string? Password);

    internal static Claim[] CreateClaims(Usuario usuario) =>
    [
        new Claim(ClaimTypes.NameIdentifier, usuario.Login),
        new Claim(ClaimTypes.Name, usuario.Nome),
        new Claim("usuario_id", usuario.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim("session_version", usuario.VersaoSessao.ToString(System.Globalization.CultureInfo.InvariantCulture))
    ];

    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Login concluído. UsuarioId: {UsuarioId}.")]
    private static partial void LogLoginSucceeded(ILogger logger, int usuarioId);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Tentativa de login inválida.")]
    private static partial void LogLoginFailed(ILogger logger);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Information, Message = "Logout concluído. UsuarioId: {UsuarioId}.")]
    private static partial void LogLogout(ILogger logger, string usuarioId);
}
