using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using System.Collections.Concurrent;

namespace Pvm.Application.Tests.Submissions;

public sealed class SubmitShopriteInvoiceHandlerTests
{
    private static readonly SubmitShopriteInvoiceCommand Command = new(
        CommandId: Guid.Parse("624912ce-0a98-4056-9f7c-8671bb16a3fd"),
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
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
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
    public async Task Valid_invoice_without_matched_local_purchase_order_is_not_sent_to_shoprite()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([])
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.ValidationBlocked, result.Status);
        Assert.Equal("Invoice must match one loaded Shoprite PO before submission.", result.Message);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Timeout_or_unknown_outcome_returns_ambiguous_and_records_attempt()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
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
            HasMatchedPurchaseOrder = true,
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
            HasMatchedPurchaseOrder = true,
            HasSuccessfulSubmission = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.DuplicateBlocked, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Concurrent_delivery_of_the_same_command_sends_at_most_once()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient
        {
            Response = new ShopriteInvoiceResponse(true, 200, "accepted", IsAmbiguous: false)
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => handler.HandleAsync(Command, CancellationToken.None)));

        Assert.Equal(1, shopriteClient.SubmitCallCount);
    }

    [Fact]
    public async Task Client_exception_after_send_boundary_is_recorded_as_ambiguous()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient
        {
            Exception = new HttpRequestException("https://user:password@example.invalid")
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Ambiguous, result.Status);
        var attempt = Assert.Single(repository.Attempts);
        Assert.True(attempt.Response.IsAmbiguous);
        Assert.DoesNotContain("password", attempt.Response.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static CanonicalInvoice ValidInvoice()
        => new(
            AcumaticaInvoiceId: "INV-1",
            InvoiceNumber: "INV342699282",
            CustomerAccount: "SHOPRITE",
            CustomerLocation: "DC-01",
            ShopritePurchaseOrderNumber: "3869384391",
            SupplierGln: "9999999999999",
            SellerVatRegistrationNumber: "4010137059",
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
        private readonly object _sync = new();
        private readonly Dictionary<Guid, (PrepareSubmissionOperation Request, SubmissionOperation Operation)> _operations = [];

        public CanonicalInvoice? Invoice { get; init; }
        public ValidationResult ValidationResult { get; init; } = new([]);
        public bool HasMatchedPurchaseOrder { get; init; }
        public bool HasUnresolvedAmbiguousSubmission { get; init; }
        public bool HasSuccessfulSubmission { get; init; }
        public ConcurrentBag<RecordedAttempt> Attempts { get; } = [];

        public Task<InvoiceSubmissionSnapshot?> GetSubmissionSnapshotAsync(
            Guid invoiceCandidateId,
            CancellationToken cancellationToken)
            => Task.FromResult(Invoice is null
                ? null
                : new InvoiceSubmissionSnapshot(
                    invoiceCandidateId,
                    Invoice,
                    ValidationResult,
                    HasMatchedPurchaseOrder,
                    "candidate-key",
                    """{"source":"fixture"}""",
                    """{"canonical":"fixture"}""",
                    "source-version"));

        public Task<SubmissionOperation> GetOrCreateSubmissionOperationAsync(
            PrepareSubmissionOperation request,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_operations.TryGetValue(request.CommandId, out var existingCommand))
                {
                    return Task.FromResult(existingCommand.Operation);
                }

                var active = _operations.Values.FirstOrDefault(item =>
                    item.Operation.State is SubmissionOperationState.Pending
                        or SubmissionOperationState.Sending
                        or SubmissionOperationState.Submitted
                        or SubmissionOperationState.Ambiguous);
                if (active.Operation is not null)
                {
                    return Task.FromResult(active.Operation);
                }

                var initialState = HasUnresolvedAmbiguousSubmission
                    ? SubmissionOperationState.Ambiguous
                    : HasSuccessfulSubmission
                        ? SubmissionOperationState.Submitted
                        : SubmissionOperationState.Pending;
                var operation = new SubmissionOperation(
                    Guid.NewGuid(),
                    request.InvoiceCandidateId,
                    request.CommandId,
                    1,
                    initialState,
                    request.SourceVersion,
                    request.RequestPayload,
                    "payload-hash");
                _operations.Add(request.CommandId, (request, operation));
                return Task.FromResult(operation);
            }
        }

        public Task<bool> TryStartSubmissionOperationAsync(
            Guid submissionOperationId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var item = _operations.Single(pair => pair.Value.Operation.Id == submissionOperationId);
                if (item.Value.Operation.State != SubmissionOperationState.Pending)
                {
                    return Task.FromResult(false);
                }

                _operations[item.Key] = (
                    item.Value.Request,
                    item.Value.Operation with { State = SubmissionOperationState.Sending });
                return Task.FromResult(true);
            }
        }

        public Task<SubmissionOperation?> GetSubmissionOperationAsync(
            Guid submissionOperationId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var operation = _operations.Values
                    .Select(item => item.Operation)
                    .SingleOrDefault(item => item.Id == submissionOperationId);
                return Task.FromResult(operation);
            }
        }

        public Task CompleteSubmissionOperationAsync(
            Guid submissionOperationId,
            ShopriteInvoiceResponse response,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var item = _operations.Single(pair => pair.Value.Operation.Id == submissionOperationId);
                var state = response.IsAmbiguous
                    ? SubmissionOperationState.Ambiguous
                    : response.Success
                        ? SubmissionOperationState.Submitted
                        : SubmissionOperationState.Rejected;
                _operations[item.Key] = (
                    item.Value.Request,
                    item.Value.Operation with { State = state });
                Attempts.Add(new RecordedAttempt(
                    item.Value.Request.InvoiceCandidateId,
                    item.Value.Request.InitiatedBy,
                    item.Value.Request.InitiationMode,
                    item.Value.Request.RequestPayload,
                    response));
            }

            return Task.CompletedTask;
        }

        public Task<int> MarkStaleSendingOperationsAmbiguousAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset detectedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class FakeShopriteInvoiceClient : IShopriteInvoiceClient
    {
        public ShopriteInvoiceResponse Response { get; init; } = new(true, 200, "accepted", IsAmbiguous: false);
        public Exception? Exception { get; init; }
        private int _submitCallCount;

        public int SubmitCallCount => _submitCallCount;
        public string? LastXml { get; private set; }

        public Task<ShopriteInvoiceResponse> SubmitAsync(string xml, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _submitCallCount);
            LastXml = xml;
            if (Exception is not null)
            {
                throw Exception;
            }

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
