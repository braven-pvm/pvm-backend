using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Pvm");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Environment variable 'ConnectionStrings__Pvm' is required.");
}

var options = new DbContextOptionsBuilder<PvmDbContext>()
    .UseNpgsql(connectionString)
    .Options;
await using var dbContext = new PvmDbContext(options);
await DatabaseMigrationRunner.MigrateAsync(dbContext);

var applied = await dbContext.Database.GetAppliedMigrationsAsync();
Console.WriteLine($"Database migration complete. Applied migrations: {string.Join(", ", applied)}");
