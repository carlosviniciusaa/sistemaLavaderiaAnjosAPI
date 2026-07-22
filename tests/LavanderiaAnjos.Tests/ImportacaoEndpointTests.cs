using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Application.Importacoes;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LavanderiaAnjos.Tests;

public sealed class ImportacaoEndpointTests
{
    private static readonly DatabaseVerificationResult HealthyDatabase =
        DatabaseVerificationResult.Healthy("servidor", "banco", "versao");

    [Fact]
    public async Task RotasExigemAutenticacaoETokenAntifalsificacao()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var unauthorized = await anonymous.GetAsync("/api/importacoes/clientes/historico", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = factory.CreateClient(new() { HandleCookies = true });
        using var login = await LoginAsync(client);
        Assert.True(login.IsSuccessStatusCode);
        using var content = WorkbookContent();
        using var missingToken = await client.PostAsync("/api/importacoes/clientes/previsualizar", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingToken.StatusCode);
    }

    [Fact]
    public async Task PrevisualizacaoNaoSalvaClientesEConfirmacaoSinteticaSalvaLoteCompleto()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        using var login = await LoginAsync(client);
        Assert.True(login.IsSuccessStatusCode);
        var token = await GetTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/importacoes/clientes/previsualizar");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = WorkbookContent();
        using var previewResponse = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<ImportacaoLoteResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(preview);
        Assert.Equal(2, preview.Resumo.QuantidadeTotal);
        Assert.Equal(2, preview.Resumo.QuantidadeSelecionada);

        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(0, scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>().Clientes.Count());
        }

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/importacoes/clientes/{preview.Resumo.Id}/confirmar");
        confirmRequest.Headers.Add("X-CSRF-TOKEN", token);
        using var confirmation = await client.SendAsync(confirmRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var result = await confirmation.Content.ReadFromJsonAsync<ImportacaoResultadoResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(2, result!.QuantidadeImportada);

        using var reportResponse = await client.GetAsync($"/api/importacoes/clientes/{preview.Resumo.Id}/relatorio", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("***.***.***-25", report, StringComparison.Ordinal);
        Assert.DoesNotContain("52998224725", report, StringComparison.Ordinal);
        Assert.DoesNotContain("11222333000181", report, StringComparison.Ordinal);

        using var verificationScope = factory.Services.CreateScope();
        Assert.Equal(2, verificationScope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>().Clientes.Count());
    }

    [Fact]
    public async Task RejeitaExtensaoIncorretaMesmoComConteudoXlsx()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        using var login = await LoginAsync(client);
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/importacoes/clientes/previsualizar");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = WorkbookContent("clientes.xls");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ErroInternoRetornaCodigoDeCorrelacaoSemExporDetalhes()
    {
        using var factory = new TestWebApplicationFactory(
            HealthyDatabase,
            configureServices: services =>
            {
                services.RemoveAll<IClienteImportacaoService>();
                services.AddSingleton<IClienteImportacaoService, ThrowingImportacaoService>();
            });
        using var client = factory.CreateClient(new() { HandleCookies = true });
        using var login = await LoginAsync(client);
        var token = await GetTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/importacoes/clientes/{Guid.NewGuid()}/confirmar");
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("errorCode").GetString()));
        Assert.Contains("Não foi possível concluir a importação", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/importacoes/clientes/antiforgery", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static MultipartFormDataContent WorkbookContent(string fileName = "clientes.xlsx")
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(WorkbookFixture.CreateStream()), "arquivo", fileName);
        return content;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { login = TestWebApplicationFactory.Login, password = TestWebApplicationFactory.Password }, TestContext.Current.CancellationToken);

    private sealed class ThrowingImportacaoService : IClienteImportacaoService
    {
        public Task<ImportacaoOperacaoResult<ImportacaoResultadoResponse>> ConfirmarAsync(
            Guid loteId,
            int usuarioId,
            string usuarioNome,
            CancellationToken cancellationToken) =>
            Task.FromException<ImportacaoOperacaoResult<ImportacaoResultadoResponse>>(
                new InvalidOperationException("detalhes internos sintéticos"));

        public Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> PrevisualizarAsync(ImportacaoArquivoEntrada arquivo, int usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ObterAsync(Guid loteId, int usuarioId, ImportacaoConsulta consulta, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoItemEdicaoResponse>> ObterItemAsync(Guid loteId, Guid itemId, int usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> CorrigirAsync(Guid loteId, Guid itemId, int usuarioId, ImportacaoClienteDados dados, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> SelecionarAsync(Guid loteId, Guid itemId, int usuarioId, bool selecionado, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ResolverDuplicidadeAsync(Guid loteId, int usuarioId, ImportacaoResolverDuplicidadeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<ImportacaoResumoResponse>> CancelarAsync(Guid loteId, int usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImportacaoResumoResponse>> HistoricoAsync(int usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportacaoOperacaoResult<string>> RelatorioAsync(Guid loteId, int usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
