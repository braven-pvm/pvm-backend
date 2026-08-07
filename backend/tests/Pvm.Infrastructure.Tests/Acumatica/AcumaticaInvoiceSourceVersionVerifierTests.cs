using System.Text.Json;
using Pvm.Application.Acumatica;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Tests.Acumatica;

public sealed class AcumaticaInvoiceSourceVersionVerifierTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsCurrentWhenLastModifiedVersionMatches()
    {
        var prepared = NewInvoice(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
        var verifier = new AcumaticaInvoiceSourceVersionVerifier(new StubClient(prepared));

        var result = await verifier.VerifyAsync(JsonSerializer.Serialize(prepared), CancellationToken.None);

        Assert.True(result.IsCurrent);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsChangedWhenCurrentVersionDiffers()
    {
        var prepared = NewInvoice(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
        var current = prepared with { LastModifiedAt = prepared.LastModifiedAt!.Value.AddMinutes(1) };
        var verifier = new AcumaticaInvoiceSourceVersionVerifier(new StubClient(current));

        var result = await verifier.VerifyAsync(JsonSerializer.Serialize(prepared), CancellationToken.None);

        Assert.False(result.IsCurrent);
        Assert.Contains("changed after preparation", result.Message);
    }

    private static AcumaticaInvoiceDto NewInvoice(DateTimeOffset lastModifiedAt)
        => new(
            "invoice-id",
            "INV000123",
            "Open",
            "SHOPRITE",
            null,
            "PO123",
            "ZAR",
            "ZA",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            100m,
            115m,
            15m,
            [],
            lastModifiedAt);

    private sealed class StubClient(AcumaticaInvoiceDto? current) : IAcumaticaInvoiceClient
    {
        public Task<IReadOnlyList<AcumaticaInvoiceDto>> FetchFinalizedInvoicesAsync(
            AcumaticaInvoiceQuery? query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AcumaticaInvoiceDto>>([]);

        public Task<AcumaticaInvoiceDto?> FetchFinalizedInvoiceAsync(
            string invoiceId,
            CancellationToken cancellationToken)
            => Task.FromResult(current);
    }
}
