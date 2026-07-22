using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Application.Clientes;

public interface IClienteRequest
{
    int? CodigoLegado { get; }
    string? NomeRazaoSocial { get; }
    string? Documento { get; }
    bool ProdutorRural { get; }
    string? InscricaoEstadual { get; }
    string? InscricaoMunicipal { get; }
    DateOnly? DataInicioRelacionamento { get; }
    string? Cep { get; }
    string? Logradouro { get; }
    string? Numero { get; }
    string? Complemento { get; }
    string? Bairro { get; }
    string? Cidade { get; }
    string? Uf { get; }
    string? Telefone { get; }
    string? EmailEnvio { get; }
    string? EmailNotaFiscal { get; }
    string? Observacoes { get; }
}

public sealed record ClienteCreateRequest(
    int? CodigoLegado,
    string? NomeRazaoSocial,
    string? Documento,
    bool ProdutorRural,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    DateOnly? DataInicioRelacionamento,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Telefone,
    string? EmailEnvio,
    string? EmailNotaFiscal,
    string? Observacoes) : IClienteRequest;

public sealed record ClienteUpdateRequest(
    int? CodigoLegado,
    string? NomeRazaoSocial,
    string? Documento,
    bool ProdutorRural,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    DateOnly? DataInicioRelacionamento,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Telefone,
    string? EmailEnvio,
    string? EmailNotaFiscal,
    string? Observacoes) : IClienteRequest;

public sealed record ClienteListItemResponse(
    int Id,
    int? CodigoLegado,
    string NomeRazaoSocial,
    string Documento,
    TipoPessoa TipoPessoa,
    string? Cidade,
    string? Telefone,
    bool Ativo);

public sealed record ClienteDetailsResponse(
    int Id,
    int? CodigoLegado,
    string NomeRazaoSocial,
    string Documento,
    TipoPessoa TipoPessoa,
    bool ProdutorRural,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    DateOnly? DataInicioRelacionamento,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Telefone,
    string? EmailEnvio,
    string? EmailNotaFiscal,
    string? Observacoes,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? AtualizadoEm);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Itens,
    int Pagina,
    int TamanhoPagina,
    int TotalItens,
    int TotalPaginas);

public sealed record ClienteListQuery(
    string? Busca,
    TipoPessoa? TipoPessoa,
    string? Cidade,
    bool? Ativo,
    int Pagina = 1,
    int TamanhoPagina = 20,
    string? OrdenarPor = null,
    string? Direcao = null);

public sealed record ServiceResult<T>(
    T? Value,
    IReadOnlyDictionary<string, string[]> Errors,
    ServiceErrorKind ErrorKind = ServiceErrorKind.None)
{
    public bool IsSuccess => ErrorKind == ServiceErrorKind.None && Errors.Count == 0;
}

public static class ServiceResult
{
    public static ServiceResult<T> Success<T>(T value) =>
        new(value, new Dictionary<string, string[]>());

    public static ServiceResult<T> Failure<T>(
        ServiceErrorKind errorKind,
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, errors, errorKind);
}

public enum ServiceErrorKind
{
    None,
    Validation,
    Conflict,
    NotFound
}

public interface IClienteService
{
    Task<PagedResult<ClienteListItemResponse>> ListarAsync(ClienteListQuery query, CancellationToken cancellationToken);
    Task<ClienteDetailsResponse?> ObterAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<ClienteDetailsResponse>> CriarAsync(ClienteCreateRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<ClienteDetailsResponse>> AtualizarAsync(int id, ClienteUpdateRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<ClienteDetailsResponse>> InativarAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<ClienteDetailsResponse>> ReativarAsync(int id, CancellationToken cancellationToken);
}
