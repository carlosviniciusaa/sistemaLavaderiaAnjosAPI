using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Application.Importacoes;
using LavanderiaAnjos.Domain.Clientes;
using LavanderiaAnjos.Domain.Importacoes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LavanderiaAnjos.Infrastructure.Importacoes;

public sealed class ClienteImportacaoService(
    LavanderiaAnjosDbContext dbContext,
    IClientePlanilhaReader reader,
    IImportacaoFileStore fileStore) : IClienteImportacaoService
{
    private const int MaxPageSize = 100;
    private const int DocumentQueryChunkSize = 1_000;

    public async Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> PrevisualizarAsync(
        ImportacaoArquivoEntrada arquivo,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        await ExpireAbandonedLotsAsync(cancellationToken);
        if (!string.Equals(Path.GetExtension(arquivo.NomeArquivo), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Failure<ImportacaoLoteResponse>("Selecione um arquivo com extensão .xlsx.");
        }
        if (arquivo.Tamanho is <= 0 or > 10 * 1024 * 1024)
        {
            return Failure<ImportacaoLoteResponse>("O arquivo deve ter até 10 MB.");
        }

        await using var copy = new MemoryStream();
        await arquivo.Conteudo.CopyToAsync(copy, cancellationToken);
        if (copy.Length != arquivo.Tamanho || copy.Length > 10 * 1024 * 1024)
        {
            return Failure<ImportacaoLoteResponse>("O tamanho recebido para o arquivo é inválido.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(copy.ToArray()));
        copy.Position = 0;
        ImportacaoLeituraResultado readResult;
        try
        {
            readResult = reader.Ler(copy);
        }
        catch (InvalidDataException exception)
        {
            return Failure<ImportacaoLoteResponse>(exception.Message);
        }

        var now = DateTime.UtcNow;
        var lot = new ImportacaoClienteLote
        {
            Id = Guid.NewGuid(),
            HashArquivo = hash,
            NomeArquivo = Path.GetFileName(arquivo.NomeArquivo),
            Status = ImportacaoLoteStatus.PreVisualizacao,
            CriadoEm = now,
            ExpiraEm = now.AddHours(24),
            UsuarioId = usuarioId
        };
        var stage = new ImportacaoLoteEstagio
        {
            LoteId = lot.Id,
            UsuarioId = usuarioId,
            HashArquivo = hash,
            NomeArquivo = lot.NomeArquivo,
            CriadoEm = now,
            ExpiraEm = lot.ExpiraEm,
            Metricas = readResult.Metricas,
            Itens = [.. readResult.Itens]
        };

        await RecalculateAsync(stage, initialSelection: true, cancellationToken);
        UpdateSummary(lot, stage);
        dbContext.ImportacoesClientes.Add(lot);
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            await fileStore.SaveStageAsync(stage, cancellationToken);
        }
        catch
        {
            dbContext.ImportacoesClientes.Remove(lot);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        return Success(ToResponse(lot, stage, new(null, null)));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ObterAsync(
        Guid loteId,
        int usuarioId,
        ImportacaoConsulta consulta,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        return loaded.Error is not null
            ? Failure<ImportacaoLoteResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind)
            : Success(ToResponse(loaded.Lot!, loaded.Stage!, consulta));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoItemEdicaoResponse>> ObterItemAsync(
        Guid loteId,
        Guid itemId,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ImportacaoItemEdicaoResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind);
        }
        var item = loaded.Stage!.Itens.SingleOrDefault(candidate => candidate.Id == itemId);
        return item is null
            ? Failure<ImportacaoItemEdicaoResponse>("Item não encontrado.", ImportacaoErroKind.NotFound)
            : Success(new ImportacaoItemEdicaoResponse(item.Id, item.Aba, item.Linha, item.Dados, item.Erros, item.Alertas));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> CorrigirAsync(
        Guid loteId,
        Guid itemId,
        int usuarioId,
        ImportacaoClienteDados dados,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ImportacaoLoteResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind);
        }
        var item = loaded.Stage!.Itens.SingleOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return Failure<ImportacaoLoteResponse>("Item não encontrado.", ImportacaoErroKind.NotFound);
        }

        item.Dados = dados;
        item.DocumentoEncontrado = dados.Documento;
        item.Corrigido = true;
        item.Ignorado = false;
        await RecalculateAsync(loaded.Stage, initialSelection: false, cancellationToken);
        await PersistStageAndSummaryAsync(loaded.Lot!, loaded.Stage, cancellationToken);
        return Success(ToResponse(loaded.Lot!, loaded.Stage, new(null, null)));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> SelecionarAsync(
        Guid loteId,
        Guid itemId,
        int usuarioId,
        bool selecionado,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ImportacaoLoteResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind);
        }
        var item = loaded.Stage!.Itens.SingleOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return Failure<ImportacaoLoteResponse>("Item não encontrado.", ImportacaoErroKind.NotFound);
        }
        if (selecionado && !CanSelect(item))
        {
            return Failure<ImportacaoLoteResponse>("O registro possui bloqueios e não pode ser selecionado.");
        }

        item.Selecionado = selecionado;
        item.Ignorado = !selecionado;
        await PersistStageAndSummaryAsync(loaded.Lot!, loaded.Stage, cancellationToken);
        return Success(ToResponse(loaded.Lot!, loaded.Stage, new(null, null)));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ResolverDuplicidadeAsync(
        Guid loteId,
        int usuarioId,
        ImportacaoResolverDuplicidadeRequest request,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ImportacaoLoteResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind);
        }
        var group = loaded.Stage!.Itens.Where(item => item.GrupoDuplicidade == request.GrupoDuplicidade).ToList();
        var principal = group.SingleOrDefault(item => item.Id == request.ItemPrincipalId);
        if (group.Count < 2 || principal is null)
        {
            return Failure<ImportacaoLoteResponse>("Grupo de duplicidade ou linha principal inválido.");
        }

        foreach (var item in group)
        {
            item.GrupoDuplicidade = null;
            item.Ignorado = item.Id != principal.Id;
            item.Selecionado = item.Id == principal.Id && item.Erros.Count == 0 && item.ClienteExistenteId is null;
        }
        await PersistStageAndSummaryAsync(loaded.Lot!, loaded.Stage, cancellationToken);
        return Success(ToResponse(loaded.Lot!, loaded.Stage, new(null, null)));
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoResultadoResponse>> ConfirmarAsync(
        Guid loteId,
        int usuarioId,
        string usuarioNome,
        CancellationToken cancellationToken)
    {
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        var rollbackGuaranteed = false;
        var commitAttempted = false;
        ConfirmationExecution execution;

        try
        {
            execution = await executionStrategy.ExecuteAsync(async () =>
            {
                // The delegate may run again after a transient failure. Never carry Added
                // clients or a stale tracked lot into the next attempt.
                dbContext.ChangeTracker.Clear();
                rollbackGuaranteed = false;
                commitAttempted = false;

                var stage = await fileStore.ReadStageAsync(loteId, cancellationToken);
                IDbContextTransaction? transaction = null;
                if (dbContext.Database.IsRelational())
                {
                    transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                }

                await using (transaction)
                {
                    async Task<ConfirmationExecution> RejectAsync(string message, ImportacaoErroKind kind)
                    {
                        if (transaction is not null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                        }
                        dbContext.ChangeTracker.Clear();
                        return new(Failure<ImportacaoResultadoResponse>(message, kind), null, null);
                    }

                    try
                    {
                        var lot = await dbContext.ImportacoesClientes.SingleOrDefaultAsync(
                            candidate => candidate.Id == loteId,
                            cancellationToken);
                        if (lot is null)
                        {
                            return await RejectAsync("Lote não encontrado.", ImportacaoErroKind.NotFound);
                        }
                        if (lot.UsuarioId != usuarioId)
                        {
                            return await RejectAsync("Você não pode acessar este lote.", ImportacaoErroKind.Forbidden);
                        }
                        if (lot.Status == ImportacaoLoteStatus.Concluida || lot.ConfirmadoEm is not null)
                        {
                            return await RejectAsync("Este lote já foi confirmado.", ImportacaoErroKind.Conflict);
                        }
                        if (lot.Status != ImportacaoLoteStatus.PreVisualizacao)
                        {
                            return await RejectAsync("Este lote não está mais em pré-visualização.", ImportacaoErroKind.Conflict);
                        }
                        if (lot.ExpiraEm <= DateTime.UtcNow)
                        {
                            return await RejectAsync("O lote expirou. Envie a planilha novamente.", ImportacaoErroKind.Expired);
                        }
                        if (stage is null)
                        {
                            return await RejectAsync(
                                "Os dados temporários deste lote não estão disponíveis.",
                                ImportacaoErroKind.NotFound);
                        }
                        if (stage.UsuarioId != usuarioId || stage.LoteId != loteId || stage.ExpiraEm <= DateTime.UtcNow ||
                            !CryptographicOperations.FixedTimeEquals(
                                Encoding.ASCII.GetBytes(stage.HashArquivo),
                                Encoding.ASCII.GetBytes(lot.HashArquivo)))
                        {
                            return await RejectAsync(
                                "A integridade do lote não pôde ser confirmada.",
                                ImportacaoErroKind.Forbidden);
                        }
                        if (await dbContext.ImportacoesClientes.AsNoTracking().AnyAsync(
                            candidate => candidate.HashArquivo == stage.HashArquivo &&
                                candidate.Status == ImportacaoLoteStatus.Concluida &&
                                candidate.Id != loteId,
                            cancellationToken))
                        {
                            return await RejectAsync(
                                "Este mesmo arquivo já possui uma importação concluída.",
                                ImportacaoErroKind.Conflict);
                        }

                        var selectedIds = stage.Itens
                            .Where(item => item.Selecionado)
                            .Select(item => item.Id)
                            .ToHashSet();
                        if (selectedIds.Count == 0)
                        {
                            return await RejectAsync(
                                "Selecione ao menos um cliente válido para importar.",
                                ImportacaoErroKind.Validation);
                        }

                        await RecalculateAsync(stage, initialSelection: false, cancellationToken);
                        var selected = stage.Itens.Where(item => selectedIds.Contains(item.Id)).ToList();
                        if (selected.Count != selectedIds.Count || selected.Any(item => !CanSelect(item)))
                        {
                            UpdateSummary(lot, stage);
                            await dbContext.SaveChangesAsync(cancellationToken);
                            if (transaction is not null)
                            {
                                commitAttempted = true;
                                await transaction.CommitAsync(cancellationToken);
                            }
                            return new(
                                Failure<ImportacaoResultadoResponse>(
                                    "A pré-visualização mudou: há registros selecionados com bloqueios. Revise-os antes de confirmar.",
                                    ImportacaoErroKind.Conflict),
                                null,
                                stage);
                        }

                        var distinctSelectedDocuments = selected
                            .Select(item => DocumentoFiscal.Normalize(item.Dados.Documento))
                            .Distinct(StringComparer.Ordinal)
                            .Count();
                        if (distinctSelectedDocuments != selected.Count)
                        {
                            return await RejectAsync(
                                "A confirmação foi bloqueada porque há documentos repetidos entre os registros selecionados.",
                                ImportacaoErroKind.Conflict);
                        }

                        var now = DateTime.UtcNow;
                        foreach (var item in selected)
                        {
                            var validation = ClienteInputValidator.Validate(item.Dados);
                            if (!validation.IsSuccess)
                            {
                                return await RejectAsync(
                                    "O lote possui dados inválidos. Revise a pré-visualização antes de confirmar.",
                                    ImportacaoErroKind.Validation);
                            }

                            dbContext.Clientes.Add(ToCliente(validation.Value!, now));
                            item.Importado = true;
                            item.Resultado = "Importado";
                        }

                        lot.Status = ImportacaoLoteStatus.Concluida;
                        lot.ConfirmadoEm = now;
                        UpdateSummary(lot, stage);
                        lot.QuantidadeImportada = selected.Count;

                        var result = Success(new ImportacaoResultadoResponse(
                            loteId, lot.Status, lot.ConfirmadoEm, lot.QuantidadeImportada,
                            lot.QuantidadeIgnorada, lot.QuantidadeInvalida, lot.QuantidadeDuplicada,
                            lot.QuantidadeExistente, usuarioNome));
                        var report = BuildReport(stage);

                        await dbContext.SaveChangesAsync(cancellationToken);
                        if (transaction is not null)
                        {
                            commitAttempted = true;
                            await transaction.CommitAsync(cancellationToken);
                        }

                        return new ConfirmationExecution(result, report, null);
                    }
                    catch
                    {
                        if (transaction is not null)
                        {
                            try
                            {
                                await transaction.RollbackAsync(cancellationToken);
                                rollbackGuaranteed = !commitAttempted;
                            }
                            catch
                            {
                                rollbackGuaranteed = false;
                            }
                        }

                        dbContext.ChangeTracker.Clear();
                        throw;
                    }
                }
            });
        }
        catch (DbUpdateException) when (rollbackGuaranteed)
        {
            dbContext.ChangeTracker.Clear();
            return Failure<ImportacaoResultadoResponse>(
                "A importação foi revertida porque o banco rejeitou um dos registros. Nenhum cliente deste lote foi gravado.",
                ImportacaoErroKind.Conflict);
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }

        if (execution.StageToSave is not null)
        {
            await fileStore.SaveStageAsync(execution.StageToSave, cancellationToken);
        }
        if (!execution.Result.IsSuccess)
        {
            return execution.Result;
        }

        await fileStore.SaveReportAsync(loteId, execution.Report!, cancellationToken);
        await fileStore.DeleteStageAsync(loteId, cancellationToken);
        return execution.Result;
    }

    public async Task<ImportacaoOperacaoResult<ImportacaoResumoResponse>> CancelarAsync(
        Guid loteId,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(loteId, usuarioId, cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ImportacaoResumoResponse>(loaded.Error.Value.Message, loaded.Error.Value.Kind);
        }
        loaded.Lot!.Status = ImportacaoLoteStatus.Cancelada;
        loaded.Stage!.Itens.ForEach(item => item.Resultado = item.Ignorado ? "Ignorado" : "Cancelado");
        UpdateSummary(loaded.Lot, loaded.Stage);
        await dbContext.SaveChangesAsync(cancellationToken);
        await fileStore.SaveReportAsync(loteId, BuildReport(loaded.Stage), cancellationToken);
        await fileStore.DeleteStageAsync(loteId, cancellationToken);
        return Success(ToSummary(loaded.Lot));
    }

    public async Task<IReadOnlyList<ImportacaoResumoResponse>> HistoricoAsync(int usuarioId, CancellationToken cancellationToken)
    {
        await ExpireAbandonedLotsAsync(cancellationToken);
        return await dbContext.ImportacoesClientes.AsNoTracking()
            .Where(lot => lot.UsuarioId == usuarioId)
            .OrderByDescending(lot => lot.CriadoEm)
            .Take(30)
            .Select(lot => ToSummary(lot))
            .ToListAsync(cancellationToken);
    }

    public async Task<ImportacaoOperacaoResult<string>> RelatorioAsync(Guid loteId, int usuarioId, CancellationToken cancellationToken)
    {
        var lot = await dbContext.ImportacoesClientes.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == loteId, cancellationToken);
        if (lot is null)
        {
            return Failure<string>("Lote não encontrado.", ImportacaoErroKind.NotFound);
        }
        if (lot.UsuarioId != usuarioId)
        {
            return Failure<string>("Você não pode acessar este lote.", ImportacaoErroKind.Forbidden);
        }
        var report = await fileStore.ReadReportAsync(loteId, cancellationToken);
        return report is null
            ? Failure<string>("O relatório ainda não está disponível.", ImportacaoErroKind.NotFound)
            : Success(report);
    }

    private async Task RecalculateAsync(ImportacaoLoteEstagio stage, bool initialSelection, CancellationToken cancellationToken)
    {
        foreach (var item in stage.Itens)
        {
            item.Erros = [.. item.ErrosLeitura];
            item.Alertas.Clear();
            item.GrupoDuplicidade = null;
            item.ClienteExistenteId = null;
            item.ClienteExistenteAtivo = null;

            var validation = ClienteInputValidator.Validate(item.Dados);
            if (!validation.IsSuccess)
            {
                item.Erros.AddRange(validation.Errors.Values.SelectMany(messages => messages));
            }
            AddWarnings(item);
        }

        var validDocuments = stage.Itens
            .Select(item => DocumentoFiscal.Normalize(item.Dados.Documento))
            .Where(document => document.Length is 11 or 14)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existing = new List<ExistingClient>();
        foreach (var documentChunk in validDocuments.Chunk(DocumentQueryChunkSize))
        {
            existing.AddRange(await dbContext.Clientes.AsNoTracking()
                .Where(client => documentChunk.Contains(client.Documento))
                .Select(client => new ExistingClient(client.Id, client.Documento, client.Ativo))
                .ToListAsync(cancellationToken));
        }
        var existingByDocument = existing.GroupBy(client => client.Documento).ToDictionary(group => group.Key, group => group.First());

        foreach (var item in stage.Itens)
        {
            var document = DocumentoFiscal.Normalize(item.Dados.Documento);
            if (existingByDocument.TryGetValue(document, out var client))
            {
                item.ClienteExistenteId = client.Id;
                item.ClienteExistenteAtivo = client.Ativo;
            }
        }

        foreach (var group in stage.Itens
            .Where(item => DocumentoFiscal.Normalize(item.Dados.Documento).Length is 11 or 14 && !item.Ignorado)
            .GroupBy(item => DocumentoFiscal.Normalize(item.Dados.Documento), StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            var groupId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..16];
            foreach (var item in group)
            {
                item.GrupoDuplicidade = groupId;
            }
            if (group.Select(item => item.Dados.NomeRazaoSocial?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                foreach (var item in group)
                {
                    item.Alertas.Add("O mesmo documento aparece com grafias diferentes de nome.");
                }
            }
        }

        foreach (var item in stage.Itens)
        {
            if (initialSelection)
            {
                item.Selecionado = CanSelect(item);
                item.Ignorado = !item.Selecionado;
            }
            else if (!CanSelect(item))
            {
                item.Selecionado = false;
            }
        }
    }

    private static void AddWarnings(ImportacaoItemEstagio item)
    {
        var data = item.Dados;
        var phoneDigits = DocumentoFiscal.Normalize(data.Telefone);
        if (phoneDigits.Length == 0)
        {
            item.Alertas.Add("Cliente sem telefone.");
        }
        else if (phoneDigits.Length is < 10 or > 11)
        {
            item.Alertas.Add("Telefone com quantidade incomum de dígitos.");
        }

        var cepDigits = DocumentoFiscal.Normalize(data.Cep);
        if (cepDigits.Length is > 0 and < 8)
        {
            item.Alertas.Add("CEP incompleto.");
        }
        if (string.IsNullOrWhiteSpace(data.EmailEnvio) && string.IsNullOrWhiteSpace(data.EmailNotaFiscal))
        {
            item.Alertas.Add("Cliente sem e-mail.");
        }

        var address = new[] { data.Logradouro, data.Numero, data.Bairro, data.Cidade, data.Uf };
        var filled = address.Count(value => !string.IsNullOrWhiteSpace(value));
        if (filled is > 0 and < 5)
        {
            item.Alertas.Add("Endereço parcialmente preenchido.");
        }
    }

    private async Task PersistStageAndSummaryAsync(ImportacaoClienteLote lot, ImportacaoLoteEstagio stage, CancellationToken cancellationToken)
    {
        UpdateSummary(lot, stage);
        await dbContext.SaveChangesAsync(cancellationToken);
        await fileStore.SaveStageAsync(stage, cancellationToken);
    }

    private async Task ExpireAbandonedLotsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expired = await dbContext.ImportacoesClientes
            .Where(lot => lot.Status == ImportacaoLoteStatus.PreVisualizacao && lot.ExpiraEm <= now)
            .ToListAsync(cancellationToken);
        foreach (var lot in expired)
        {
            lot.Status = ImportacaoLoteStatus.Expirada;
            await fileStore.DeleteStageAsync(lot.Id, cancellationToken);
        }
        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await fileStore.CleanupAsync(now, cancellationToken);
    }

    private async Task<(ImportacaoClienteLote? Lot, ImportacaoLoteEstagio? Stage, (string Message, ImportacaoErroKind Kind)? Error)> LoadAsync(
        Guid id,
        int userId,
        CancellationToken cancellationToken)
    {
        var lot = await dbContext.ImportacoesClientes.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (lot is null)
        {
            return (null, null, ("Lote não encontrado.", ImportacaoErroKind.NotFound));
        }
        if (lot.UsuarioId != userId)
        {
            return (null, null, ("Você não pode acessar este lote.", ImportacaoErroKind.Forbidden));
        }
        if (lot.Status != ImportacaoLoteStatus.PreVisualizacao)
        {
            return (null, null, ("Este lote não está mais em pré-visualização.", ImportacaoErroKind.Conflict));
        }
        if (lot.ExpiraEm <= DateTime.UtcNow)
        {
            lot.Status = ImportacaoLoteStatus.Expirada;
            await dbContext.SaveChangesAsync(cancellationToken);
            await fileStore.DeleteStageAsync(id, cancellationToken);
            return (null, null, ("O lote expirou. Envie a planilha novamente.", ImportacaoErroKind.Expired));
        }

        var stage = await fileStore.ReadStageAsync(id, cancellationToken);
        if (stage is null)
        {
            return (null, null, ("Os dados temporários deste lote não estão disponíveis.", ImportacaoErroKind.NotFound));
        }
        if (stage.UsuarioId != userId || stage.LoteId != id || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stage.HashArquivo), Encoding.ASCII.GetBytes(lot.HashArquivo)))
        {
            return (null, null, ("A integridade do lote não pôde ser confirmada.", ImportacaoErroKind.Forbidden));
        }
        return (lot, stage, null);
    }

    private static ImportacaoLoteResponse ToResponse(ImportacaoClienteLote lot, ImportacaoLoteEstagio stage, ImportacaoConsulta query)
    {
        var page = Math.Max(1, query.Pagina);
        var pageSize = Math.Clamp(query.TamanhoPagina, 1, MaxPageSize);
        IEnumerable<ImportacaoItemEstagio> filtered = stage.Itens;
        filtered = query.Filtro?.Trim().ToLowerInvariant() switch
        {
            "validos" => filtered.Where(item => item.Erros.Count == 0 && item.GrupoDuplicidade is null && item.ClienteExistenteId is null),
            "alertas" => filtered.Where(item => item.Alertas.Count > 0),
            "invalidos" => filtered.Where(item => item.Erros.Count > 0),
            "duplicados" => filtered.Where(item => item.GrupoDuplicidade is not null),
            "existentes" => filtered.Where(item => item.ClienteExistenteId is not null),
            "selecionados" => filtered.Where(item => item.Selecionado),
            "ignorados" => filtered.Where(item => item.Ignorado),
            _ => filtered
        };
        var search = query.Busca?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var document = DocumentoFiscal.Normalize(search);
            filtered = filtered.Where(item =>
                item.Dados.NomeRazaoSocial?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                document.Length > 0 && DocumentoFiscal.Normalize(item.Dados.Documento).Contains(document, StringComparison.Ordinal) ||
                item.Aba.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Linha.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal));
        }

        var all = filtered.OrderBy(item => item.Aba).ThenBy(item => item.Linha).ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(ToItem).ToList();
        return new(
            ToSummary(lot),
            stage.Metricas,
            new(items, page, pageSize, all.Count, all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)pageSize)));
    }

    private static ImportacaoItemResponse ToItem(ImportacaoItemEstagio item)
    {
        var normalized = DocumentoFiscal.Normalize(item.Dados.Documento);
        DocumentoFiscal.TryValidate(normalized, out var type);
        return new(
            item.Id, item.Aba, item.Linha, item.Dados.NomeRazaoSocial,
            Mask(item.DocumentoEncontrado), Mask(normalized),
            normalized.Length is 11 or 14 ? type : null, item.Dados.Cidade,
            Status(item), [.. item.Erros], [.. item.Alertas], item.GrupoDuplicidade,
            item.ClienteExistenteId, item.ClienteExistenteAtivo, item.Selecionado,
            item.Ignorado, CanSelect(item), item.Corrigido);
    }

    private static string Status(ImportacaoItemEstagio item) =>
        item.Importado ? "Importado" :
        item.Ignorado ? "Ignorado" :
        item.ClienteExistenteId is not null ? item.ClienteExistenteAtivo == true ? "Já existe no sistema" : "Já existe inativo no sistema" :
        item.GrupoDuplicidade is not null ? "Duplicado na planilha" :
        item.Erros.Count > 0 ? "Inválido" :
        item.Corrigido ? "Corrigido" :
        item.Alertas.Count > 0 ? "Válido com alerta" :
        item.Selecionado ? "Selecionado" : "Válido";

    private static bool CanSelect(ImportacaoItemEstagio item) =>
        item.Erros.Count == 0 && item.GrupoDuplicidade is null && item.ClienteExistenteId is null;

    private static void UpdateSummary(ImportacaoClienteLote lot, ImportacaoLoteEstagio stage)
    {
        lot.QuantidadeTotal = stage.Itens.Count;
        lot.QuantidadeValida = stage.Itens.Count(item => item.Erros.Count == 0 && item.GrupoDuplicidade is null && item.ClienteExistenteId is null);
        lot.QuantidadeComAlerta = stage.Itens.Count(item => item.Alertas.Count > 0);
        lot.QuantidadeInvalida = stage.Itens.Count(item => item.Erros.Count > 0);
        lot.QuantidadeDuplicada = stage.Itens.Count(item => item.GrupoDuplicidade is not null);
        lot.QuantidadeExistente = stage.Itens.Count(item => item.ClienteExistenteId is not null);
        lot.QuantidadeSelecionada = stage.Itens.Count(item => item.Selecionado);
        lot.QuantidadeIgnorada = stage.Itens.Count(item => item.Ignorado);
        lot.QuantidadeImportada = stage.Itens.Count(item => item.Importado);
    }

    private static ImportacaoResumoResponse ToSummary(ImportacaoClienteLote lot) => new(
        lot.Id, lot.NomeArquivo, lot.HashArquivo[..Math.Min(12, lot.HashArquivo.Length)], lot.Status,
        lot.CriadoEm, lot.ExpiraEm, lot.ConfirmadoEm, lot.QuantidadeTotal, lot.QuantidadeValida,
        lot.QuantidadeComAlerta, lot.QuantidadeInvalida, lot.QuantidadeDuplicada,
        lot.QuantidadeExistente, lot.QuantidadeSelecionada, lot.QuantidadeImportada, lot.QuantidadeIgnorada);

    private static Cliente ToCliente(NormalizedClienteData data, DateTime now) => new()
    {
        CodigoLegado = data.CodigoLegado,
        NomeRazaoSocial = data.NomeRazaoSocial,
        Documento = data.Documento,
        TipoPessoa = data.TipoPessoa,
        ProdutorRural = data.ProdutorRural,
        InscricaoEstadual = data.InscricaoEstadual,
        InscricaoMunicipal = data.InscricaoMunicipal,
        DataInicioRelacionamento = data.DataInicioRelacionamento,
        Cep = data.Cep,
        Logradouro = data.Logradouro,
        Numero = data.Numero,
        Complemento = data.Complemento,
        Bairro = data.Bairro,
        Cidade = data.Cidade,
        Uf = data.Uf,
        Telefone = data.Telefone,
        EmailEnvio = data.EmailEnvio,
        EmailNotaFiscal = data.EmailNotaFiscal,
        Observacoes = data.Observacoes,
        Ativo = true,
        CriadoEm = now
    };

    private static string BuildReport(ImportacaoLoteEstagio stage)
    {
        var builder = new StringBuilder("Aba,Linha,Nome,DocumentoMascarado,Resultado,Motivo\r\n");
        foreach (var item in stage.Itens)
        {
            var reasons = item.Erros.Count > 0 ? string.Join("; ", item.Erros) : string.Join("; ", item.Alertas);
            var fields = new[]
            {
                item.Aba,
                item.Linha.ToString(CultureInfo.InvariantCulture),
                item.Dados.NomeRazaoSocial ?? string.Empty,
                Mask(item.Dados.Documento) ?? string.Empty,
                item.Resultado ?? Status(item),
                reasons
            };
            builder.AppendLine(string.Join(',', fields.Select(Csv)));
        }
        return builder.ToString();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string? Mask(string? value)
    {
        var digits = DocumentoFiscal.Normalize(value);
        return digits.Length switch
        {
            11 => $"***.***.***-{digits[^2..]}",
            14 => $"**.***.***/****-{digits[^2..]}",
            > 2 => new string('*', digits.Length - 2) + digits[^2..],
            0 => null,
            _ => new string('*', digits.Length)
        };
    }

    private static ImportacaoOperacaoResult<T> Success<T>(T value) => new(value, null);
    private static ImportacaoOperacaoResult<T> Failure<T>(string message, ImportacaoErroKind kind = ImportacaoErroKind.Validation) => new(default, message, kind);

    private sealed record ExistingClient(int Id, string Documento, bool Ativo);
    private sealed record ConfirmationExecution(
        ImportacaoOperacaoResult<ImportacaoResultadoResponse> Result,
        string? Report,
        ImportacaoLoteEstagio? StageToSave);
}
