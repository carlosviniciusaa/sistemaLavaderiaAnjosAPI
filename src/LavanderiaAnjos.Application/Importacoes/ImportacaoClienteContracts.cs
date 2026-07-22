using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Domain.Clientes;
using LavanderiaAnjos.Domain.Importacoes;

namespace LavanderiaAnjos.Application.Importacoes;

public sealed record ImportacaoClienteDados(
    int? CodigoLegado,
    string? NomeRazaoSocial,
    string? Documento,
    bool ProdutorRural,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    DateOnly? DataInicioRelacionamento,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Telefone,
    string? EmailEnvio,
    string? EmailNotaFiscal,
    string? Observacoes = null) : IClienteRequest;

public sealed record PlanilhaAbaMetricas(
    string Aba,
    int UltimaLinhaFisica,
    int LinhasCabecalho,
    int LinhasTotalmenteVazias,
    int LinhasParciaisDescartadas,
    int LinhasOcultas,
    int LinhasFiltradas,
    int RegistrosConsiderados);

public sealed record PlanilhaMetricas(
    IReadOnlyList<PlanilhaAbaMetricas> Abas,
    int LinhasFisicas,
    int LinhasCabecalho,
    int LinhasTotalmenteVazias,
    int LinhasParciaisDescartadas,
    int LinhasOcultas,
    int LinhasFiltradas,
    int RegistrosConsiderados);

public sealed class ImportacaoItemEstagio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Aba { get; set; } = string.Empty;
    public int Linha { get; set; }
    public string? DocumentoEncontrado { get; set; }
    public ImportacaoClienteDados Dados { get; set; } = new(
        null, null, null, false, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null);
    public List<string> ErrosLeitura { get; set; } = [];
    public List<string> Erros { get; set; } = [];
    public List<string> Alertas { get; set; } = [];
    public string? GrupoDuplicidade { get; set; }
    public int? ClienteExistenteId { get; set; }
    public bool? ClienteExistenteAtivo { get; set; }
    public bool Selecionado { get; set; }
    public bool Ignorado { get; set; }
    public bool Corrigido { get; set; }
    public bool Importado { get; set; }
    public string? Resultado { get; set; }
}

public sealed class ImportacaoLoteEstagio
{
    public Guid LoteId { get; set; }
    public int UsuarioId { get; set; }
    public string HashArquivo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public PlanilhaMetricas Metricas { get; set; } = new([], 0, 0, 0, 0, 0, 0, 0);
    public List<ImportacaoItemEstagio> Itens { get; set; } = [];
}

public sealed record ImportacaoLeituraResultado(
    PlanilhaMetricas Metricas,
    IReadOnlyList<ImportacaoItemEstagio> Itens);

public sealed record ImportacaoResumoResponse(
    Guid Id,
    string NomeArquivo,
    string HashArquivoAbreviado,
    ImportacaoLoteStatus Status,
    DateTime CriadoEm,
    DateTime ExpiraEm,
    DateTime? ConfirmadoEm,
    int QuantidadeTotal,
    int QuantidadeValida,
    int QuantidadeComAlerta,
    int QuantidadeInvalida,
    int QuantidadeDuplicada,
    int QuantidadeExistente,
    int QuantidadeSelecionada,
    int QuantidadeImportada,
    int QuantidadeIgnorada);

public sealed record ImportacaoItemResponse(
    Guid Id,
    string Aba,
    int Linha,
    string? NomeRazaoSocial,
    string? DocumentoEncontradoMascarado,
    string? DocumentoNormalizadoMascarado,
    TipoPessoa? TipoPessoa,
    string? Cidade,
    string Status,
    IReadOnlyList<string> Problemas,
    IReadOnlyList<string> Alertas,
    string? GrupoDuplicidade,
    int? ClienteExistenteId,
    bool? ClienteExistenteAtivo,
    bool Selecionado,
    bool Ignorado,
    bool PodeSelecionar,
    bool Corrigido);

public sealed record ImportacaoLoteResponse(
    ImportacaoResumoResponse Resumo,
    PlanilhaMetricas Metricas,
    PagedResult<ImportacaoItemResponse> Itens);

public sealed record ImportacaoItemEdicaoResponse(
    Guid Id,
    string Aba,
    int Linha,
    ImportacaoClienteDados Dados,
    IReadOnlyList<string> Erros,
    IReadOnlyList<string> Alertas);

public sealed record ImportacaoConsulta(
    string? Filtro,
    string? Busca,
    int Pagina = 1,
    int TamanhoPagina = 50);

public sealed record ImportacaoSelecaoRequest(bool Selecionado);
public sealed record ImportacaoResolverDuplicidadeRequest(string GrupoDuplicidade, Guid ItemPrincipalId);

public sealed record ImportacaoResultadoResponse(
    Guid LoteId,
    ImportacaoLoteStatus Status,
    DateTime? ConfirmadoEm,
    int QuantidadeImportada,
    int QuantidadeIgnorada,
    int QuantidadeInvalida,
    int QuantidadeDuplicada,
    int QuantidadeExistente,
    string UsuarioResponsavel);

public sealed record ImportacaoArquivoEntrada(
    string NomeArquivo,
    long Tamanho,
    Stream Conteudo);

public sealed record ImportacaoOperacaoResult<T>(
    T? Value,
    string? Message,
    ImportacaoErroKind ErrorKind = ImportacaoErroKind.None)
{
    public bool IsSuccess => ErrorKind == ImportacaoErroKind.None;
}

public enum ImportacaoErroKind
{
    None,
    Validation,
    Conflict,
    NotFound,
    Forbidden,
    Expired
}

public interface IClientePlanilhaReader
{
    ImportacaoLeituraResultado Ler(Stream planilha);
}

public interface IImportacaoFileStore
{
    Task SaveStageAsync(ImportacaoLoteEstagio lote, CancellationToken cancellationToken);
    Task<ImportacaoLoteEstagio?> ReadStageAsync(Guid loteId, CancellationToken cancellationToken);
    Task DeleteStageAsync(Guid loteId, CancellationToken cancellationToken);
    Task SaveReportAsync(Guid loteId, string csv, CancellationToken cancellationToken);
    Task<string?> ReadReportAsync(Guid loteId, CancellationToken cancellationToken);
    Task CleanupAsync(DateTime utcNow, CancellationToken cancellationToken);
}

public interface IClienteImportacaoService
{
    Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> PrevisualizarAsync(ImportacaoArquivoEntrada arquivo, int usuarioId, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ObterAsync(Guid loteId, int usuarioId, ImportacaoConsulta consulta, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoItemEdicaoResponse>> ObterItemAsync(Guid loteId, Guid itemId, int usuarioId, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> CorrigirAsync(Guid loteId, Guid itemId, int usuarioId, ImportacaoClienteDados dados, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> SelecionarAsync(Guid loteId, Guid itemId, int usuarioId, bool selecionado, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoLoteResponse>> ResolverDuplicidadeAsync(Guid loteId, int usuarioId, ImportacaoResolverDuplicidadeRequest request, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoResultadoResponse>> ConfirmarAsync(Guid loteId, int usuarioId, string usuarioNome, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<ImportacaoResumoResponse>> CancelarAsync(Guid loteId, int usuarioId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImportacaoResumoResponse>> HistoricoAsync(int usuarioId, CancellationToken cancellationToken);
    Task<ImportacaoOperacaoResult<string>> RelatorioAsync(Guid loteId, int usuarioId, CancellationToken cancellationToken);
}
