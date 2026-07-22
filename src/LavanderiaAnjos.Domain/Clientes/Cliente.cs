namespace LavanderiaAnjos.Domain.Clientes;

public sealed class Cliente
{
    public int Id { get; set; }
    public int? CodigoLegado { get; set; }
    public string NomeRazaoSocial { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public TipoPessoa TipoPessoa { get; set; }
    public bool ProdutorRural { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public DateOnly? DataInicioRelacionamento { get; set; }
    public string? Cep { get; set; }
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Telefone { get; set; }
    public string? EmailEnvio { get; set; }
    public string? EmailNotaFiscal { get; set; }
    public string? Observacoes { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }
}
