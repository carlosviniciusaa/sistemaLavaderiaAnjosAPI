namespace LavanderiaAnjos.Domain.Importacoes;

public sealed class ImportacaoClienteLote
{
    public Guid Id { get; set; }
    public string HashArquivo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public ImportacaoLoteStatus Status { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? ConfirmadoEm { get; set; }
    public int UsuarioId { get; set; }
    public int QuantidadeTotal { get; set; }
    public int QuantidadeValida { get; set; }
    public int QuantidadeComAlerta { get; set; }
    public int QuantidadeImportada { get; set; }
    public int QuantidadeIgnorada { get; set; }
    public int QuantidadeInvalida { get; set; }
    public int QuantidadeDuplicada { get; set; }
    public int QuantidadeExistente { get; set; }
    public int QuantidadeSelecionada { get; set; }
}
