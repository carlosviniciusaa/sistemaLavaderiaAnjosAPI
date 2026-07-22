using LavanderiaAnjos.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LavanderiaAnjos.Infrastructure.Persistence.Configurations;

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuarios");
        builder.HasKey(usuario => usuario.Id);
        builder.Property(usuario => usuario.Id).UseIdentityColumn();
        builder.Property(usuario => usuario.Nome).HasMaxLength(150).IsRequired();
        builder.Property(usuario => usuario.Login).HasMaxLength(100).IsRequired();
        builder.Property(usuario => usuario.LoginNormalizado).HasMaxLength(100).IsRequired();
        builder.Property(usuario => usuario.SenhaHash).HasMaxLength(500).IsRequired();
        builder.Property(usuario => usuario.Ativo).HasDefaultValue(true);
        builder.Property(usuario => usuario.CriadoEm).HasColumnType("datetime2");
        builder.Property(usuario => usuario.AtualizadoEm).HasColumnType("datetime2");
        builder.Property(usuario => usuario.VersaoSessao).HasDefaultValue(0);
        builder.Property(usuario => usuario.Versao).HasDefaultValue(1L).IsConcurrencyToken();
        builder.HasIndex(usuario => usuario.Login)
            .IsUnique()
            .HasDatabaseName("UX_Usuarios_Login");
        builder.HasIndex(usuario => usuario.LoginNormalizado)
            .IsUnique()
            .HasDatabaseName("UX_Usuarios_LoginNormalizado");
    }
}
