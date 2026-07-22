using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Domain.Clientes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Infrastructure.Clientes;

public sealed partial class ClienteService(
    LavanderiaAnjosDbContext dbContext,
    ILogger<ClienteService> logger) : IClienteService
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<ClienteListItemResponse>> ListarAsync(
        ClienteListQuery query,
        CancellationToken cancellationToken)
    {
        var pagina = Math.Max(1, query.Pagina);
        var tamanhoPagina = Math.Clamp(query.TamanhoPagina, 1, MaxPageSize);
        var clientes = dbContext.Clientes.AsNoTracking().AsQueryable();

        clientes = query.Ativo.HasValue
            ? clientes.Where(cliente => cliente.Ativo == query.Ativo.Value)
            : clientes.Where(cliente => cliente.Ativo);

        if (query.TipoPessoa.HasValue)
        {
            clientes = clientes.Where(cliente => cliente.TipoPessoa == query.TipoPessoa.Value);
        }

        var cidade = query.Cidade?.Trim();
        if (!string.IsNullOrWhiteSpace(cidade))
        {
            clientes = clientes.Where(cliente => cliente.Cidade != null && cliente.Cidade.Contains(cidade));
        }

        var busca = query.Busca?.Trim();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var documento = DocumentoFiscal.Normalize(busca);
            var telefone = NormalizePhoneSearch(busca);
            var codigoLegado = int.TryParse(busca, out var codigo) ? codigo : (int?)null;
            clientes = clientes.Where(cliente =>
                cliente.NomeRazaoSocial.Contains(busca) ||
                cliente.Cidade != null && cliente.Cidade.Contains(busca) ||
                documento.Length > 0 && cliente.Documento.Contains(documento) ||
                telefone.Length > 0 && cliente.Telefone != null && cliente.Telefone.Contains(telefone) ||
                codigoLegado.HasValue && cliente.CodigoLegado == codigoLegado);
        }

        clientes = ApplyOrdering(clientes, query.OrdenarPor, query.Direcao);
        var totalItens = await clientes.CountAsync(cancellationToken);
        var itens = await clientes
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(cliente => new ClienteListItemResponse(
                cliente.Id,
                cliente.CodigoLegado,
                cliente.NomeRazaoSocial,
                cliente.Documento,
                cliente.TipoPessoa,
                cliente.Cidade,
                cliente.Telefone,
                cliente.Ativo))
            .ToListAsync(cancellationToken);

        return new PagedResult<ClienteListItemResponse>(
            itens,
            pagina,
            tamanhoPagina,
            totalItens,
            totalItens == 0 ? 0 : (int)Math.Ceiling(totalItens / (double)tamanhoPagina));
    }

    public async Task<ClienteDetailsResponse?> ObterAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Clientes
            .AsNoTracking()
            .Where(cliente => cliente.Id == id)
            .Select(cliente => ToDetails(cliente))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ServiceResult<ClienteDetailsResponse>> CriarAsync(
        ClienteCreateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ClienteInputValidator.Validate(request);
        if (!validation.IsSuccess)
        {
            return ServiceResult.Failure<ClienteDetailsResponse>(validation.ErrorKind, validation.Errors);
        }

        var data = validation.Value!;
        if (await DocumentoAtivoExistsAsync(data.Documento, null, cancellationToken))
        {
            return DuplicateDocumentResult();
        }

        var cliente = new Cliente { Ativo = true, CriadoEm = DateTime.UtcNow };
        Apply(cliente, data);
        dbContext.Clientes.Add(cliente);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return DuplicateDocumentResult();
        }

        LogCreated(logger, cliente.Id);
        return ServiceResult.Success(ToDetails(cliente));
    }

    public async Task<ServiceResult<ClienteDetailsResponse>> AtualizarAsync(
        int id,
        ClienteUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ClienteInputValidator.Validate(request);
        if (!validation.IsSuccess)
        {
            return ServiceResult.Failure<ClienteDetailsResponse>(validation.ErrorKind, validation.Errors);
        }

        var cliente = await dbContext.Clientes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (cliente is null)
        {
            return NotFoundResult();
        }

        var data = validation.Value!;
        if (cliente.Ativo && await DocumentoAtivoExistsAsync(data.Documento, id, cancellationToken))
        {
            return DuplicateDocumentResult();
        }

        Apply(cliente, data);
        cliente.AtualizadoEm = DateTime.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return DuplicateDocumentResult();
        }

        LogUpdated(logger, cliente.Id);
        return ServiceResult.Success(ToDetails(cliente));
    }

    public async Task<ServiceResult<ClienteDetailsResponse>> InativarAsync(int id, CancellationToken cancellationToken)
    {
        var cliente = await dbContext.Clientes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (cliente is null)
        {
            return NotFoundResult();
        }

        if (cliente.Ativo)
        {
            cliente.Ativo = false;
            cliente.AtualizadoEm = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            LogDeactivated(logger, cliente.Id);
        }

        return ServiceResult.Success(ToDetails(cliente));
    }

    public async Task<ServiceResult<ClienteDetailsResponse>> ReativarAsync(int id, CancellationToken cancellationToken)
    {
        var cliente = await dbContext.Clientes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (cliente is null)
        {
            return NotFoundResult();
        }

        if (cliente.Ativo)
        {
            return ServiceResult.Success(ToDetails(cliente));
        }

        if (await DocumentoAtivoExistsAsync(cliente.Documento, id, cancellationToken))
        {
            return DuplicateDocumentResult("Não é possível reativar: outro cliente ativo já utiliza este CPF ou CNPJ.");
        }

        cliente.Ativo = true;
        cliente.AtualizadoEm = DateTime.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return DuplicateDocumentResult("Não é possível reativar: outro cliente ativo já utiliza este CPF ou CNPJ.");
        }

        LogReactivated(logger, cliente.Id);
        return ServiceResult.Success(ToDetails(cliente));
    }

    private Task<bool> DocumentoAtivoExistsAsync(string documento, int? exceptId, CancellationToken cancellationToken) =>
        dbContext.Clientes.AnyAsync(
            cliente => cliente.Ativo && cliente.Documento == documento && (!exceptId.HasValue || cliente.Id != exceptId),
            cancellationToken);

    private static IQueryable<Cliente> ApplyOrdering(IQueryable<Cliente> clientes, string? field, string? direction)
    {
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        return (field?.Trim().ToLowerInvariant(), descending) switch
        {
            ("cidade", true) => clientes.OrderByDescending(cliente => cliente.Cidade).ThenBy(cliente => cliente.NomeRazaoSocial),
            ("cidade", false) => clientes.OrderBy(cliente => cliente.Cidade).ThenBy(cliente => cliente.NomeRazaoSocial),
            ("documento", true) => clientes.OrderByDescending(cliente => cliente.Documento),
            ("documento", false) => clientes.OrderBy(cliente => cliente.Documento),
            ("criadoem", true) => clientes.OrderByDescending(cliente => cliente.CriadoEm),
            ("criadoem", false) => clientes.OrderBy(cliente => cliente.CriadoEm),
            ("nome", true) or ("nomerazaosocial", true) => clientes.OrderByDescending(cliente => cliente.NomeRazaoSocial),
            _ => clientes.OrderBy(cliente => cliente.NomeRazaoSocial)
        };
    }

    private static void Apply(Cliente cliente, NormalizedClienteData data)
    {
        cliente.CodigoLegado = data.CodigoLegado;
        cliente.NomeRazaoSocial = data.NomeRazaoSocial;
        cliente.Documento = data.Documento;
        cliente.TipoPessoa = data.TipoPessoa;
        cliente.ProdutorRural = data.ProdutorRural;
        cliente.InscricaoEstadual = data.InscricaoEstadual;
        cliente.InscricaoMunicipal = data.InscricaoMunicipal;
        cliente.DataInicioRelacionamento = data.DataInicioRelacionamento;
        cliente.Cep = data.Cep;
        cliente.Logradouro = data.Logradouro;
        cliente.Numero = data.Numero;
        cliente.Complemento = data.Complemento;
        cliente.Bairro = data.Bairro;
        cliente.Cidade = data.Cidade;
        cliente.Uf = data.Uf;
        cliente.Telefone = data.Telefone;
        cliente.EmailEnvio = data.EmailEnvio;
        cliente.EmailNotaFiscal = data.EmailNotaFiscal;
        cliente.Observacoes = data.Observacoes;
    }

    private static ClienteDetailsResponse ToDetails(Cliente cliente) =>
        new(
            cliente.Id, cliente.CodigoLegado, cliente.NomeRazaoSocial, cliente.Documento,
            cliente.TipoPessoa, cliente.ProdutorRural, cliente.InscricaoEstadual,
            cliente.InscricaoMunicipal, cliente.DataInicioRelacionamento, cliente.Cep,
            cliente.Logradouro, cliente.Numero, cliente.Complemento, cliente.Bairro,
            cliente.Cidade, cliente.Uf, cliente.Telefone, cliente.EmailEnvio,
            cliente.EmailNotaFiscal, cliente.Observacoes, cliente.Ativo, cliente.CriadoEm,
            cliente.AtualizadoEm);

    private static ServiceResult<ClienteDetailsResponse> DuplicateDocumentResult(
        string message = "Já existe um cliente ativo com este CPF ou CNPJ.") =>
        ServiceResult.Failure<ClienteDetailsResponse>(
            ServiceErrorKind.Conflict,
            new Dictionary<string, string[]> { ["documento"] = [message] });

    private static ServiceResult<ClienteDetailsResponse> NotFoundResult() =>
        ServiceResult.Failure<ClienteDetailsResponse>(
            ServiceErrorKind.NotFound,
            new Dictionary<string, string[]> { ["id"] = ["Cliente não encontrado."] });

    private static string NormalizePhoneSearch(string value) =>
        string.Concat(value.Where(character => character is not (' ' or '(' or ')' or '-')));

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        exception.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "Cliente criado. Id: {ClienteId}.")]
    private static partial void LogCreated(ILogger logger, int clienteId);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Cliente editado. Id: {ClienteId}.")]
    private static partial void LogUpdated(ILogger logger, int clienteId);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information, Message = "Cliente inativado. Id: {ClienteId}.")]
    private static partial void LogDeactivated(ILogger logger, int clienteId);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Cliente reativado. Id: {ClienteId}.")]
    private static partial void LogReactivated(ILogger logger, int clienteId);
}
