using LavanderiaAnjos.Application.Database;
using LavanderiaAnjos.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LavanderiaAnjos.Infrastructure.Database;

public sealed class SqlServerDatabaseVerifier(LavanderiaAnjosDbContext dbContext) : IDatabaseVerifier
{
    private const string VerificationQuery = """
        SELECT
            @@SERVERNAME AS Servidor,
            DB_NAME() AS BancoAtual,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS Versao;
        """;

    public async Task<DatabaseVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = VerificationQuery;
            command.CommandTimeout = 10;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return DatabaseVerificationResult.Unhealthy("resposta_vazia");
            }

            return DatabaseVerificationResult.Healthy(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2));
        }
        catch (SqlException exception)
        {
            return DatabaseVerificationResult.Unhealthy(Classify(exception), exception.Number);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DatabaseVerificationResult.Unhealthy("erro_inesperado");
        }
        finally
        {
            if (shouldClose && connection.State != System.Data.ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string Classify(SqlException exception) => exception.Number switch
    {
        -2 => "timeout_ou_porta",
        -1 => "instancia_ou_porta",
        53 or 64 or 11001 => "resolucao_nome_ou_rede",
        18456 => "autenticacao",
        4060 => "acesso_ao_banco",
        _ when exception.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) => "certificado",
        _ when exception.Message.Contains("instance", StringComparison.OrdinalIgnoreCase) => "instancia_ou_porta",
        _ => "sql_server"
    };
}
