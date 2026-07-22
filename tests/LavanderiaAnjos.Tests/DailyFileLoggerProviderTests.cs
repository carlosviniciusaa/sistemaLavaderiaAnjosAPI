using LavanderiaAnjos.Web.Logging;
using Microsoft.Extensions.Logging;

namespace LavanderiaAnjos.Tests;

public sealed class DailyFileLoggerProviderTests
{
    [Fact]
    public void SanitizaExcecaoERemoveLogsForaDaRetencao()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LavanderiaAnjosLoggerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var expiredPath = Path.Combine(directory, "lavanderia-20000101.log");
        File.WriteAllText(expiredPath, "antigo");
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-40));

        try
        {
            using var provider = new DailyFileLoggerProvider(directory, retentionDays: 30);
            var logger = provider.CreateLogger("Teste");
            var exception = new InvalidOperationException(
                "Server=interno;Database=privado;User Id=usuario;Password=segredo; CPF 52998224725; contato@exemplo.com");
            logger.Log(
                LogLevel.Error,
                new EventId(1),
                "Falha sintética",
                exception,
                static (state, _) => state);

            var currentPath = Directory.GetFiles(directory, "lavanderia-*.log").Single();
            var content = File.ReadAllText(currentPath);

            Assert.False(File.Exists(expiredPath));
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
            Assert.Contains("connection string omitida", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("segredo", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("52998224725", content, StringComparison.Ordinal);
            Assert.DoesNotContain("contato@exemplo.com", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
