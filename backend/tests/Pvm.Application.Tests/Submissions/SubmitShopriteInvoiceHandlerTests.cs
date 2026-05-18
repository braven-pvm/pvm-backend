using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;

namespace Pvm.Application.Tests.Submissions;

public sealed class SubmitShopriteInvoiceHandlerTests
{
    private static readonly SubmitShopriteInvoiceCommand Command = new(
        InvoiceCandidateId: Guid.Parse("9bc85839-70e4-4dbf-900a-c0e5d2608c50"),
        InitiatedBy: "qa-user",
        InitiationMode: "manual");

    [Fact]
    public async Task Invalid_invoice_is_not_sent_to_shoprite()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult(
            [
                new ValidationIssue("missing-gln", "Store/DC GLN is missing.", ValidationSeverity.Blocking, "integration-config")
            ])
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.ValidationBlocked, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Valid_invoice_calls_shoprite_client_and_records_attempt()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([])
        };
        var shopriteClient = new FakeShopriteInvoiceClient
        {
            Response = new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false)
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Submitted, result.Status);
        Assert.Equal(1, shopriteClient.SubmitCallCount);

        var attempt = Assert.Single(repository.Attempts);
        Assert.Equal(Command.InvoiceCandidateId, attempt.InvoiceCandidateId);
        Assert.Equal(Command.InitiatedBy, attempt.InitiatedBy);
        Assert.Equal(Command.InitiationMode, attempt.InitiationMode);
        Assert.Equal(shopriteClient.LastXml, attempt.Xml);
        Assert.Contains("INV342699282", attempt.Xml);
        Assert.Equal(shopriteClient.Response, attempt.Response);
    }

    [Fact]
    public async Task Timeout_or_unknown_outcome_returns_ambiguous_and_records_attempt()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([])
        };
        var shopriteClient = new FakeShopriteInvoiceClient
        {
            Response = new ShopriteInvoiceResponse(false, null, "Shoprite request timed out.", IsAmbiguous: true)
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Ambiguous, result.Status);
        Assert.Equal(1, shopriteClient.SubmitCallCount);

        var attempt = Assert.Single(repository.Attempts);
        Assert.Equal(shopriteClient.Response, attempt.Response);
        Assert.Equal(shopriteClient.LastXml, attempt.Xml);
    }

    [Fact]
    public async Task Prior_unresolved_ambiguous_attempt_blocks_retry_and_does_not_call_client()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasUnresolvedAmbiguousSubmission = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.ManualReviewRequired, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Duplicate_key_already_submitted_returns_duplicate_blocked_and_does_not_call_client()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasSuccessfulSubmission = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.DuplicateBlocked, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    private static CanonicalInvoice ValidInvoice()
        => new(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV342699282",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            StoreDcGln: "6001001018104",
            CountryCode: "ZA",
            CurrencyCode: "ZAR",
            InvoiceDate: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero),
            TotalExcludingTax: new Money("ZAR", 109.8765m),
            TotalIncludingTax: new Money("ZAR", 125.1789m),
            TotalTax: new Money("ZAR", 15.3024m),
            Lines:
            [
                new CanonicalInvoiceLine(
                    LineNumber: 1,
                    AcumaticaInventoryId: "SKU-1",
                    Gtin: "16001069205048",
                    Description: "Item 1",
                    Quantity: 1m,
                    AcumaticaUom: "EA",
                    ShopriteUom: ShopriteMeasurementUnit.EA,
                    PackSize: 24m,
                    UnitAmountExcludingTax: new Money("ZAR", 109.8765m),
                    UnitAmountIncludingTax: new Money("ZAR", 125.1789m),
                    TaxAmount: new Money("ZAR", 15.3024m),
                    TaxCategoryCode: "STANDARD",
                    TaxPercentage: 15m,
                    IsCatchWeight: false)
            ]);

    private sealed class FakeInvoiceCandidateRepository : IInvoiceCandidateRepository
    {
        public CanonicalInvoice? Invoice { get; init; }
        public ValidationResult ValidationResult { get; init; } = new([]);
        public bool HasUnresolvedAmbiguousSubmission { get; init; }
        public bool HasSuccessfulSubmission { get; init; }
        public List<RecordedAttempt> Attempts { get; } = [];

        public Task<CanonicalInvoice?> GetCanonicalInvoiceAsync(Guid invoiceCandidateId, CancellationToken cancellationToken)
            => Task.FromResult(Invoice);

        public Task<ValidationResult> GetValidationResultAsync(Guid invoiceCandidateId, CancellationToken cancellationToken)
            => Task.FromResult(ValidationResult);

        public Task<bool> HasUnresolvedAmbiguousSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken)
            => Task.FromResult(HasUnresolvedAmbiguousSubmission);

        public Task<bool> HasSuccessfulSubmissionAsync(Guid invoiceCandidateId, CancellationToken cancellationToken)
            => Task.FromResult(HasSuccessfulSubmission);

        public Task RecordAttemptAsync(
            Guid invoiceCandidateId,
            string initiatedBy,
            string initiationMode,
            string xml,
            ShopriteInvoiceResponse response,
            CancellationToken cancellationToken)
        {
            Attempts.Add(new RecordedAttempt(invoiceCandidateId, initiatedBy, initiationMode, xml, response));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeShopriteInvoiceClient : IShopriteInvoiceClient
    {
        public ShopriteInvoiceResponse Response { get; init; } = new(true, 200, "accepted", IsAmbiguous: false);
        public int SubmitCallCount { get; private set; }
        public string? LastXml { get; private set; }

        public Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken)
        {
            SubmitCallCount++;
            LastXml = xml;
            return Task.FromResult(Response);
        }
    }

    private sealed record RecordedAttempt(
        Guid InvoiceCandidateId,
        string InitiatedBy,
        string InitiationMode,
        string Xml,
        ShopriteInvoiceResponse Response);
}
