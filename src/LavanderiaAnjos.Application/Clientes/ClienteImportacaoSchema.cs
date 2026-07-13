namespace LavanderiaAnjos.Application.Clientes;

public static class ClienteImportacaoSchema
{
    public static readonly IReadOnlySet<string> AbasDeClientes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Uniformes e hoteis",
        "Consumidor final"
    };

    public static readonly IReadOnlySet<string> ColunasIgnoradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Boleto",
        "Vencimento",
        "Envio"
    };

    public static readonly IReadOnlySet<string> ColunasDeAtendimento = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Última",
        "Próximo"
    };

    public static bool RepresentaCliente(string nomeDaAba) => AbasDeClientes.Contains(nomeDaAba);
}
