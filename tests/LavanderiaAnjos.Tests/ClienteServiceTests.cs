using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Infrastructure.Clientes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LavanderiaAnjos.Tests;

public sealed class ClienteServiceTests
{
    [Fact]
    public async Task ExecutaCicloDeCriacaoEdicaoInativacaoEReativacao()
    {
        await using var dbContext = CreateDbContext();
        var service = new ClienteService(dbContext, NullLogger<ClienteService>.Instance);

        var created = await service.CriarAsync(Request("Cliente Inicial", "529.982.247-25"), TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess);
        Assert.Equal("52998224725", created.Value!.Documento);

        var updated = await service.AtualizarAsync(created.Value.Id, UpdateRequest("Cliente Editado", "52998224725"), TestContext.Current.CancellationToken);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Cliente Editado", updated.Value!.NomeRazaoSocial);
        Assert.NotNull(updated.Value.AtualizadoEm);

        var deactivated = await service.InativarAsync(created.Value.Id, TestContext.Current.CancellationToken);
        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.Ativo);

        var reactivated = await service.ReativarAsync(created.Value.Id, TestContext.Current.CancellationToken);
        Assert.True(reactivated.IsSuccess);
        Assert.True(reactivated.Value!.Ativo);
    }

    [Fact]
    public async Task BloqueiaDocumentoDuplicadoEReativacaoComConflito()
    {
        await using var dbContext = CreateDbContext();
        var service = new ClienteService(dbContext, NullLogger<ClienteService>.Instance);

        var first = await service.CriarAsync(Request("Primeiro", "52998224725"), TestContext.Current.CancellationToken);
        var duplicate = await service.CriarAsync(Request("Duplicado", "529.982.247-25"), TestContext.Current.CancellationToken);
        Assert.Equal(ServiceErrorKind.Conflict, duplicate.ErrorKind);

        await service.InativarAsync(first.Value!.Id, TestContext.Current.CancellationToken);
        var replacement = await service.CriarAsync(Request("Substituto", "52998224725"), TestContext.Current.CancellationToken);
        Assert.True(replacement.IsSuccess);

        var reactivation = await service.ReativarAsync(first.Value.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ServiceErrorKind.Conflict, reactivation.ErrorKind);
    }

    [Fact]
    public async Task ListaBuscaFiltraEPaginaNoServidor()
    {
        await using var dbContext = CreateDbContext();
        var service = new ClienteService(dbContext, NullLogger<ClienteService>.Instance);
        await service.CriarAsync(Request("Alfa Serviços", "52998224725", "Bastos", "14999998888", 10), TestContext.Current.CancellationToken);
        await service.CriarAsync(Request("Beta Comércio", "11222333000181", "Marília", "1433332222", 20), TestContext.Current.CancellationToken);

        var byCity = await service.ListarAsync(new ClienteListQuery(null, null, "Bastos", null, 1, 10), TestContext.Current.CancellationToken);
        var byDocument = await service.ListarAsync(new ClienteListQuery("11.222.333/0001-81", null, null, null, 1, 10), TestContext.Current.CancellationToken);
        var byPhone = await service.ListarAsync(new ClienteListQuery("(14) 99999-8888", null, null, null, 1, 1), TestContext.Current.CancellationToken);
        var byCode = await service.ListarAsync(new ClienteListQuery("20", null, null, null, 1, 10), TestContext.Current.CancellationToken);

        Assert.Single(byCity.Itens);
        Assert.Single(byDocument.Itens);
        Assert.Equal(1, byPhone.TamanhoPagina);
        Assert.Single(byPhone.Itens);
        Assert.Equal("Beta Comércio", Assert.Single(byCode.Itens).NomeRazaoSocial);
    }

    private static LavanderiaAnjosDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LavanderiaAnjosDbContext>()
            .UseInMemoryDatabase($"ClienteServiceTests-{Guid.NewGuid():N}")
            .Options;
        return new LavanderiaAnjosDbContext(options);
    }

    private static ClienteCreateRequest Request(
        string nome,
        string documento,
        string? cidade = null,
        string? telefone = null,
        int? codigoLegado = null) =>
        new(codigoLegado, nome, documento, false, null, null, null, "01769000", "Rua Teste", "S/N", null, "Centro", cidade, "sp", telefone, "contato@exemplo.com; fiscal@exemplo.com", null, null);

    private static ClienteUpdateRequest UpdateRequest(string nome, string documento) =>
        new(null, nome, documento, false, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
