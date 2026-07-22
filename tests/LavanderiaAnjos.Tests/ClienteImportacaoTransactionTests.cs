using LavanderiaAnjos.Application.Importacoes;
using LavanderiaAnjos.Domain.Clientes;
using LavanderiaAnjos.Domain.Importacoes;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Importacoes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace LavanderiaAnjos.Tests;

public sealed class ClienteImportacaoTransactionTests
{
    [Fact]
    public async Task EstrategiaEnvolveSaveChangesERetryNaoDuplicaEntidadesRastreadas()
    {
        await using var fixture = await ImportFixture.CreateAsync(useRetryingStrategy: true);
        fixture.Interceptor.FailureFactory = static () => new RetryableImportException();
        fixture.Interceptor.RemainingFailures = 1;

        var result = await fixture.Service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(fixture.Interceptor.SawExecutionStrategy);
        Assert.Equal(2, fixture.Interceptor.SaveAttempts);
        Assert.Equal(2, await fixture.DbContext.Clientes.CountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            fixture.DbContext.ChangeTracker.Entries(),
            entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task FalhaNaGravacaoReverteTudoPreservaArquivoEPermiteNovaTentativa()
    {
        await using var fixture = await ImportFixture.CreateAsync(useRetryingStrategy: true);
        fixture.Interceptor.FailureFactory = static () =>
            new DbUpdateException("Falha sintética depois da gravação.", new InvalidOperationException());
        fixture.Interceptor.RemainingFailures = 1;

        var failed = await fixture.Service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);

        Assert.Equal(ImportacaoErroKind.Conflict, failed.ErrorKind);
        Assert.Contains("Nenhum cliente", failed.Message, StringComparison.Ordinal);
        Assert.Equal(0, await fixture.DbContext.Clientes.CountAsync(TestContext.Current.CancellationToken));
        var lotAfterFailure = await fixture.DbContext.ImportacoesClientes
            .AsNoTracking()
            .SingleAsync(lot => lot.Id == fixture.LotId, TestContext.Current.CancellationToken);
        Assert.Equal(ImportacaoLoteStatus.PreVisualizacao, lotAfterFailure.Status);
        Assert.Null(lotAfterFailure.ConfirmadoEm);
        Assert.Equal(0, lotAfterFailure.QuantidadeImportada);
        Assert.NotNull(await fixture.FileStore.ReadStageAsync(
            fixture.LotId,
            TestContext.Current.CancellationToken));

        var retried = await fixture.Service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);

        Assert.True(retried.IsSuccess);
        Assert.Equal(2, await fixture.DbContext.Clientes.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null(await fixture.FileStore.ReadStageAsync(
            fixture.LotId,
            TestContext.Current.CancellationToken));

        var repeated = await fixture.Service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);
        Assert.Equal(ImportacaoErroKind.Conflict, repeated.ErrorKind);
    }

    [Fact]
    public async Task DocumentoCriadoDepoisDaPreviaAbortaLoteInteiro()
    {
        await using var fixture = await ImportFixture.CreateAsync(useRetryingStrategy: false);
        fixture.DbContext.Clientes.Add(new Cliente
        {
            NomeRazaoSocial = "Concorrente",
            Documento = "52998224725",
            TipoPessoa = TipoPessoa.PessoaFisica,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await fixture.Service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);

        Assert.Equal(ImportacaoErroKind.Conflict, result.ErrorKind);
        Assert.Equal(1, await fixture.DbContext.Clientes.CountAsync(TestContext.Current.CancellationToken));
        var lot = await fixture.DbContext.ImportacoesClientes
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == fixture.LotId, TestContext.Current.CancellationToken);
        Assert.Equal(ImportacaoLoteStatus.PreVisualizacao, lot.Status);
        Assert.Equal(0, lot.QuantidadeImportada);
        Assert.Equal(1, lot.QuantidadeSelecionada);
        var revalidatedStage = await fixture.FileStore.ReadStageAsync(
            fixture.LotId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(revalidatedStage);
        Assert.False(revalidatedStage.Itens.Single(item => item.Dados.Documento == "52998224725").Selecionado);
    }

    [Fact]
    public async Task ArquivoTemporarioSoEhExcluidoDepoisDoCommitVisivel()
    {
        await using var fixture = await ImportFixture.CreateAsync(useRetryingStrategy: false);
        var observingStore = new CommitObservingFileStore(fixture.FileStore, fixture.Connection);
        var service = new ClienteImportacaoService(
            fixture.DbContext,
            new ClientePlanilhaReader(),
            observingStore);

        var result = await service.ConfirmarAsync(
            fixture.LotId,
            fixture.UserId,
            "Teste",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(observingStore.CommitWasVisibleWhenStageWasDeleted);
        Assert.Null(await fixture.FileStore.ReadStageAsync(
            fixture.LotId,
            TestContext.Current.CancellationToken));
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly string _temporaryPath;

        private ImportFixture(
            SqliteConnection connection,
            LavanderiaAnjosDbContext dbContext,
            ImportacaoFileStore fileStore,
            SaveFailureInterceptor interceptor,
            string temporaryPath,
            Guid lotId,
            int userId)
        {
            Connection = connection;
            DbContext = dbContext;
            FileStore = fileStore;
            Interceptor = interceptor;
            _temporaryPath = temporaryPath;
            LotId = lotId;
            UserId = userId;
            Service = new ClienteImportacaoService(dbContext, new ClientePlanilhaReader(), fileStore);
        }

        public SqliteConnection Connection { get; }
        public LavanderiaAnjosDbContext DbContext { get; }
        public ImportacaoFileStore FileStore { get; }
        public SaveFailureInterceptor Interceptor { get; }
        public ClienteImportacaoService Service { get; }
        public Guid LotId { get; }
        public int UserId { get; }

        public static async Task<ImportFixture> CreateAsync(bool useRetryingStrategy)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var interceptor = new SaveFailureInterceptor();
            var optionsBuilder = new DbContextOptionsBuilder<LavanderiaAnjosDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor);
            if (useRetryingStrategy)
            {
                optionsBuilder.ReplaceService<IExecutionStrategyFactory, RetryingTestExecutionStrategyFactory>();
            }

            var dbContext = new LavanderiaAnjosDbContext(optionsBuilder.Options);
            await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var user = new Usuario
            {
                Nome = "Usuário de teste",
                Login = $"usuario-{Guid.NewGuid():N}",
                SenhaHash = "hash-de-teste",
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };
            dbContext.Usuarios.Add(user);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var now = DateTime.UtcNow;
            var lot = new ImportacaoClienteLote
            {
                Id = Guid.NewGuid(),
                HashArquivo = new string('A', 64),
                NomeArquivo = "sintetico.xlsx",
                Status = ImportacaoLoteStatus.PreVisualizacao,
                CriadoEm = now,
                ExpiraEm = now.AddHours(1),
                UsuarioId = user.Id,
                QuantidadeTotal = 2,
                QuantidadeValida = 2,
                QuantidadeSelecionada = 2
            };
            dbContext.ImportacoesClientes.Add(lot);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            var temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "LavanderiaAnjosTransactionTests",
                Guid.NewGuid().ToString("N"));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Importacao:TemporaryPath"] = temporaryPath
                })
                .Build();
            var fileStore = new ImportacaoFileStore(configuration);
            await fileStore.SaveStageAsync(
                Stage(lot.Id, user.Id, lot.HashArquivo, now, lot.ExpiraEm),
                TestContext.Current.CancellationToken);
            interceptor.ResetObservations();
            return new(connection, dbContext, fileStore, interceptor, temporaryPath, lot.Id, user.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
            if (Directory.Exists(_temporaryPath))
            {
                Directory.Delete(_temporaryPath, recursive: true);
            }
        }

        private static ImportacaoLoteEstagio Stage(
            Guid lotId,
            int userId,
            string hash,
            DateTime createdAt,
            DateTime expiresAt) => new()
        {
            LoteId = lotId,
            UsuarioId = userId,
            HashArquivo = hash,
            NomeArquivo = "sintetico.xlsx",
            CriadoEm = createdAt,
            ExpiraEm = expiresAt,
            Itens =
            [
                Item("Cliente sintético um", "52998224725", 2),
                Item("Cliente sintético dois", "11222333000181", 3)
            ]
        };

        private static ImportacaoItemEstagio Item(string name, string document, int row) => new()
        {
            Aba = "Clientes",
            Linha = row,
            DocumentoEncontrado = document,
            Dados = new ImportacaoClienteDados(
                null, name, document, false, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null),
            Selecionado = true
        };
    }

    private sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        public Func<Exception>? FailureFactory { get; set; }
        public int RemainingFailures { get; set; }
        public int SaveAttempts { get; private set; }
        public bool SawExecutionStrategy { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            SawExecutionStrategy |= ExecutionStrategy.Current is not null;
            if (RemainingFailures > 0)
            {
                RemainingFailures--;
                throw FailureFactory!();
            }

            return ValueTask.FromResult(result);
        }

        public void ResetObservations()
        {
            SaveAttempts = 0;
            SawExecutionStrategy = false;
        }
    }

    private sealed class RetryingTestExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryingTestExecutionStrategy(dependencies);
    }

    private sealed class RetryingTestExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is RetryableImportException;
    }

    private sealed class RetryableImportException : Exception;

    private sealed class CommitObservingFileStore(
        IImportacaoFileStore inner,
        SqliteConnection connection) : IImportacaoFileStore
    {
        public bool CommitWasVisibleWhenStageWasDeleted { get; private set; }

        public Task SaveStageAsync(ImportacaoLoteEstagio lote, CancellationToken cancellationToken) =>
            inner.SaveStageAsync(lote, cancellationToken);

        public Task<ImportacaoLoteEstagio?> ReadStageAsync(Guid id, CancellationToken cancellationToken) =>
            inner.ReadStageAsync(id, cancellationToken);

        public async Task DeleteStageAsync(Guid id, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM ImportacoesClientes WHERE Status = 2),
                    (SELECT COUNT(*) FROM Clientes)
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            CommitWasVisibleWhenStageWasDeleted = reader.GetInt64(0) == 1 && reader.GetInt64(1) == 2;
            await inner.DeleteStageAsync(id, cancellationToken);
        }

        public Task SaveReportAsync(Guid id, string csv, CancellationToken cancellationToken) =>
            inner.SaveReportAsync(id, csv, cancellationToken);

        public Task<string?> ReadReportAsync(Guid id, CancellationToken cancellationToken) =>
            inner.ReadReportAsync(id, cancellationToken);

        public Task CleanupAsync(DateTime utcNow, CancellationToken cancellationToken) =>
            inner.CleanupAsync(utcNow, cancellationToken);
    }
}
