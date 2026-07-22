using System.Text;
using System.Text.Json;
using LavanderiaAnjos.Application.Importacoes;
using Microsoft.Extensions.Configuration;

namespace LavanderiaAnjos.Infrastructure.Importacoes;

public sealed class ImportacaoFileStore : IImportacaoFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _stageDirectory;
    private readonly string _reportDirectory;

    public ImportacaoFileStore(IConfiguration configuration)
    {
        var configured = configuration["Importacao:TemporaryPath"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "LavanderiaAnjos", "importacoes")
            : Path.GetFullPath(configured);
        _stageDirectory = Path.Combine(root, "stage");
        _reportDirectory = Path.Combine(root, "reports");
        Directory.CreateDirectory(_stageDirectory);
        Directory.CreateDirectory(_reportDirectory);
    }

    public async Task SaveStageAsync(ImportacaoLoteEstagio lote, CancellationToken cancellationToken)
    {
        var target = StagePath(lote.LoteId);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65_536, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, lote, SerializerOptions, cancellationToken);
        }

        File.Move(temporary, target, true);
    }

    public async Task<ImportacaoLoteEstagio?> ReadStageAsync(Guid loteId, CancellationToken cancellationToken)
    {
        var path = StagePath(loteId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<ImportacaoLoteEstagio>(stream, SerializerOptions, cancellationToken);
    }

    public Task DeleteStageAsync(Guid loteId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = StagePath(loteId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public async Task SaveReportAsync(Guid loteId, string csv, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(ReportPath(loteId), csv, new UTF8Encoding(true), cancellationToken);
    }

    public async Task<string?> ReadReportAsync(Guid loteId, CancellationToken cancellationToken)
    {
        var path = ReportPath(loteId);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public Task CleanupAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        DeleteOlderThan(_stageDirectory, utcNow.AddHours(-25), cancellationToken);
        DeleteOlderThan(_reportDirectory, utcNow.AddDays(-30), cancellationToken);
        return Task.CompletedTask;
    }

    private static void DeleteOlderThan(string directory, DateTime cutoff, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(path) < cutoff)
            {
                File.Delete(path);
            }
        }
    }

    private string StagePath(Guid id) => Path.Combine(_stageDirectory, $"{id:N}.json");
    private string ReportPath(Guid id) => Path.Combine(_reportDirectory, $"{id:N}.csv");
}
