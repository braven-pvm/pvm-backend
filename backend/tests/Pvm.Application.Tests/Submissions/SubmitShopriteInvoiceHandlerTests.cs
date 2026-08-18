using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using System.Collections.Concurrent;
using Pvm.Application.Automation;

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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

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
        var archive = new FakePayloadArchive();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, archive);

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
        Assert.Collection(
            archive.Writes,
            payload => Assert.Equal(PayloadArchiveKind.AcumaticaSource, payload.Kind),
            payload => Assert.Equal(PayloadArchiveKind.CanonicalInvoice, payload.Kind),
            payload => Assert.Equal(PayloadArchiveKind.ShopriteRequest, payload.Kind),
            payload => Assert.Equal(PayloadArchiveKind.ShopriteResponse, payload.Kind));
        Assert.True(repository.PreparedPayloadsRecordedBeforeStart);
    }

    [Fact]
    public async Task Held_invoice_is_not_sent_to_shoprite()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true,
            CandidateStatus = "Suspended"
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.PolicyBlocked, result.Status);
        Assert.Contains("hold", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.ValidationBlocked, result.Status);
        Assert.Equal("Invoice must match one loaded Shoprite PO before submission.", result.Message);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Automatic_submission_with_changed_source_is_not_sent_to_shoprite()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var verifier = new FakeSourceVersionVerifier(
            new InvoiceSourceVersionVerification(
                false,
                "The Acumatica invoice changed after preparation and must be reconciled again."));
        var handler = new SubmitShopriteInvoiceHandler(
            repository,
            shopriteClient,
            new FakePayloadArchive(),
            verifier);
        var command = Command with { InitiationMode = "automatic" };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.ManualReviewRequired, result.Status);
        Assert.Equal(1, verifier.CallCount);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Shadow_mode_blocks_automatic_submission_before_operation_or_external_send()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var gate = new FakeAutomationGate(new AutomationSubmissionPermission(
            false,
            4,
            "automation-mode-blocked",
            "Automatic submission is blocked while mode is Shadow."));
        var handler = new SubmitShopriteInvoiceHandler(
            repository,
            shopriteClient,
            new FakePayloadArchive(),
            automationGate: gate);

        var result = await handler.HandleAsync(
            Command with { InitiationMode = "automatic" },
            CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.PolicyBlocked, result.Status);
        Assert.Equal(1, gate.CallCount);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Emergency_stop_blocks_manual_submission()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var gate = new FakeAutomationGate(new AutomationSubmissionPermission(
            false,
            5,
            "emergency-stop",
            "Invoice submission is disabled by the emergency stop."));
        var handler = new SubmitShopriteInvoiceHandler(
            repository,
            shopriteClient,
            new FakePayloadArchive(),
            automationGate: gate);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.PolicyBlocked, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Policy_change_before_claim_cancels_pending_operation_without_sending()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true,
            RejectStart = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var gate = new FakeAutomationGate(
            new AutomationSubmissionPermission(true, 7, "eligible", "Eligible."),
            new AutomationSubmissionPermission(false, 8, "emergency-stop", "Emergency stop active."));
        var handler = new SubmitShopriteInvoiceHandler(
            repository,
            shopriteClient,
            new FakePayloadArchive(),
            automationGate: gate);

        var result = await handler.HandleAsync(
            Command with { InitiationMode = "automatic" },
            CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.PolicyBlocked, result.Status);
        Assert.True(repository.PendingOperationWasCancelled);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

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
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, new FakePayloadArchive());

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Ambiguous, result.Status);
        var attempt = Assert.Single(repository.Attempts);
        Assert.True(attempt.Response.IsAmbiguous);
        Assert.DoesNotContain("password", attempt.Response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Archive_failure_before_send_boundary_does_not_call_shoprite()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var archive = new FakePayloadArchive
        {
            WriteException = new IOException("archive unavailable")
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, archive);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Failed, result.Status);
        Assert.Contains("Nothing was sent", result.Message);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
        Assert.False(repository.StartWasCalled);
    }

    [Fact]
    public async Task Archive_race_returns_the_winning_operation_state_instead_of_failure()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true,
            SimulateArchiveRace = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var handler = new SubmitShopriteInvoiceHandler(
            repository,
            shopriteClient,
            new FakePayloadArchive());

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.InProgress, result.Status);
        Assert.Equal(0, shopriteClient.SubmitCallCount);
    }

    [Fact]
    public async Task Response_archive_failure_after_send_is_marked_ambiguous()
    {
        var repository = new FakeInvoiceCandidateRepository
        {
            Invoice = ValidInvoice(),
            ValidationResult = new ValidationResult([]),
            HasMatchedPurchaseOrder = true
        };
        var shopriteClient = new FakeShopriteInvoiceClient();
        var archive = new FakePayloadArchive
        {
            FailingKind = PayloadArchiveKind.ShopriteResponse
        };
        var handler = new SubmitShopriteInvoiceHandler(repository, shopriteClient, archive);

        var result = await handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(SubmitShopriteInvoiceStatus.Ambiguous, result.Status);
        Assert.Equal(1, shopriteClient.SubmitCallCount);
        Assert.True(repository.ArchiveFailureWasMarkedAmbiguous);
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
        public string CandidateStatus { get; init; } = "Ready";
        public bool HasUnresolvedAmbiguousSubmission { get; init; }
        public bool HasSuccessfulSubmission { get; init; }
        public ConcurrentBag<RecordedAttempt> Attempts { get; } = [];
        public bool PreparedPayloadsRecordedBeforeStart { get; private set; }
        public bool StartWasCalled { get; private set; }
        public bool ArchiveFailureWasMarkedAmbiguous { get; private set; }
        public bool SimulateArchiveRace { get; init; }
        public bool RejectStart { get; init; }
        public bool PendingOperationWasCancelled { get; private set; }

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
                    "source-version",
                    CandidateStatus));

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
                    request.FrozenSourceJson,
                    request.FrozenCanonicalJson,
                    request.RequestPayload,
                    "payload-hash",
                    DateTimeOffset.UtcNow,
                    []);
                _operations.Add(request.CommandId, (request, operation));
                return Task.FromResult(operation);
            }
        }

        public Task RecordPreparedPayloadArchivesAsync(
            Guid submissionOperationId,
            IReadOnlyCollection<PayloadArchiveRecord> payloads,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var item = _operations.Single(pair => pair.Value.Operation.Id == submissionOperationId);
                if (SimulateArchiveRace)
                {
                    _operations[item.Key] = (
                        item.Value.Request,
                        item.Value.Operation with { State = SubmissionOperationState.Sending });
                    throw new InvalidOperationException("The winning command already started sending.");
                }

                _operations[item.Key] = (
                    item.Value.Request,
                    item.Value.Operation with { PayloadArchives = payloads.ToArray() });
                PreparedPayloadsRecordedBeforeStart = true;
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryStartSubmissionOperationAsync(
            Guid submissionOperationId,
            DateTimeOffset startedAt,
            int? expectedAutomationPolicyVersion,
            bool automatic,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                StartWasCalled = true;
                if (RejectStart)
                {
                    return Task.FromResult(false);
                }
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

        public Task CancelPendingSubmissionOperationAsync(
            Guid submissionOperationId,
            string reason,
            DateTimeOffset cancelledAt,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var item = _operations.Single(pair => pair.Value.Operation.Id == submissionOperationId);
                if (item.Value.Operation.State == SubmissionOperationState.Pending)
                {
                    _operations[item.Key] = (
                        item.Value.Request,
                        item.Value.Operation with { State = SubmissionOperationState.Cancelled });
                    PendingOperationWasCancelled = true;
                }
            }
            return Task.CompletedTask;
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
            PayloadArchiveRecord responsePayload,
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

        public Task MarkSubmissionOperationArchiveFailureAmbiguousAsync(
            Guid submissionOperationId,
            DateTimeOffset detectedAt,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var item = _operations.Single(pair => pair.Value.Operation.Id == submissionOperationId);
                _operations[item.Key] = (
                    item.Value.Request,
                    item.Value.Operation with { State = SubmissionOperationState.Ambiguous });
                ArchiveFailureWasMarkedAmbiguous = true;
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

    private sealed class FakePayloadArchive : IPayloadArchive
    {
        private readonly ConcurrentDictionary<string, (string Content, PayloadArchiveRecord Record)> _payloads = [];

        public Exception? WriteException { get; init; }
        public PayloadArchiveKind? FailingKind { get; init; }
        public ConcurrentQueue<PayloadArchiveWrite> Writes { get; } = [];

        public Task<PayloadArchiveRecord> WriteAsync(
            PayloadArchiveWrite payload,
            CancellationToken cancellationToken)
        {
            if (WriteException is not null || payload.Kind == FailingKind)
            {
                throw WriteException ?? new IOException("archive unavailable");
            }

            Writes.Enqueue(payload);
            var record = new PayloadArchiveRecord(
                payload.Kind,
                $"payloads/{payload.Path}",
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(payload.Content))).ToLowerInvariant(),
                payload.ContentType,
                System.Text.Encoding.UTF8.GetByteCount(payload.Content),
                DateTimeOffset.UtcNow);
            _payloads[payload.Path] = (payload.Content, record);
            return Task.FromResult(record);
        }

        public Task<string> ReadVerifiedAsync(
            PayloadArchiveRecord payload,
            CancellationToken cancellationToken)
        {
            var stored = _payloads.Values.Single(item => item.Record.Location == payload.Location);
            return Task.FromResult(stored.Content);
        }
    }

    private sealed class FakeSourceVersionVerifier(InvoiceSourceVersionVerification result)
        : IInvoiceSourceVersionVerifier
    {
        public int CallCount { get; private set; }

        public Task<InvoiceSourceVersionVerification> VerifyAsync(
            string? sourceJson,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAutomationGate(params AutomationSubmissionPermission[] permissions)
        : IAutomationSubmissionGate
    {
        private readonly Queue<AutomationSubmissionPermission> _permissions = new(permissions);
        public int CallCount { get; private set; }

        public Task<AutomationSubmissionPermission> EvaluateSubmissionAsync(
            Guid invoiceCandidateId,
            string initiationMode,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var permission = _permissions.Count > 1 ? _permissions.Dequeue() : _permissions.Peek();
            return Task.FromResult(permission);
        }

    }

    private sealed record RecordedAttempt(
        Guid InvoiceCandidateId,
        string InitiatedBy,
        string InitiationMode,
        string Xml,
        ShopriteInvoiceResponse Response);
}
