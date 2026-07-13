using LavanderiaAnjos.Application.Database;

namespace LavanderiaAnjos.Web.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (
            IDatabaseHealthService databaseHealth,
            CancellationToken cancellationToken) =>
        {
            var databaseIsHealthy = await databaseHealth.IsHealthyAsync(cancellationToken);
            var statusCode = databaseIsHealthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            return Results.Json(
                new
                {
                    status = databaseIsHealthy ? "Healthy" : "Unhealthy",
                    database = databaseIsHealthy ? "Healthy" : "Unhealthy"
                },
                statusCode: statusCode);
        })
        .WithName("Health")
        .WithTags("Infraestrutura")
        .AllowAnonymous();

        return endpoints;
    }
}
