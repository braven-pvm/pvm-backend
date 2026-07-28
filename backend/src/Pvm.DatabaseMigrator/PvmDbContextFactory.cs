using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pvm.Infrastructure.Persistence;

namespace Pvm.DatabaseMigrator;

public sealed class PvmDbContextFactory : IDesignTimeDbContextFactory<PvmDbContext>
{
    public PvmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Pvm")
            ?? "Host=localhost;Port=54329;Database=pvm;Username=pvm;Password=pvm";
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new PvmDbContext(options);
    }
}
