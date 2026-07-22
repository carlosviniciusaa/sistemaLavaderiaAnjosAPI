using LavanderiaAnjos.Domain.Importacoes;
using LavanderiaAnjos.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LavanderiaAnjos.Infrastructure.Persistence.Configurations;

public sealed class ImportacaoClienteLoteConfiguration : IEntityTypeConfiguration<ImportacaoClienteLote>
{
    public void Configure(EntityTypeBuilder<ImportacaoClienteLote> builder)
    {
        builder.ToTable("ImportacoesClientes");
        builder.HasKey(lote => lote.Id);
        builder.Property(lote => lote.HashArquivo).HasColumnType("char(64)").IsRequired();
        builder.Property(lote => lote.NomeArquivo).HasMaxLength(260).IsRequired();
        builder.Property(lote => lote.Status).HasConversion<int>().IsRequired();
        builder.Property(lote => lote.CriadoEm).HasColumnType("datetime2");
        builder.Property(lote => lote.ExpiraEm).HasColumnType("datetime2");
        builder.Property(lote => lote.ConfirmadoEm).HasColumnType("datetime2");

        builder.HasIndex(lote => lote.HashArquivo)
            .HasDatabaseName("IX_ImportacoesClientes_HashArquivo");
        builder.HasIndex(lote => new { lote.UsuarioId, lote.CriadoEm })
            .HasDatabaseName("IX_ImportacoesClientes_UsuarioId_CriadoEm");
        builder.HasIndex(lote => lote.HashArquivo)
            .IsUnique()
            .HasFilter("[Status] = 2")
            .HasDatabaseName("UX_ImportacoesClientes_Hash_Concluida");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(lote => lote.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
