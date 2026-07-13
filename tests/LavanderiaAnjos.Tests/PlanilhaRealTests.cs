using System.Security.Cryptography;
using LavanderiaAnjos.Application.Clientes;

namespace LavanderiaAnjos.Tests;

public sealed class PlanilhaRealTests
{
    [Fact]
    public void LeAsDuasAbasSemModificarOArquivoOriginal()
    {
        var solutionRoot = FindSolutionRoot();
        var workbookPath = Path.Combine(solutionRoot, "Cadastro Clientes.xlsx");

        // O arquivo real é deliberadamente ausente em clones do Git.
        if (!File.Exists(workbookPath))
        {
            return;
        }

        var before = SHA256.HashData(File.ReadAllBytes(workbookPath));
        var inspector = new PlanilhaClienteInspector();

        using (var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var sheetNames = inspector.ObterNomesDasAbas(stream);
            Assert.Equal(["Uniformes e hoteis", "Consumidor final"], sheetNames);
        }

        var after = SHA256.HashData(File.ReadAllBytes(workbookPath));
        Assert.Equal(before, after);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LavanderiaAnjos.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz da solução.");
    }
}
