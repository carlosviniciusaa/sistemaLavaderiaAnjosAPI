using System.Security.Cryptography;
using System.Text.Json;
using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Application.Importacoes;
using LavanderiaAnjos.Infrastructure.Importacoes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LavanderiaAnjos.Tests;

public sealed class PlanilhaRealPreviewIntegrationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [Fact]
    public async Task GeraSomenteAPrevisualizacaoRealQuandoExplicitamenteHabilitado()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_REAL_PREVIEW"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var root = FindSolutionRoot();
        var workbookPath = Path.Combine(root, "Cadastro Clientes.xlsx");
        Assert.True(File.Exists(workbookPath));
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: false)
            .Build();
        var connectionString = configuration.GetConnectionString("LavanderiaAnjos");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<LavanderiaAnjosDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(30))
            .Options;
        await using var dbContext = new LavanderiaAnjosDbContext(options);
        var userId = await dbContext.Usuarios.Where(user => user.Ativo).OrderBy(user => user.Id)
            .Select(user => user.Id).FirstAsync(TestContext.Current.CancellationToken);
        var clientsBefore = await dbContext.Clientes.CountAsync(TestContext.Current.CancellationToken);
        var hashBefore = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(workbookPath, TestContext.Current.CancellationToken)));
        var fileStore = new ImportacaoFileStore(configuration);
        var service = new ClienteImportacaoService(dbContext, new ClientePlanilhaReader(), fileStore);

        await using var workbook = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await service.PrevisualizarAsync(
            new(Path.GetFileName(workbookPath), workbook.Length, workbook), userId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Message);
        var preview = result.Value!;
        var stage = await fileStore.ReadStageAsync(preview.Resumo.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(stage);

        var clientsAfter = await dbContext.Clientes.CountAsync(TestContext.Current.CancellationToken);
        var hashAfter = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(workbookPath, TestContext.Current.CancellationToken)));
        Assert.Equal(clientsBefore, clientsAfter);
        Assert.Equal(hashBefore, hashAfter);

        var duplicateGroups = stage.Itens.Where(item => item.GrupoDuplicidade is not null)
            .Select(item => item.GrupoDuplicidade).Distinct(StringComparer.Ordinal).Count();
        var cpfCount = stage.Itens.Count(item => DocumentoFiscal.Normalize(item.Dados.Documento).Length == 11);
        var cnpjCount = stage.Itens.Count(item => DocumentoFiscal.Normalize(item.Dados.Documento).Length == 14);
        var aggregate = new
        {
            loteId = preview.Resumo.Id,
            hashAntes = hashBefore,
            hashDepois = hashAfter,
            clientesAntes = clientsBefore,
            clientesDepois = clientsAfter,
            preview.Resumo.QuantidadeTotal,
            preview.Resumo.QuantidadeValida,
            preview.Resumo.QuantidadeComAlerta,
            preview.Resumo.QuantidadeInvalida,
            preview.Resumo.QuantidadeDuplicada,
            gruposDuplicados = duplicateGroups,
            preview.Resumo.QuantidadeExistente,
            preview.Resumo.QuantidadeSelecionada,
            preview.Resumo.QuantidadeIgnorada,
            cpfs = cpfCount,
            cnpjs = cnpjCount,
            metricas = preview.Metricas
        };
        output.WriteLine(JsonSerializer.Serialize(aggregate, SerializerOptions));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LavanderiaAnjos.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Não foi possível localizar a raiz da solução.");
    }
}
