namespace LavanderiaAnjos.Application.Clientes;

public interface IPlanilhaClienteInspector
{
    IReadOnlyList<string> ObterNomesDasAbas(Stream planilha);
}
