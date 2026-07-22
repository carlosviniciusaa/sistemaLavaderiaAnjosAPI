namespace LavanderiaAnjos.Application.Administradores;

public sealed record AdministradorCreateRequest(
    string? Nome,
    string? Login,
    string? Senha,
    string? ConfirmacaoSenha);

public sealed record AdministradorUpdateRequest(string? Nome, string? Login, long Versao);

public sealed record AdministradorPasswordRequest(
    string? NovaSenha,
    string? ConfirmacaoSenha,
    long Versao);

public sealed record AdministradorStatusRequest(long Versao);

public sealed record AdministradorResponse(
    int Id,
    string Nome,
    string Login,
    bool Ativo,
    DateTime CriadoEm,
    DateTime? AtualizadoEm,
    long Versao);

public sealed record AdministradorAuditContext(
    int ResponsavelId,
    string ResponsavelNome,
    string CorrelationCode);

public sealed record AdministradorServiceResult<T>(
    T? Value,
    IReadOnlyDictionary<string, string[]> Errors,
    AdministradorServiceErrorKind ErrorKind = AdministradorServiceErrorKind.None)
{
    public bool IsSuccess => ErrorKind == AdministradorServiceErrorKind.None && Errors.Count == 0;
}

public static class AdministradorServiceResult
{
    public static AdministradorServiceResult<T> Success<T>(T value) =>
        new(value, new Dictionary<string, string[]>());

    public static AdministradorServiceResult<T> Failure<T>(
        AdministradorServiceErrorKind errorKind,
        IReadOnlyDictionary<string, string[]> errors) => new(default, errors, errorKind);
}

public enum AdministradorServiceErrorKind
{
    None,
    Validation,
    Conflict,
    NotFound,
    Forbidden,
    Concurrency
}

public interface IAdministradorService
{
    Task<IReadOnlyList<AdministradorResponse>> ListarAsync(CancellationToken cancellationToken);
    Task<AdministradorResponse?> ObterAsync(int id, CancellationToken cancellationToken);
    Task<AdministradorServiceResult<AdministradorResponse>> CriarAsync(AdministradorCreateRequest request, AdministradorAuditContext audit, CancellationToken cancellationToken);
    Task<AdministradorServiceResult<AdministradorResponse>> AtualizarAsync(int id, AdministradorUpdateRequest request, AdministradorAuditContext audit, CancellationToken cancellationToken);
    Task<AdministradorServiceResult<AdministradorResponse>> RedefinirSenhaAsync(int id, AdministradorPasswordRequest request, AdministradorAuditContext audit, CancellationToken cancellationToken);
    Task<AdministradorServiceResult<AdministradorResponse>> InativarAsync(int id, AdministradorStatusRequest request, AdministradorAuditContext audit, CancellationToken cancellationToken);
    Task<AdministradorServiceResult<AdministradorResponse>> ReativarAsync(int id, AdministradorStatusRequest request, AdministradorAuditContext audit, CancellationToken cancellationToken);
}
