using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Web.Endpoints;

public static class ClienteEndpoints
{
    public static IEndpointRouteBuilder MapClienteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var clientes = endpoints.MapGroup("/api/clientes")
            .WithTags("Clientes")
            .RequireAuthorization();

        clientes.MapGet("", ListarAsync);
        clientes.MapGet("/{id:int}", async (int id, IClienteService service, CancellationToken cancellationToken) =>
        {
            var cliente = await service.ObterAsync(id, cancellationToken);
            return cliente is null ? Results.NotFound(new { message = "Cliente não encontrado." }) : Results.Ok(cliente);
        });
        clientes.MapPost("", async (ClienteCreateRequest request, IClienteService service, CancellationToken cancellationToken) =>
            ToHttpResult(await service.CriarAsync(request, cancellationToken), created: true));
        clientes.MapPut("/{id:int}", async (int id, ClienteUpdateRequest request, IClienteService service, CancellationToken cancellationToken) =>
            ToHttpResult(await service.AtualizarAsync(id, request, cancellationToken)));
        clientes.MapPatch("/{id:int}/inativar", async (int id, IClienteService service, CancellationToken cancellationToken) =>
            ToHttpResult(await service.InativarAsync(id, cancellationToken)));
        clientes.MapPatch("/{id:int}/reativar", async (int id, IClienteService service, CancellationToken cancellationToken) =>
            ToHttpResult(await service.ReativarAsync(id, cancellationToken)));

        return endpoints;
    }

    private static async Task<IResult> ListarAsync(
        HttpRequest request,
        IClienteService service,
        CancellationToken cancellationToken)
    {
        var query = new ClienteListQuery(
            request.Query["busca"],
            ParseTipoPessoa(request.Query["tipoPessoa"]),
            request.Query["cidade"],
            ParseAtivo(request.Query["ativo"]),
            ParsePositiveInt(request.Query["pagina"], 1),
            ParsePositiveInt(request.Query["tamanhoPagina"], 20),
            request.Query["ordenarPor"],
            request.Query["direcao"]);

        return Results.Ok(await service.ListarAsync(query, cancellationToken));
    }

    private static IResult ToHttpResult(ServiceResult<ClienteDetailsResponse> result, bool created = false)
    {
        if (result.IsSuccess)
        {
            return created
                ? Results.Created($"/api/clientes/{result.Value!.Id}", result.Value)
                : Results.Ok(result.Value);
        }

        return result.ErrorKind switch
        {
            ServiceErrorKind.NotFound => Results.NotFound(new { message = result.Errors.Values.SelectMany(item => item).First() }),
            ServiceErrorKind.Conflict => Results.Conflict(new { message = result.Errors.Values.SelectMany(item => item).First(), errors = result.Errors }),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static TipoPessoa? ParseTipoPessoa(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "1" or "pf" or "pessoafisica" => TipoPessoa.PessoaFisica,
            "2" or "pj" or "pessoajuridica" => TipoPessoa.PessoaJuridica,
            _ => null
        };

    private static bool? ParseAtivo(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "ativos" => true,
            "false" or "0" or "inativos" => false,
            "todos" => null,
            _ => null
        };

    private static int ParsePositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
