using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Tests;

public sealed class DocumentoFiscalTests
{
    [Theory]
    [InlineData("529.982.247-25", TipoPessoa.PessoaFisica)]
    [InlineData("52998224725", TipoPessoa.PessoaFisica)]
    [InlineData("11.222.333/0001-81", TipoPessoa.PessoaJuridica)]
    [InlineData("11222333000181", TipoPessoa.PessoaJuridica)]
    public void ValidaDocumentoEIdentificaTipo(string documento, TipoPessoa tipoEsperado)
    {
        Assert.True(DocumentoFiscal.TryValidate(documento, out var tipo));
        Assert.Equal(tipoEsperado, tipo);
    }

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("11.222.333/0001-80")]
    [InlineData("111.111.111-11")]
    [InlineData("00.000.000/0000-00")]
    [InlineData("")]
    [InlineData("123")]
    public void RejeitaDocumentoInvalido(string documento) =>
        Assert.False(DocumentoFiscal.TryValidate(documento, out _));

    [Fact]
    public void NormalizaDocumentoFormatado() =>
        Assert.Equal("11222333000181", DocumentoFiscal.Normalize(" 11.222.333/0001-81 "));

    [Theory]
    [InlineData("52998224725", "529.982.247-25")]
    [InlineData("11222333000181", "11.222.333/0001-81")]
    public void FormataDocumento(string documento, string esperado) =>
        Assert.Equal(esperado, DocumentoFiscal.Format(documento));
}
