using LavanderiaAnjos.Domain.Clientes;
using LavanderiaAnjos.Domain.Importacoes;
using LavanderiaAnjos.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace LavanderiaAnjos.Infrastructure.Persistence;

public sealed class LavanderiaAnjosDbContext(DbContextOptions<LavanderiaAnjosDbContext> options)
    : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ImportacaoClienteLote> ImportacoesClientes => Set<ImportacaoClienteLote>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LavanderiaAnjosDbContext).Assembly);
    }
}
