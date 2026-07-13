using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Infrastructure;
using LavanderiaAnjos.Web.Endpoints;
using LavanderiaAnjos.Web.Logging;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "Lavanderia Anjos";
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (!builder.Environment.IsDevelopment())
{
    var configuredPath = builder.Configuration["Logging:File:Path"] ?? "logs";
    var logPath = Path.IsPathRooted(configuredPath)
        ? configuredPath
        : Path.Combine(builder.Environment.ContentRootPath, configuredPath);
    builder.Logging.AddProvider(new DailyFileLoggerProvider(logPath));
}

builder.Services.AddInfrastructure(builder.Configuration);

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
        ApplicationLog.UnhandledException(logger, exception.GetType().Name);

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
                message = "Ocorreu um erro interno."
            });
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoint();
app.MapFallbackToFile("index.html");

await app.RunAsync();

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

public partial class Program;
