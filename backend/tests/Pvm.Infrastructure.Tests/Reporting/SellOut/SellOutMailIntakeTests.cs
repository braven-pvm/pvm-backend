using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pvm.Application.Reporting.SellOut;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Reporting.SellOut;
using Testcontainers.PostgreSql;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class SellOutMailIntakeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly FakeMailbox _mailbox = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private static byte[] MakroFile(string october = "6") => Encoding.Latin1.GetBytes(MakroDispoParserTests.File(
        MakroDispoParserTests.Row("EA", "1.000", ["0", "0", "0", "0", october, "0", "0", "0"])));

    private static SellOutMail Mail(string id, string sender = "person@pvm.co.za", string? authAs = "Internal", bool attachments = true) =>
        new(id, sender, "Makro report", new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero), attachments, authAs);

    [Theory]
    [InlineData("person@pvm.co.za", "Internal", true)]
    [InlineData("Person@PVM.CO.ZA", "Internal", true)]
    [InlineData("person@pvm.co.za", "Anonymous", false)]
    [InlineData("person@pvm.co.za", null, false)]
    [InlineData("person@example.com", "Internal", false)]
    [InlineData("person@notpvm.co.za", "Internal", false)]
    [InlineData(null, "Internal", false)]
    public void Only_an_internal_sender_of_an_allowed_domain_is_trusted(string? sender, string? authAs, bool expected)
    {
        Assert.Equal(expected, SellOutMailIntakeService.IsTrusted(Mail("1", sender!, authAs), ["pvm.co.za"]));
    }

    [Fact]
    public async Task An_attached_file_loads_and_the_mail_moves_to_loaded()
    {
        _mailbox.Add(Mail("m1"), new SellOutMailAttachment("Makro Dispo Report PVM 05112026.xls", "application/vnd.ms-excel", MakroFile()));

        var result = await RunAsync();

        Assert.Equal((1, 1, 0, 0), (result.Mails, result.Loaded, result.Failed, result.Refused));
        Assert.Equal(SellOutMailFolders.Loaded, _mailbox.Moved["m1"]);
        await using var db = CreateDbContext();
        var import = await db.SellOutImports.SingleAsync();
        Assert.Equal(SellOutImportStatuses.Loaded, import.Status);
        Assert.Equal("mail:person@pvm.co.za", import.ReceivedBy);
        Assert.Equal(6m, (await db.SellOutMonths.SingleAsync()).UnitsEach);
    }

    [Fact]
    public async Task A_mail_from_outside_is_not_read_and_moves_to_refused()
    {
        _mailbox.Add(Mail("m1", authAs: "Anonymous"), new SellOutMailAttachment("report.xls", null, MakroFile()));

        var result = await RunAsync();

        Assert.Equal(1, result.Refused);
        Assert.Equal(SellOutMailFolders.Refused, _mailbox.Moved["m1"]);
        Assert.Empty(_mailbox.AttachmentReads);
        await using var db = CreateDbContext();
        Assert.Empty(await db.SellOutImports.ToListAsync());
    }

    [Fact]
    public async Task A_mail_with_no_file_moves_to_refused()
    {
        _mailbox.Add(Mail("m1", attachments: false));

        var result = await RunAsync();

        Assert.Equal(1, result.Refused);
        Assert.Equal(SellOutMailFolders.Refused, _mailbox.Moved["m1"]);
    }

    [Fact]
    public async Task A_file_that_is_not_a_report_moves_the_mail_to_failed_and_images_are_left_out()
    {
        _mailbox.Add(
            Mail("m1"),
            new SellOutMailAttachment("logo.png", "image/png", [1, 2, 3]),
            new SellOutMailAttachment("notes.txt", "text/plain", "not a report"u8.ToArray()));

        var result = await RunAsync();

        Assert.Equal(1, result.Failed);
        Assert.Equal(SellOutMailFolders.Failed, _mailbox.Moved["m1"]);
        await using var db = CreateDbContext();
        var import = await db.SellOutImports.SingleAsync();
        Assert.Equal("notes.txt", import.FileName);
        Assert.Equal(SellOutImportStatuses.Refused, import.Status);
    }

    [Fact]
    public async Task The_same_file_sent_again_counts_as_loaded()
    {
        _mailbox.Add(Mail("m1"), new SellOutMailAttachment("a.xls", null, MakroFile()));
        _mailbox.Add(Mail("m2"), new SellOutMailAttachment("a.xls", null, MakroFile()));

        var result = await RunAsync();

        Assert.Equal(2, result.Loaded);
        Assert.Equal(SellOutMailFolders.Loaded, _mailbox.Moved["m2"]);
    }

    [Fact]
    public async Task A_switched_off_intake_reads_nothing()
    {
        _mailbox.Add(Mail("m1"), new SellOutMailAttachment("a.xls", null, MakroFile()));

        await using var db = CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, enabled: false).RunAsync(CancellationToken.None));
        Assert.Empty(_mailbox.Moved);
    }

    private async Task<SellOutMailIntakeResult> RunAsync()
    {
        await using var db = CreateDbContext();
        return await CreateService(db, enabled: true).RunAsync(CancellationToken.None);
    }

    private SellOutMailIntakeService CreateService(PvmDbContext db, bool enabled) => new(
        _mailbox,
        new SellOutImportService(db, [new MakroDispoParser(), new ShopriteVasParser(), .. WholesalerListParser.All()], new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<SellOutImportService>.Instance),
        Options.Create(new SellOutMailOptions { Enabled = enabled, Mailbox = "reports@pvm.co.za" }),
        NullLogger<SellOutMailIntakeService>.Instance);

    private PvmDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PvmDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class FakeMailbox : ISellOutMailbox
    {
        private readonly List<(SellOutMail Mail, SellOutMailAttachment[] Files)> _inbox = [];

        public Dictionary<string, string> Moved { get; } = [];

        public List<string> AttachmentReads { get; } = [];

        public void Add(SellOutMail mail, params SellOutMailAttachment[] files) => _inbox.Add((mail, files));

        public Task<IReadOnlyList<SellOutMail>> ListInboxAsync(int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SellOutMail>>(_inbox.Select(item => item.Mail).Where(mail => !Moved.ContainsKey(mail.Id)).Take(max).ToList());

        public Task<IReadOnlyList<SellOutMailAttachment>> GetAttachmentsAsync(string mailId, CancellationToken cancellationToken)
        {
            AttachmentReads.Add(mailId);
            return Task.FromResult<IReadOnlyList<SellOutMailAttachment>>(_inbox.Single(item => item.Mail.Id == mailId).Files);
        }

        public Task MoveAsync(string mailId, string folder, CancellationToken cancellationToken)
        {
            Moved[mailId] = folder;
            return Task.CompletedTask;
        }
    }
}
