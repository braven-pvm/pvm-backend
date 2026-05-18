using Microsoft.EntityFrameworkCore;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
}
