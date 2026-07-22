using System.Diagnostics;
using System.Globalization;
using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Infrastructure;
using LavanderiaAnjos.Infrastructure.Authentication;
using LavanderiaAnjos.Web.Endpoints;
using LavanderiaAnjos.Web.Logging;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "LavanderiaAnjos");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (!builder.Environment.IsDevelopment())
{
    var configuredPath = builder.Configuration["Logging:File:Path"] ?? "logs";
    var logPath = Path.IsPathRooted(configuredPath)
        ? configuredPath
        : Path.Combine(builder.Environment.ContentRootPath, configuredPath);
    var retentionDays = Math.Clamp(builder.Configuration.GetValue("Logging:File:RetentionDays", 30), 1, 365);
    builder.Logging.AddProvider(new DailyFileLoggerProvider(logPath, retentionDays));
}

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "LavanderiaAnjos.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/clientes";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var idValue = context.Principal?.FindFirst("usuario_id")?.Value;
            var versionValue = context.Principal?.FindFirst("session_version")?.Value;
            if (!int.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                !int.TryParse(versionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<LavanderiaAnjosDbContext>();
            var valid = await dbContext.Usuarios.AsNoTracking().AnyAsync(
                usuario => usuario.Id == id && usuario.Ativo && usuario.VersaoSessao == version);
            if (!valid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("Administradores", policy => policy.RequireAuthenticatedUser()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

var app = builder.Build();
var lifetimeLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Lifecycle");

ApplicationLog.Starting(lifetimeLogger, app.Environment.EnvironmentName);
app.Lifetime.ApplicationStarted.Register(() => ApplicationLog.Started(lifetimeLogger));
app.Lifetime.ApplicationStopping.Register(() => ApplicationLog.Stopping(lifetimeLogger));

if (args.Contains("--check-database", StringComparer.OrdinalIgnoreCase))
{
    await VerifyDatabaseAndExitAsync(app.Services);
    return;
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception exception)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        var errorCode = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        ApplicationLog.UnhandledException(logger, exception, errorCode);

        if (app.Environment.IsDevelopment())
        {
            throw;
        }

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "Unhealthy",
                message = "Ocorreu um erro interno.",
                errorCode
            });
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use(async (context, next) =>
{
    if ((context.Request.Path.Equals("/index.html") || context.Request.Path.Equals("/importacao.html") ||
         context.Request.Path.Equals("/administradores.html")) &&
        context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login");
        return;
    }

    await next(context);
});

app.UseStaticFiles();

app.MapGet("/", (HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? Results.Redirect("/clientes")
        : Results.Redirect("/login"));
app.MapGet("/login", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "login.html"), "text/html")).AllowAnonymous();
app.MapGet("/clientes", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html")).RequireAuthorization();
app.MapGet("/importacao", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "importacao.html"), "text/html")).RequireAuthorization();
app.MapGet("/administradores", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "administradores.html"), "text/html"))
    .RequireAuthorization("Administradores");

app.MapHealthEndpoint();
app.MapAuthEndpoints();
app.MapClienteEndpoints();
app.MapImportacaoEndpoints();
app.MapAdministradorEndpoints();

await TryInitializeBootstrapAdminAsync(app.Services, lifetimeLogger);
await app.RunAsync();

static async Task TryInitializeBootstrapAdminAsync(IServiceProvider services, ILogger logger)
{
    try
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BootstrapAdminInitializer>().InitializeAsync();
    }
    catch (Exception exception)
    {
        Program.LogBootstrapFailure(logger, exception.GetType().Name);
    }
}

static async Task VerifyDatabaseAndExitAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var verifier = scope.ServiceProvider.GetRequiredService<IDatabaseVerifier>();
    var result = await verifier.VerifyAsync();

    if (result.IsHealthy)
    {
        Console.WriteLine("Conectividade SQL: sucesso.");
        Console.WriteLine($"Servidor: {result.Server}");
        Console.WriteLine($"Banco: {result.Database}");
        Console.WriteLine($"Versão: {result.Version}");
        return;
    }

    Console.Error.WriteLine("Conectividade SQL: falha.");
    Console.Error.WriteLine($"Categoria: {result.FailureCategory}; código: {result.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "indisponível"}.");
    Environment.ExitCode = 2;
}

public partial class Program
{
    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Não foi possível executar o bootstrap do administrador. Categoria: {FailureCategory}.")]
    internal static partial void LogBootstrapFailure(ILogger logger, string failureCategory);
}
