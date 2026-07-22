using ClosedXML.Excel;
using LavanderiaAnjos.Infrastructure.Importacoes;

namespace LavanderiaAnjos.Tests;

public sealed class ClientePlanilhaReaderTests
{
    [Fact]
    public void LeAsDuasAbasPreservaFormatacaoEIgnoraColunasForaDoEscopo()
    {
        using var stream = WorkbookFixture.CreateStream();
        var result = new ClientePlanilhaReader().Ler(stream);

        Assert.Equal(2, result.Metricas.RegistrosConsiderados);
        Assert.Equal(4, result.Metricas.LinhasFisicas);
        Assert.Equal(2, result.Metricas.LinhasCabecalho);
        var uniform = Assert.Single(result.Itens, item => item.Aba == "Uniformes e hoteis");
        Assert.Equal("52998224725", uniform.DocumentoEncontrado);
        Assert.Equal("01769000", uniform.Dados.Cep);
        Assert.Equal(new DateOnly(2024, 1, 15), uniform.Dados.DataInicioRelacionamento);
        Assert.Null(uniform.Dados.Complemento);
        Assert.DoesNotContain("Cobrança", uniform.Dados.Observacoes ?? string.Empty, StringComparison.Ordinal);
        var consumer = Assert.Single(result.Itens, item => item.Aba == "Consumidor final");
        Assert.False(consumer.Dados.ProdutorRural);
        Assert.Equal("Sala 2", consumer.Dados.Complemento);
    }

    [Fact]
    public void RejeitaAbaOuCabecalhoObrigatorioAusente()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Uniformes e hoteis").Cell("A1").Value = "Nome";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream); stream.Position = 0;

        var exception = Assert.Throws<InvalidDataException>(() => new ClientePlanilhaReader().Ler(stream));
        Assert.Contains("cabeçalhos obrigatórios", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejeitaArquivoCorrompido()
    {
        using var stream = new MemoryStream("isto não é xlsx"u8.ToArray());
        var exception = Assert.Throws<InvalidDataException>(() => new ClientePlanilhaReader().Ler(stream));
        Assert.Contains("corrompido", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class WorkbookFixture
{
    private static readonly string[] UniformHeaders =
    [
        "Código", "Nome", "CNPJ ou CPF", "Produtor Rural", "I.E.", "I.M.", "Início", "Logradouro", "Nº",
        "Bairro", "Cidade", "UF", "CEP", "Telefone", "E-mail Envio", "E-mail NF", "Boleto", "Vencimento", "Envio", "Última", "Próximo"
    ];

    private static readonly string[] ConsumerHeaders =
    [
        "Nome", "CNPJ ou CPF", "I.E.", "I.M.", "CEP", "Logradouro", "Nº", "Complemento", "Bairro", "Cidade",
        "UF", "Telefone", "E-mail Envio", "E-mail NF", "Boleto", "Vencimento", "Envio"
    ];

    public static MemoryStream CreateStream()
    {
        using var workbook = new XLWorkbook();
        var uniform = workbook.AddWorksheet("Uniformes e hoteis");
        WriteHeaders(uniform, UniformHeaders);
        uniform.Cell(2, 1).Value = 7;
        uniform.Cell(2, 2).Value = "Cliente Uniforme";
        uniform.Cell(2, 3).Value = 52_998_224_725d;
        uniform.Cell(2, 3).Style.NumberFormat.Format = "00000000000";
        uniform.Cell(2, 4).Value = "Não";
        uniform.Cell(2, 7).Value = new DateTime(2024, 1, 15);
        uniform.Cell(2, 8).Value = "Rua A";
        uniform.Cell(2, 9).Value = "10";
        uniform.Cell(2, 10).Value = "Centro";
        uniform.Cell(2, 11).Value = "Bastos";
        uniform.Cell(2, 12).Value = "SP";
        uniform.Cell(2, 13).Value = 1_769_000;
        uniform.Cell(2, 13).Style.NumberFormat.Format = "00000000";
        uniform.Cell(2, 14).Value = "14999999999";
        uniform.Cell(2, 15).Value = "uniforme@exemplo.com";
        uniform.Cell(2, 17).Value = "Cobrança ignorada";

        var consumer = workbook.AddWorksheet("Consumidor final");
        WriteHeaders(consumer, ConsumerHeaders);
        consumer.Cell(2, 1).Value = "Cliente Consumidor";
        consumer.Cell(2, 2).Value = 11_222_333_000_181d;
        consumer.Cell(2, 2).Style.NumberFormat.Format = "00000000000000";
        consumer.Cell(2, 5).Value = "17500000";
        consumer.Cell(2, 6).Value = "Avenida B";
        consumer.Cell(2, 7).Value = "20";
        consumer.Cell(2, 8).Value = "Sala 2";
        consumer.Cell(2, 9).Value = "Centro";
        consumer.Cell(2, 10).Value = "Marília";
        consumer.Cell(2, 11).Value = "SP";
        consumer.Cell(2, 12).Value = "1433334444";
        consumer.Cell(2, 13).Value = "consumidor@exemplo.com";

        var stream = new MemoryStream();
        workbook.SaveAs(stream); stream.Position = 0; return stream;
    }

    private static void WriteHeaders(IXLWorksheet worksheet, string[] headers)
    {
        for (var index = 0; index < headers.Length; index++) worksheet.Cell(1, index + 1).Value = headers[index];
    }
}
