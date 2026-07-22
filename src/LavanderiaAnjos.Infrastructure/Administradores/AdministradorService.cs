using System.Data;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Infrastructure.Administradores;

public sealed partial class AdministradorService(
    LavanderiaAnjosDbContext dbContext,
    IPasswordHasher<Usuario> passwordHasher,
    ILogger<AdministradorService> logger) : IAdministradorService
{
    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    public async Task<IReadOnlyList<AdministradorResponse>> ListarAsync(CancellationToken cancellationToken) =>
        await dbContext.Usuarios.AsNoTracking()
            .OrderByDescending(usuario => usuario.Ativo)
            .ThenBy(usuario => usuario.Nome)
            .Select(usuario => ToResponse(usuario))
            .ToListAsync(cancellationToken);

    public Task<AdministradorResponse?> ObterAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Usuarios.AsNoTracking()
            .Where(usuario => usuario.Id == id)
            .Select(usuario => ToResponse(usuario))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AdministradorServiceResult<AdministradorResponse>> CriarAsync(
        AdministradorCreateRequest request,
        AdministradorAuditContext audit,
        CancellationToken cancellationToken)
    {
        var identity = AdministradorInputValidator.ValidateIdentity(request.Nome, request.Login);
        var password = AdministradorInputValidator.ValidatePassword(request.Senha, request.ConfirmacaoSenha);
        if (!identity.IsSuccess || !password.IsSuccess)
            return ValidationResult(identity.Errors, password.Errors);

        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var data = identity.Value;
            if (await LoginExistsAsync(data.LoginNormalizado, null, cancellationToken))
                return DuplicateLoginResult();

            var usuario = new Usuario
            {
                Nome = data.Nome,
                Login = data.Login,
                LoginNormalizado = data.LoginNormalizado,
                Ativo = true,
                CriadoEm = DateTime.UtcNow,
                Versao = 1
            };
            usuario.SenhaHash = passwordHasher.HashPassword(usuario, password.Value!);
            dbContext.Usuarios.Add(usuario);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                return DuplicateLoginResult();
            }

            LogAudit(logger, audit.ResponsavelId, usuario.Id, "AdministradorCriado", DateTime.UtcNow, audit.CorrelationCode);
            return AdministradorServiceResult.Success(ToResponse(usuario));
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<AdministradorServiceResult<AdministradorResponse>> AtualizarAsync(
        int id,
        AdministradorUpdateRequest request,
        AdministradorAuditContext audit,
        CancellationToken cancellationToken)
    {
        var validation = AdministradorInputValidator.ValidateIdentity(request.Nome, request.Login);
        if (!validation.IsSuccess)
            return AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Validation, validation.Errors);

        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var usuario = await dbContext.Usuarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (usuario is null) return NotFoundResult();
            if (request.Versao != usuario.Versao) return ConcurrencyResult();

            var data = validation.Value;
            if (await LoginExistsAsync(data.LoginNormalizado, id, cancellationToken))
                return DuplicateLoginResult();

            var loginChanged = !string.Equals(usuario.LoginNormalizado, data.LoginNormalizado, StringComparison.Ordinal);
            usuario.Nome = data.Nome;
            usuario.Login = data.Login;
            usuario.LoginNormalizado = data.LoginNormalizado;
            usuario.AtualizadoEm = DateTime.UtcNow;
            usuario.Versao++;
            if (loginChanged) usuario.VersaoSessao++;

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyResult();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                return DuplicateLoginResult();
            }

            LogAudit(logger, audit.ResponsavelId, usuario.Id, "AdministradorAlterado", DateTime.UtcNow, audit.CorrelationCode);
            return AdministradorServiceResult.Success(ToResponse(usuario));
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<AdministradorServiceResult<AdministradorResponse>> RedefinirSenhaAsync(
        int id,
        AdministradorPasswordRequest request,
        AdministradorAuditContext audit,
        CancellationToken cancellationToken)
    {
        var validation = AdministradorInputValidator.ValidatePassword(request.NovaSenha, request.ConfirmacaoSenha);
        if (!validation.IsSuccess)
            return AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Validation, validation.Errors);

        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var usuario = await dbContext.Usuarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (usuario is null) return NotFoundResult();
            if (request.Versao != usuario.Versao) return ConcurrencyResult();

            usuario.SenhaHash = passwordHasher.HashPassword(usuario, validation.Value!);
            usuario.VersaoSessao++;
            usuario.Versao++;
            usuario.AtualizadoEm = DateTime.UtcNow;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyResult();
            }

            LogAudit(logger, audit.ResponsavelId, usuario.Id, "SenhaRedefinida", DateTime.UtcNow, audit.CorrelationCode);
            return AdministradorServiceResult.Success(ToResponse(usuario));
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<AdministradorServiceResult<AdministradorResponse>> InativarAsync(
        int id,
        AdministradorStatusRequest request,
        AdministradorAuditContext audit,
        CancellationToken cancellationToken)
    {
        if (id == audit.ResponsavelId)
            return ForbiddenResult("Você não pode inativar a própria conta.");

        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            async Task<AdministradorServiceResult<AdministradorResponse>> Operation()
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
                var usuario = await dbContext.Usuarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (usuario is null) return NotFoundResult();
                if (request.Versao != usuario.Versao) return ConcurrencyResult();
                if (!usuario.Ativo) return AdministradorServiceResult.Success(ToResponse(usuario));

                if (await dbContext.Usuarios.CountAsync(item => item.Ativo, cancellationToken) <= 1)
                    return ForbiddenResult("Não é possível inativar o último administrador ativo.");

                usuario.Ativo = false;
                usuario.VersaoSessao++;
                usuario.Versao++;
                usuario.AtualizadoEm = DateTime.UtcNow;
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ConcurrencyResult();
                }

                LogAudit(logger, audit.ResponsavelId, usuario.Id, "AdministradorInativado", DateTime.UtcNow, audit.CorrelationCode);
                return AdministradorServiceResult.Success(ToResponse(usuario));
            }

            if (!dbContext.Database.IsRelational())
                return await Operation();

            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(Operation);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<AdministradorServiceResult<AdministradorResponse>> ReativarAsync(
        int id,
        AdministradorStatusRequest request,
        AdministradorAuditContext audit,
        CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var usuario = await dbContext.Usuarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (usuario is null) return NotFoundResult();
            if (request.Versao != usuario.Versao) return ConcurrencyResult();
            if (usuario.Ativo) return AdministradorServiceResult.Success(ToResponse(usuario));
            if (await LoginExistsAsync(usuario.LoginNormalizado, id, cancellationToken)) return DuplicateLoginResult();

            usuario.Ativo = true;
            usuario.Versao++;
            usuario.AtualizadoEm = DateTime.UtcNow;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyResult();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                return DuplicateLoginResult();
            }

            LogAudit(logger, audit.ResponsavelId, usuario.Id, "AdministradorReativado", DateTime.UtcNow, audit.CorrelationCode);
            return AdministradorServiceResult.Success(ToResponse(usuario));
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private Task<bool> LoginExistsAsync(string normalizedLogin, int? exceptId, CancellationToken cancellationToken) =>
        dbContext.Usuarios.AnyAsync(
            usuario => usuario.LoginNormalizado == normalizedLogin && (!exceptId.HasValue || usuario.Id != exceptId),
            cancellationToken);

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static AdministradorResponse ToResponse(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Login, usuario.Ativo, usuario.CriadoEm, usuario.AtualizadoEm, usuario.Versao);

    private static AdministradorServiceResult<AdministradorResponse> ValidationResult(params IReadOnlyDictionary<string, string[]>[] errorSets) =>
        AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Validation,
            errorSets.SelectMany(set => set).ToDictionary(pair => pair.Key, pair => pair.Value));

    private static AdministradorServiceResult<AdministradorResponse> DuplicateLoginResult() =>
        AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Conflict,
            new Dictionary<string, string[]> { ["login"] = ["Este login já está em uso."] });

    private static AdministradorServiceResult<AdministradorResponse> NotFoundResult() =>
        AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.NotFound,
            new Dictionary<string, string[]> { ["id"] = ["Administrador não encontrado."] });

    private static AdministradorServiceResult<AdministradorResponse> ForbiddenResult(string message) =>
        AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Forbidden,
            new Dictionary<string, string[]> { ["operacao"] = [message] });

    private static AdministradorServiceResult<AdministradorResponse> ConcurrencyResult() =>
        AdministradorServiceResult.Failure<AdministradorResponse>(AdministradorServiceErrorKind.Concurrency,
            new Dictionary<string, string[]> { ["versao"] = ["Este cadastro foi alterado por outra sessão. Recarregue a página."] });

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } ||
        exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        exception.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;

    [LoggerMessage(
        EventId = 2300,
        Level = LogLevel.Information,
        Message = "Auditoria administrativa. ResponsavelId: {ResponsavelId}; AfetadoId: {AfetadoId}; Acao: {Acao}; DataUtc: {DataUtc}; Correlacao: {CorrelationCode}.")]
    private static partial void LogAudit(
        ILogger logger,
        int responsavelId,
        int afetadoId,
        string acao,
        DateTime dataUtc,
        string correlationCode);
}
