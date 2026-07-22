using System.Globalization;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using LavanderiaAnjos.Application.Importacoes;
using Microsoft.AspNetCore.Antiforgery;

namespace LavanderiaAnjos.Web.Endpoints;

public static partial class ImportacaoEndpoints
{
    public static IEndpointRouteBuilder MapImportacaoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints.MapGroup("/api/importacoes/clientes")
            .WithTags("Importação inicial")
            .RequireAuthorization();

        imports.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });
        imports.MapGet("/historico", async (ClaimsPrincipal user, IClienteImportacaoService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.HistoricoAsync(UserId(user), cancellationToken)));
        imports.MapGet("/{loteId:guid}", ObterAsync);
        imports.MapGet("/{loteId:guid}/itens/{itemId:guid}", async (
            Guid loteId, Guid itemId, ClaimsPrincipal user, IClienteImportacaoService service, CancellationToken cancellationToken) =>
            ToResult(await service.ObterItemAsync(loteId, itemId, UserId(user), cancellationToken)));
        imports.MapGet("/{loteId:guid}/relatorio", RelatorioAsync);

        imports.MapPost("/previsualizar", PrevisualizarAsync).DisableAntiforgery();
        imports.MapPut("/{loteId:guid}/itens/{itemId:guid}", CorrigirAsync);
        imports.MapPatch("/{loteId:guid}/itens/{itemId:guid}/selecao", SelecionarAsync);
        imports.MapPost("/{loteId:guid}/duplicidades/resolver", ResolverAsync);
        imports.MapPost("/{loteId:guid}/confirmar", ConfirmarAsync);
        imports.MapPost("/{loteId:guid}/cancelar", CancelarAsync);

        return endpoints;
    }

    private static async Task<IResult> PrevisualizarAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery))
        {
            return CsrfFailure();
        }
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Envie a planilha como formulário multipart." });
        }
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("arquivo");
        if (file is null)
        {
            return Results.BadRequest(new { message = "Selecione a planilha .xlsx." });
        }

        await using var stream = file.OpenReadStream();
        var result = await service.PrevisualizarAsync(
            new(file.FileName, file.Length, stream), UserId(context.User), cancellationToken);
        return ToResult(result, created: true);
    }

    private static async Task<IResult> ObterAsync(
        Guid loteId,
        HttpRequest request,
        ClaimsPrincipal user,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        var query = new ImportacaoConsulta(
            request.Query["filtro"], request.Query["busca"],
            PositiveInt(request.Query["pagina"], 1), PositiveInt(request.Query["tamanhoPagina"], 50));
        return ToResult(await service.ObterAsync(loteId, UserId(user), query, cancellationToken));
    }

    private static async Task<IResult> CorrigirAsync(
        Guid loteId,
        Guid itemId,
        ImportacaoClienteDados data,
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.CorrigirAsync(loteId, itemId, UserId(context.User), data, cancellationToken));
    }

    private static async Task<IResult> SelecionarAsync(
        Guid loteId,
        Guid itemId,
        ImportacaoSelecaoRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.SelecionarAsync(loteId, itemId, UserId(context.User), request.Selecionado, cancellationToken));
    }

    private static async Task<IResult> ResolverAsync(
        Guid loteId,
        ImportacaoResolverDuplicidadeRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.ResolverDuplicidadeAsync(loteId, UserId(context.User), request, cancellationToken));
    }

    private static async Task<IResult> ConfirmarAsync(
        Guid loteId,
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        var name = context.User.FindFirstValue(ClaimTypes.Name) ?? "Usuário autenticado";
        try
        {
            return ToResult(await service.ConfirmarAsync(
                loteId, UserId(context.User), name, cancellationToken));
        }
        catch (Exception exception)
        {
            var errorCode = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            var logger = loggerFactory.CreateLogger("ImportacaoClientes");
            LogUnexpectedConfirmationFailure(logger, exception, loteId, errorCode);
            return Results.Json(
                new
                {
                    message = "Não foi possível concluir a importação. Tente novamente ou informe o código de erro.",
                    errorCode
                },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CancelarAsync(
        Guid loteId,
        HttpContext context,
        IAntiforgery antiforgery,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        if (!await ValidateCsrfAsync(context, antiforgery)) return CsrfFailure();
        return ToResult(await service.CancelarAsync(loteId, UserId(context.User), cancellationToken));
    }

    private static async Task<IResult> RelatorioAsync(
        Guid loteId,
        ClaimsPrincipal user,
        IClienteImportacaoService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RelatorioAsync(loteId, UserId(user), cancellationToken);
        if (!result.IsSuccess) return ToResult(result);
        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(result.Value!);
        var bytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);
        return Results.File(bytes, "text/csv; charset=utf-8", $"relatorio-importacao-{loteId:N}.csv");
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

    private static IResult ToResult<T>(ImportacaoOperacaoResult<T> result, bool created = false)
    {
        if (result.IsSuccess)
        {
            return created ? Results.Created($"/api/importacoes/clientes/{GetId(result.Value)}", result.Value) : Results.Ok(result.Value);
        }
        return result.ErrorKind switch
        {
            ImportacaoErroKind.NotFound => Results.NotFound(new { message = result.Message }),
            ImportacaoErroKind.Forbidden => Results.Forbid(),
            ImportacaoErroKind.Conflict => Results.Conflict(new { message = result.Message }),
            ImportacaoErroKind.Expired => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status410Gone),
            _ => Results.BadRequest(new { message = result.Message })
        };
    }

    private static string GetId<T>(T value) => value is ImportacaoLoteResponse response
        ? response.Resumo.Id.ToString("N", CultureInfo.InvariantCulture)
        : string.Empty;

    private static int UserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue("usuario_id")!, CultureInfo.InvariantCulture);

    private static int PositiveInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Falha inesperada ao confirmar importação de clientes. Lote: {LoteId}; código: {ErrorCode}.")]
    private static partial void LogUnexpectedConfirmationFailure(
        ILogger logger,
        Exception exception,
        Guid loteId,
        string errorCode);
}
