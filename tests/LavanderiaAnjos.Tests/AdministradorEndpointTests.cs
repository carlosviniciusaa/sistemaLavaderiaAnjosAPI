using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LavanderiaAnjos.Application.Administradores;
using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Domain.Usuarios;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Tests;

public sealed class AdministradorEndpointTests
{
    private static readonly DatabaseVerificationResult HealthyDatabase =
        DatabaseVerificationResult.Healthy("servidor", "banco", "versao");

    [Fact]
    public async Task RotasExigemAutenticacaoECsrf()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/administradores", TestContext.Current.CancellationToken)).StatusCode);

        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        using var missingCsrf = await client.PostAsJsonAsync("/api/administradores", ValidCreate("sem.csrf"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
    }

    [Fact]
    public async Task CriaAdministradorComHashSemExporSenhaELoginNormalizado()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);

        using var response = await SendAsync(client, HttpMethod.Post, "/api/administradores", ValidCreate("  Nova.Admin  "), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("senha", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        var created = JsonSerializer.Deserialize<AdministradorResponse>(body, JsonOptions())!;
        Assert.Equal("nova.admin", created.Login);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>();
        var usuario = await db.Usuarios.SingleAsync(item => item.Id == created.Id, TestContext.Current.CancellationToken);
        Assert.Equal("nova.admin", usuario.LoginNormalizado);
        Assert.NotEqual("Senha123!", usuario.SenhaHash);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, "Senha123!"));
    }

    [Theory]
    [InlineData("", "admin.ok", "Senha123!", "Senha123!", "nome")]
    [InlineData("Nome", "", "Senha123!", "Senha123!", "login")]
    [InlineData("Nome", "ab", "Senha123!", "Senha123!", "login")]
    [InlineData("Nome", "admin teste", "Senha123!", "Senha123!", "login")]
    [InlineData("Nome", "admin@teste", "Senha123!", "Senha123!", "login")]
    [InlineData("Nome", "admin.ok", "Curta1", "Curta1", "novaSenha")]
    [InlineData("Nome", "admin.ok", "12345678", "12345678", "novaSenha")]
    [InlineData("Nome", "admin.ok", "abcdefgh", "abcdefgh", "novaSenha")]
    [InlineData("Nome", "admin.ok", "Senha123!", "Senha124!", "confirmacaoSenha")]
    public async Task RejeitaCadastroInvalido(string nome, string login, string senha, string confirmacao, string field)
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/administradores",
            new AdministradorCreateRequest(nome, login, senha, confirmacao), token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImpedeLoginDuplicadoMesmoInativoEDiferenteCaixa()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        var created = await CreateAsync(client, token, "Duplicado.Admin");
        using var inactivate = await SendAsync(client, HttpMethod.Patch, $"/api/administradores/{created.Id}/inativar", new AdministradorStatusRequest(created.Versao), token);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        using var duplicate = await SendAsync(client, HttpMethod.Post, "/api/administradores", ValidCreate("DUPLICADO.ADMIN"), token);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task EditaNomeELoginAtualizaDataEProtegeConcorrencia()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        var created = await CreateAsync(client, token, "editar.admin");
        var stale = new AdministradorUpdateRequest("Nome Editado", "login.editado", created.Versao);
        using var updatedResponse = await SendAsync(client, HttpMethod.Put, $"/api/administradores/{created.Id}", stale, token);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<AdministradorResponse>(JsonOptions(), TestContext.Current.CancellationToken);
        Assert.Equal("Nome Editado", updated!.Nome);
        Assert.Equal("login.editado", updated.Login);
        Assert.NotNull(updated.AtualizadoEm);
        Assert.True(updated.Versao > created.Versao);
        using var concurrent = await SendAsync(client, HttpMethod.Put, $"/api/administradores/{created.Id}", stale, token);
        Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
    }

    [Fact]
    public async Task NovaSenhaFuncionaAntigaFalhaECookiesDoAfetadoSaoInvalidados()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var manager = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(manager);
        var token = await GetTokenAsync(manager);
        var created = await CreateAsync(manager, token, "senha.admin", "SenhaAntiga123!");

        using var affectedSession = factory.CreateClient(new() { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(affectedSession, "SENHA.ADMIN", "SenhaAntiga123!")).StatusCode);
        using var reset = await SendAsync(manager, HttpMethod.Post, $"/api/administradores/{created.Id}/redefinir-senha",
            new AdministradorPasswordRequest("SenhaNova456!", "SenhaNova456!", created.Versao), token);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await affectedSession.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);

        using var oldPasswordClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(oldPasswordClient, "senha.admin", "SenhaAntiga123!")).StatusCode);
        using var newPasswordClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(newPasswordClient, "senha.admin", "SenhaNova456!")).StatusCode);
    }

    [Fact]
    public async Task RedefinirPropriaSenhaEncerraSessao()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        var me = await GetCurrentAsync(factory);
        using var reset = await SendAsync(client, HttpMethod.Post, $"/api/administradores/{me.Id}/redefinir-senha",
            new AdministradorPasswordRequest("MinhaSenha456!", "MinhaSenha456!", me.Versao), token);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("true", reset.Headers.GetValues("X-Reauthentication-Required").Single());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RedefinicaoNaoRegistraSenhaNemHashNosLogs()
    {
        var messages = new List<string>();
        var provider = new CapturingLoggerProvider(messages);
        using var factory = new TestWebApplicationFactory(
            HealthyDatabase,
            configureServices: services => services.AddSingleton<ILoggerProvider>(provider));
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        var created = await CreateAsync(client, token, "logs.admin", "SenhaAntes123!");
        const string newPassword = "SegredoUnico987!";
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/administradores/{created.Id}/redefinir-senha",
            new AdministradorPasswordRequest(newPassword, newPassword, created.Versao), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var hash = await scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>().Usuarios
            .Where(usuario => usuario.Id == created.Id)
            .Select(usuario => usuario.SenhaHash)
            .SingleAsync(TestContext.Current.CancellationToken);
        var log = string.Join(Environment.NewLine, messages);
        Assert.DoesNotContain(newPassword, log, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InativaOutroImpedeAutoInativacaoProtegeUltimoEReativa()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var client = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(client);
        var token = await GetTokenAsync(client);
        var current = await GetCurrentAsync(factory);
        using var self = await SendAsync(client, HttpMethod.Patch, $"/api/administradores/{current.Id}/inativar", new AdministradorStatusRequest(current.Versao), token);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);

        var other = await CreateAsync(client, token, "status.admin");
        using var inactivate = await SendAsync(client, HttpMethod.Patch, $"/api/administradores/{other.Id}/inativar", new AdministradorStatusRequest(other.Versao), token);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        var inactive = await inactivate.Content.ReadFromJsonAsync<AdministradorResponse>(JsonOptions(), TestContext.Current.CancellationToken);
        Assert.False(inactive!.Ativo);
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdministradorService>();
            var lastActiveAttempt = await service.InativarAsync(
                current.Id,
                new AdministradorStatusRequest(current.Versao),
                new AdministradorAuditContext(999, "Outro administrador", "teste-ultimo-ativo"),
                TestContext.Current.CancellationToken);
            Assert.Equal(AdministradorServiceErrorKind.Forbidden, lastActiveAttempt.ErrorKind);
        }
        using var inactiveLogin = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(inactiveLogin, "status.admin", "Senha123!")).StatusCode);
        using var reactivate = await SendAsync(client, HttpMethod.Patch, $"/api/administradores/{other.Id}/reativar", new AdministradorStatusRequest(inactive.Versao), token);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        Assert.True((await reactivate.Content.ReadFromJsonAsync<AdministradorResponse>(JsonOptions(), TestContext.Current.CancellationToken))!.Ativo);
    }

    [Fact]
    public async Task ConcorrenciaBloqueiaLoginDuplicadoENuncaDeixaSistemaSemAdministrador()
    {
        using var factory = new TestWebApplicationFactory(HealthyDatabase);
        using var manager = factory.CreateClient(new() { HandleCookies = true });
        await LoginAsync(manager);
        var token = await GetTokenAsync(manager);
        var requests = new[]
        {
            SendAsync(manager, HttpMethod.Post, "/api/administradores", ValidCreate("concorrente.admin"), token),
            SendAsync(manager, HttpMethod.Post, "/api/administradores", ValidCreate("CONCORRENTE.ADMIN"), token)
        };
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var concurrentAdmin = (await responses.Single(response => response.StatusCode == HttpStatusCode.Created)
            .Content.ReadFromJsonAsync<AdministradorResponse>(JsonOptions(), TestContext.Current.CancellationToken))!;
        foreach (var response in responses) response.Dispose();

        var current = await GetCurrentAsync(factory);
        using var concurrentClient = factory.CreateClient(new() { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK,
            (await LoginAsync(concurrentClient, "concorrente.admin", "Senha123!")).StatusCode);
        var concurrentToken = await GetTokenAsync(concurrentClient);
        var deactivations = await Task.WhenAll(
            SendAsync(manager, HttpMethod.Patch, $"/api/administradores/{concurrentAdmin.Id}/inativar",
                new AdministradorStatusRequest(concurrentAdmin.Versao), token),
            SendAsync(concurrentClient, HttpMethod.Patch, $"/api/administradores/{current.Id}/inativar",
                new AdministradorStatusRequest(current.Versao), concurrentToken));
        Assert.Single(deactivations, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in deactivations) response.Dispose();

        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>().Usuarios
            .CountAsync(item => item.Ativo, TestContext.Current.CancellationToken));
    }

    private static AdministradorCreateRequest ValidCreate(string login, string password = "Senha123!") =>
        new("Administrador Sintético", login, password, password);

    private static async Task<AdministradorResponse> CreateAsync(HttpClient client, string token, string login, string password = "Senha123!")
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/administradores", ValidCreate(login, password), token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdministradorResponse>(JsonOptions(), TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(HttpClient client, HttpMethod method, string url, T payload, string token)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/administradores/antiforgery", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<AdministradorResponse> GetCurrentAsync(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LavanderiaAnjosDbContext>().Usuarios.AsNoTracking()
            .Select(usuario => new AdministradorResponse(usuario.Id, usuario.Nome, usuario.Login, usuario.Ativo, usuario.CriadoEm, usuario.AtualizadoEm, usuario.Versao))
            .SingleAsync(usuario => usuario.Login == TestWebApplicationFactory.Login, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login = TestWebApplicationFactory.Login, string password = TestWebApplicationFactory.Password) =>
        client.PostAsJsonAsync("/api/auth/login", new { login, password }, TestContext.Current.CancellationToken);

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (messages) messages.Add(formatter(state, exception));
            }
        }
    }
}
