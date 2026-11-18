using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Documents;
using Pvm.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Documents;

/// <summary>
/// Device enrolment on Postgres (design 6.10): the conditional update that lets one setup code
/// enrol once, even under a race, and the key lookup.
/// </summary>
public sealed class ScannerDeviceServiceTests : IAsyncLifetime
{
    private const string Admin = "admin@pvm.co.za";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await DatabaseMigrationRunner.MigrateAsync(db);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Create_StoresOnlyHashes_AndEnrol_ReturnsAKeyThatAuthenticates()
    {
        var issued = await CreateAsync("Dispatch phone 1");

        await using (var db = CreateDbContext())
        {
            var code = await db.ScannerDeviceSetupCodes.SingleAsync();
            Assert.Equal(ScannerDeviceService.Hash(issued.Code), code.CodeHash);
            Assert.NotEqual(issued.Code, code.CodeHash);
            Assert.Null(code.UsedAt);
            Assert.Equal(issued.Device.Id, code.DeviceId);
        }

        var enrolment = await EnrolAsync(issued.Code);

        Assert.NotNull(enrolment);
        Assert.StartsWith(ScannerDeviceService.KeyPrefix, enrolment.DeviceKey);
        Assert.Equal("Dispatch phone 1", enrolment.DeviceName);
        await using (var db = CreateDbContext())
        {
            var device = await db.ScannerDevices.SingleAsync();
            Assert.Equal(ScannerDeviceService.Hash(enrolment.DeviceKey), device.KeyHash);
            Assert.NotNull(device.EnrolledAt);
            Assert.NotNull((await db.ScannerDeviceSetupCodes.SingleAsync()).UsedAt);
        }

        await using (var db = CreateDbContext())
        {
            var found = await new ScannerDeviceService(db).AuthenticateAsync(enrolment.DeviceKey, default);
            Assert.NotNull(found);
            Assert.Equal(issued.Device.Id, found.Id);
        }
    }

    [Fact]
    public async Task Enrol_SameCodeConcurrently_EnrolsOnce()
    {
        var issued = await CreateAsync("Dispatch phone 1");

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => EnrolAsync(issued.Code))));

        var winner = Assert.Single(attempts, attempt => attempt is not null);
        await using var db = CreateDbContext();
        var device = await db.ScannerDevices.SingleAsync();
        Assert.Equal(ScannerDeviceService.Hash(winner!.DeviceKey), device.KeyHash);
    }

    [Fact]
    public async Task Enrol_UsedExpiredUnknownOrRevoked_ReturnsNull()
    {
        var used = await CreateAsync("Used");
        Assert.NotNull(await EnrolAsync(used.Code));
        Assert.Null(await EnrolAsync(used.Code));

        var expired = await CreateAsync("Expired");
        await using (var db = CreateDbContext())
        {
            await db.ScannerDeviceSetupCodes
                .Where(code => code.DeviceId == expired.Device.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        Assert.Null(await EnrolAsync(expired.Code));

        Assert.Null(await EnrolAsync("not-a-code"));

        var revoked = await CreateAsync("Revoked");
        await using (var db = CreateDbContext())
        {
            Assert.Equal(RevokeOutcome.Revoked, await new ScannerDeviceService(db).RevokeAsync(revoked.Device.Id, Admin, default));
        }

        Assert.Null(await EnrolAsync(revoked.Code));
    }

    [Fact]
    public async Task NewSetupCode_InvalidatesTheOldCode_AndReEnrolmentReplacesTheKey()
    {
        var issued = await CreateAsync("Dispatch phone 1");
        var first = await EnrolAsync(issued.Code);
        Assert.NotNull(first);

        IssuedSetupCode second;
        IssuedSetupCode third;
        await using (var db = CreateDbContext())
        {
            var service = new ScannerDeviceService(db);
            second = (await service.NewSetupCodeAsync(issued.Device.Id, Admin, default)).Issued!;
            third = (await service.NewSetupCodeAsync(issued.Device.Id, Admin, default)).Issued!;
        }

        Assert.Null(await EnrolAsync(second.Code));
        var again = await EnrolAsync(third.Code);
        Assert.NotNull(again);

        await using var check = CreateDbContext();
        var service2 = new ScannerDeviceService(check);
        Assert.Null(await service2.AuthenticateAsync(first.DeviceKey, default));
        Assert.NotNull(await service2.AuthenticateAsync(again.DeviceKey, default));
    }

    [Fact]
    public async Task Create_NameOfAnActiveDevice_IsTaken_ButARevokedNameIsFree()
    {
        var issued = await CreateAsync("Dispatch phone 1");

        await using var db = CreateDbContext();
        var service = new ScannerDeviceService(db);
        Assert.Equal(CreateDeviceOutcome.NameTaken, (await service.CreateAsync("dispatch phone 1", Admin, default)).Outcome);

        await service.RevokeAsync(issued.Device.Id, Admin, default);
        Assert.Equal(CreateDeviceOutcome.Created, (await service.CreateAsync("Dispatch phone 1", Admin, default)).Outcome);
    }

    private async Task<IssuedSetupCode> CreateAsync(string name)
    {
        await using var db = CreateDbContext();
        var result = await new ScannerDeviceService(db).CreateAsync(name, Admin, default);
        Assert.Equal(CreateDeviceOutcome.Created, result.Outcome);
        return result.Issued!;
    }

    private async Task<DeviceEnrolment?> EnrolAsync(string code)
    {
        await using var db = CreateDbContext();
        return await new ScannerDeviceService(db).EnrolAsync(code, default);
    }

    private PvmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PvmDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new PvmDbContext(options);
    }
}
