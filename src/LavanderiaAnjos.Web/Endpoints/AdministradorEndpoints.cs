using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace LavanderiaAnjos.Web.Endpoints;

public static class AdministradorEndpoints
{
    public static IEndpointRouteBuilder MapAdministradorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admins = endpoints.MapGroup("/api/administradores")
            .WithTags("Administradores")
            .RequireAuthorization("Administradores");

        admins.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });
        admins.MapGet("", async (IAdministradorService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListarAsync(cancellationToken)));
        admins.MapGet("/{id:int}", async (int id, IAdministradorService service, CancellationToken cancellationToken) =>
        {
            var administrador = await service.ObterAsync(id, cancellationToken);
            return administrador is null
                ? Results.NotFound(new { message = "Administrador não encontrado." })
                : Results.Ok(administrador);
        });
        admins.MapPost("", CriarAsync);
        admins.MapPut("/{id:int}", AtualizarAsync);
        admins.MapPost("/{id:int}/redefinir-senha", RedefinirSenhaAsync);
        admins.MapPatch("/{id:int}/inativar", InativarAsync);
        admins.MapPatch("/{id:int}/reativar", ReativarAsync);
        return endpoints;
    }

    private static async Task<IResult> CriarAsync(
        AdministradorCreateRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IAdministradorService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        var result = await service.CriarAsync(request, Audit(context), cancellationToken);
        return ToResult(result, created: true);
    }

    private static async Task<IResult> AtualizarAsync(
        int id,
        AdministradorUpdateRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IAdministradorService service,
        LavanderiaAnjosDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        var currentId = UserId(context.User);
        var oldLogin = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await service.AtualizarAsync(id, request, Audit(context), cancellationToken);
        if (result.IsSuccess && id == currentId)
        {
            if (!string.Equals(oldLogin, result.Value!.Login, StringComparison.Ordinal))
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Headers["X-Reauthentication-Required"] = "true";
            }
            else
            {
                var usuario = await dbContext.Usuarios.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
                await SignInAsync(context, usuario);
            }
        }
        return ToResult(result);
    }

    private static async Task<IResult> RedefinirSenhaAsync(
        int id,
        AdministradorPasswordRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IAdministradorService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        var result = await service.RedefinirSenhaAsync(id, request, Audit(context), cancellationToken);
        if (result.IsSuccess && id == UserId(context.User))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Headers["X-Reauthentication-Required"] = "true";
        }
        return ToResult(result);
    }

    private static async Task<IResult> InativarAsync(
        int id,
        AdministradorStatusRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IAdministradorService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.InativarAsync(id, request, Audit(context), cancellationToken));
    }

    private static async Task<IResult> ReativarAsync(
        int id,
        AdministradorStatusRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IAdministradorService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.ReativarAsync(id, request, Audit(context), cancellationToken));
    }

    private static async Task<bool> ValidateCsrfAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static IResult CsrfFailure() => Results.Json(
        new { message = "A validação de segurança expirou. Recarregue a página e tente novamente." },
        statusCode: StatusCodes.Status403Forbidden);

    private static IResult ToResult(AdministradorServiceResult<AdministradorResponse> result, bool created = false)
    {
        if (result.IsSuccess)
            return created
                ? Results.Created($"/api/administradores/{result.Value!.Id}", result.Value)
                : Results.Ok(result.Value);

        var message = result.Errors.Values.SelectMany(value => value).First();
        return result.ErrorKind switch
        {
            AdministradorServiceErrorKind.NotFound => Results.NotFound(new { message }),
            AdministradorServiceErrorKind.Conflict or AdministradorServiceErrorKind.Concurrency => Results.Conflict(new { message, errors = result.Errors }),
            AdministradorServiceErrorKind.Forbidden => Results.Json(new { message }, statusCode: StatusCodes.Status403Forbidden),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static AdministradorAuditContext Audit(HttpContext context) =>
        new(UserId(context.User), context.User.FindFirstValue(ClaimTypes.Name) ?? "Usuário autenticado",
            Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);

    private static int UserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue("usuario_id")!, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static Task SignInAsync(HttpContext context, Usuario usuario)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            AuthEndpoints.CreateClaims(usuario), CookieAuthenticationDefaults.AuthenticationScheme));
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }
}
