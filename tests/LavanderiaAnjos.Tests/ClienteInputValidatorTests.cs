using LavanderiaAnjos.Application.Clientes;
using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Tests;

public sealed class ClienteInputValidatorTests
{
    [Fact]
    public void NormalizaCadastroValidoComCamposOpcionaisVazios()
    {
        var result = ClienteInputValidator.Validate(Request(
            nome: "  Cliente com Acentuação  ",
            documento: "529.982.247-25",
            cep: "01769-000",
            telefone: "(14) 99999-8888",
            uf: "sp",
            emailEnvio: "contato@exemplo.com; fiscal@exemplo.com"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Cliente com Acentuação", result.Value!.NomeRazaoSocial);
        Assert.Equal("52998224725", result.Value.Documento);
        Assert.Equal(TipoPessoa.PessoaFisica, result.Value.TipoPessoa);
        Assert.Equal("01769000", result.Value.Cep);
        Assert.Equal("14999998888", result.Value.Telefone);
        Assert.Equal("SP", result.Value.Uf);
        Assert.Null(result.Value.InscricaoEstadual);
        Assert.Null(result.Value.InscricaoMunicipal);
    }

    [Theory]
    [InlineData(null, "52998224725", "nomeRazaoSocial")]
    [InlineData("Cliente", null, "documento")]
    [InlineData("Cliente", "123", "documento")]
    public void RejeitaObrigatoriosInvalidos(string? nome, string? documento, string campo)
    {
        var result = ClienteInputValidator.Validate(Request(nome, documento));
        Assert.False(result.IsSuccess);
        Assert.Contains(campo, result.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejeitaEmailInvalido()
    {
        var result = ClienteInputValidator.Validate(Request(emailEnvio: "valido@exemplo.com; email-invalido"));
        Assert.Contains("emailEnvio", result.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static ClienteCreateRequest Request(
        string? nome = "Cliente Teste",
        string? documento = "52998224725",
        string? cep = null,
        string? telefone = null,
        string? uf = null,
        string? emailEnvio = null) =>
        new(null, nome, documento, false, null, null, null, cep, null, null, null, null, null, uf, telefone, emailEnvio, null, null);
}
