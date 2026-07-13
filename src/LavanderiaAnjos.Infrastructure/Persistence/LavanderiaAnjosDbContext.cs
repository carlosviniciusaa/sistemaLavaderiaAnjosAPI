using Microsoft.EntityFrameworkCore;

namespace LavanderiaAnjos.Infrastructure.Persistence;

public sealed class LavanderiaAnjosDbContext(DbContextOptions<LavanderiaAnjosDbContext> options)
    : DbContext(options)
{
    // O contexto começa intencionalmente sem DbSets. A entidade Cliente e sua
    // migration serão criadas depois da validação do modelo preliminar.
}
