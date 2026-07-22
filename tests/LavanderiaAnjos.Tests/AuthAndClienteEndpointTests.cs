using System.Net;
using System.Net.Http.Json;
using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Application.Database;

namespace LavanderiaAnjos.Tests;

public sealed class AuthAndClienteEndpointTests
{
    private static readonly DatabaseVerificationResult HealthyDatabase =
        DatabaseVerificationResult.Healthy("servidor", "banco", "versao");

    [Fact]
    public async Task RotasDeClientesExigemAutenticacao()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/clientes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginValidoIdentificaUsuarioELogoutEncerraSessao()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });

        using var login = await LoginAsync(client);
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var me = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var logout = await client.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task LoginInvalidoEOUsuarioInativoSaoRejeitados()
    {
        using var activeFactory = new TestWebApplicationFactory(HealthyDatabase);
        using var activeClient = activeFactory.CreateClient();
        using var invalid = await activeClient.PostAsJsonAsync("/api/auth/login", new { login = TestWebApplicationFactory.Login, password = "senha-errada" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        using var inactiveFactory = new TestWebApplicationFactory(HealthyDatabase, usuarioAtivo: false);
        using var inactiveClient = inactiveFactory.CreateClient();
        using var inactive = await LoginAsync(inactiveClient);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
    }

    [Fact]
    public async Task ApiCriaListaBuscaEditaInativaEReativaCliente()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        using var login = await LoginAsync(client);
        Assert.True(login.IsSuccessStatusCode);

        var createRequest = CreateRequest("Cliente API", "529.982.247-25");
        using var createdResponse = await client.PostAsJsonAsync("/api/clientes", createRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<ClienteDetailsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        using var duplicateResponse = await client.PostAsJsonAsync("/api/clientes", createRequest with { NomeRazaoSocial = "Duplicado" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/clientes?busca=Cliente%20API&pagina=1&tamanhoPagina=10", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<ClienteListItemResponse>>(TestContext.Current.CancellationToken);
        Assert.Single(list!.Itens);

        using var updateResponse = await client.PutAsJsonAsync($"/api/clientes/{created.Id}", UpdateRequest("Cliente API Editado", created.Documento), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var deactivate = await client.PatchAsync($"/api/clientes/{created.Id}/inativar", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using var reactivate = await client.PatchAsync($"/api/clientes/{created.Id}/reativar", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { login = TestWebApplicationFactory.Login, password = TestWebApplicationFactory.Password }, TestContext.Current.CancellationToken);

    private static ClienteCreateRequest CreateRequest(string nome, string documento) =>
        new(null, nome, documento, false, null, null, null, "01769-000", "Rua Exemplo", "123", null, "Centro", "Bastos", "sp", "(14) 99999-8888", "contato@exemplo.com", null, null);

    private static ClienteUpdateRequest UpdateRequest(string nome, string documento) =>
        new(null, nome, documento, false, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
