using LavanderiaAnjos.Domain.Clientes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LavanderiaAnjos.Infrastructure.Persistence.Configurations;

public sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("Clientes");
        builder.HasKey(cliente => cliente.Id);
        builder.Property(cliente => cliente.Id).UseIdentityColumn();
        builder.Property(cliente => cliente.NomeRazaoSocial).HasMaxLength(200).IsRequired();
        builder.Property(cliente => cliente.Documento).HasColumnType("varchar(14)").IsRequired();
        builder.Property(cliente => cliente.TipoPessoa).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        builder.Property(cliente => cliente.InscricaoEstadual).HasMaxLength(30);
        builder.Property(cliente => cliente.InscricaoMunicipal).HasMaxLength(30);
        builder.Property(cliente => cliente.DataInicioRelacionamento).HasColumnType("date");
        builder.Property(cliente => cliente.Cep).HasColumnType("varchar(8)");
        builder.Property(cliente => cliente.Logradouro).HasMaxLength(200);
        builder.Property(cliente => cliente.Numero).HasMaxLength(30);
        builder.Property(cliente => cliente.Complemento).HasMaxLength(150);
        builder.Property(cliente => cliente.Bairro).HasMaxLength(100);
        builder.Property(cliente => cliente.Cidade).HasMaxLength(100);
        builder.Property(cliente => cliente.Uf).HasColumnType("varchar(2)");
        builder.Property(cliente => cliente.Telefone).HasColumnType("varchar(20)");
        builder.Property(cliente => cliente.EmailEnvio).HasMaxLength(500);
        builder.Property(cliente => cliente.EmailNotaFiscal).HasMaxLength(500);
        builder.Property(cliente => cliente.Observacoes).HasMaxLength(2000);
        builder.Property(cliente => cliente.Ativo).HasDefaultValue(true);
        builder.Property(cliente => cliente.CriadoEm).HasColumnType("datetime2");
        builder.Property(cliente => cliente.AtualizadoEm).HasColumnType("datetime2");

        builder.HasIndex(cliente => cliente.Documento)
            .IsUnique()
            .HasFilter("[Ativo] = 1")
            .HasDatabaseName("UX_Clientes_Documento_Ativo");
        builder.HasIndex(cliente => cliente.NomeRazaoSocial)
            .HasDatabaseName("IX_Clientes_NomeRazaoSocial");
        builder.HasIndex(cliente => cliente.CodigoLegado)
            .HasFilter("[CodigoLegado] IS NOT NULL")
            .HasDatabaseName("IX_Clientes_CodigoLegado");
        builder.HasIndex(cliente => new { cliente.Ativo, cliente.NomeRazaoSocial })
            .HasDatabaseName("IX_Clientes_Ativo_NomeRazaoSocial");
    }
}
