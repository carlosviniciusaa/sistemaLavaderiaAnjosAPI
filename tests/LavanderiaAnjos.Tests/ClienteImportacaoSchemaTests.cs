using LavanderiaAnjos.Application.Clientes;

namespace LavanderiaAnjos.Tests;

public sealed class ClienteImportacaoSchemaTests
{
    [Fact]
    public void IdentificaExatamenteAsColunasIgnoradas()
    {
        Assert.Equal(3, ClienteImportacaoSchema.ColunasIgnoradas.Count);
        Assert.Contains("Boleto", ClienteImportacaoSchema.ColunasIgnoradas);
        Assert.Contains("Vencimento", ClienteImportacaoSchema.ColunasIgnoradas);
        Assert.Contains("Envio", ClienteImportacaoSchema.ColunasIgnoradas);
        Assert.DoesNotContain("E-mail Envio", ClienteImportacaoSchema.ColunasIgnoradas);
    }

    [Theory]
    [InlineData("Uniformes e hoteis")]
    [InlineData("Consumidor final")]
    public void TrataAsDuasAbasComoClientes(string nomeDaAba)
    {
        Assert.True(ClienteImportacaoSchema.RepresentaCliente(nomeDaAba));
    }

    [Fact]
    public void NaoAssociaColunasDeAtendimentoAoCliente()
    {
        Assert.Contains("Última", ClienteImportacaoSchema.ColunasDeAtendimento);
        Assert.Contains("Próximo", ClienteImportacaoSchema.ColunasDeAtendimento);
    }
}
