namespace LavanderiaAnjos.Domain.Usuarios;

public sealed class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string LoginNormalizado { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    public int VersaoSessao { get; set; }
    public long Versao { get; set; } = 1;
}
