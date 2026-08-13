using Pvm.Application.Shoprite;
using Pvm.Application.Automation;

namespace Pvm.Application.Submissions;

public sealed class SubmitShopriteInvoiceHandler(
    IInvoiceCandidateRepository repository,
    IShopriteInvoiceClient shopriteClient,
    IPayloadArchive payloadArchive,
    IInvoiceSourceVersionVerifier? sourceVersionVerifier = null,
    IAutomationSubmissionGate? automationGate = null)
{
    private static readonly TimeSpan SendingRecoveryThreshold = TimeSpan.FromMinutes(15);

    public async Task<SubmitShopriteInvoiceResult> HandleAsync(
        SubmitShopriteInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await repository.MarkStaleSendingOperationsAmbiguousAsync(
            now - SendingRecoveryThreshold,
            now,
            cancellationToken);

        var snapshot = await repository.GetSubmissionSnapshotAsync(command.InvoiceCandidateId, cancellationToken);
        if (snapshot is null)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.Failed, "Invoice candidate not found.");
        }

        if (!snapshot.Validation.CanSubmit)
        {
            return new SubmitShopriteInvoiceResult(SubmitShopriteInvoiceStatus.ValidationBlocked, "Invoice has blocking validation issues.");
        }

        if (!snapshot.HasMatchedPurchaseOrder)
        {
            return new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.ValidationBlocked,
                "Invoice must match one loaded Shoprite PO before submission.");
        }

        AutomationSubmissionPermission? policyPermission = null;
        if (automationGate is not null)
        {
            policyPermission = await automationGate.EvaluateSubmissionAsync(
                command.InvoiceCandidateId,
                command.InitiationMode,
                now,
                cancellationToken);
            if (!policyPermission.Allowed)
            {
                return new SubmitShopriteInvoiceResult(
                    SubmitShopriteInvoiceStatus.PolicyBlocked,
                    policyPermission.Message);
            }
        }

        if (string.Equals(command.InitiationMode, "automatic", StringComparison.OrdinalIgnoreCase)
            && sourceVersionVerifier is not null)
        {
            var verification = await sourceVersionVerifier.VerifyAsync(
                snapshot.SourceJson,
                cancellationToken);
            if (!verification.IsCurrent)
            {
                return new SubmitShopriteInvoiceResult(
                    SubmitShopriteInvoiceStatus.ManualReviewRequired,
                    verification.Message);
            }
        }

        var xml = ShopriteInvoiceXmlGenerator.Generate(snapshot.Invoice);
        var operation = await repository.GetOrCreateSubmissionOperationAsync(
            new PrepareSubmissionOperation(
                snapshot.InvoiceCandidateId,
                command.CommandId,
                command.InitiatedBy,
                command.InitiationMode,
                snapshot.IdempotencyKey,
                snapshot.SourceVersion,
                snapshot.SourceJson,
                snapshot.CanonicalJson,
                xml),
            cancellationToken);

        if (operation.State != SubmissionOperationState.Pending)
        {
            return ExistingOperationResult(operation);
        }

        string requestPayload;
        try
        {
            requestPayload = await EnsurePreparedPayloadsArchivedAsync(operation, payloadArchive, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            var concurrentResult = await ResolveConcurrentArchiveResultAsync(
                operation.Id,
                cancellationToken);
            if (concurrentResult is not null)
            {
                return concurrentResult;
            }

            return new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Failed,
                "Submission evidence could not be archived. Nothing was sent to Shoprite.",
                operation.Id);
        }

        var started = await repository.TryStartSubmissionOperationAsync(
            operation.Id,
            DateTimeOffset.UtcNow,
            policyPermission?.PolicyVersion,
            string.Equals(command.InitiationMode, "automatic", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
        if (!started)
        {
            if (automationGate is not null && policyPermission is not null)
            {
                var currentPermission = await automationGate.EvaluateSubmissionAsync(
                    command.InvoiceCandidateId,
                    command.InitiationMode,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (!currentPermission.Allowed || currentPermission.PolicyVersion != policyPermission.PolicyVersion)
                {
                    await repository.CancelPendingSubmissionOperationAsync(
                        operation.Id,
                        "Automation policy changed before the external-send claim.",
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                    return new SubmitShopriteInvoiceResult(
                        SubmitShopriteInvoiceStatus.PolicyBlocked,
                        currentPermission.Allowed
                            ? "Automation policy changed before submission. The candidate must be evaluated again."
                            : currentPermission.Message,
                        operation.Id);
                }
            }
            var current = await repository.GetSubmissionOperationAsync(operation.Id, cancellationToken);
            return current is null
                ? new SubmitShopriteInvoiceResult(
                    SubmitShopriteInvoiceStatus.Failed,
                    "Submission operation disappeared before it could be started.",
                    operation.Id)
                : ExistingOperationResult(current);
        }

        ShopriteInvoiceResponse response;
        try
        {
            response = await shopriteClient.SubmitAsync(requestPayload, cancellationToken);
        }
        catch (Exception)
        {
            response = new ShopriteInvoiceResponse(
                Success: false,
                StatusCode: null,
                Body: "Submission client failed after the send boundary.",
                IsAmbiguous: true);
        }

        PayloadArchiveRecord responsePayload;
        try
        {
            responsePayload = await payloadArchive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.ShopriteResponse,
                    OperationPath(operation, "response.txt"),
                    "text/plain; charset=utf-8",
                    response.Body),
                CancellationToken.None);
        }
        catch (Exception)
        {
            await repository.MarkSubmissionOperationArchiveFailureAmbiguousAsync(
                operation.Id,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            return new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Ambiguous,
                "Shoprite responded, but response evidence could not be archived. Manual review is required.",
                operation.Id);
        }

        await repository.CompleteSubmissionOperationAsync(
            operation.Id,
            response,
            responsePayload,
            CancellationToken.None);

        if (response.IsAmbiguous)
        {
            return new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Ambiguous,
                "Submission outcome is ambiguous and requires manual review.",
                operation.Id);
        }

        return response.Success
            ? new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Submitted,
                "Invoice submitted to Shoprite.",
                operation.Id)
            : new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Failed,
                "Shoprite rejected or failed the submission.",
                operation.Id);
    }

    private static SubmitShopriteInvoiceResult ExistingOperationResult(SubmissionOperation operation)
        => operation.State switch
        {
            SubmissionOperationState.Pending or SubmissionOperationState.Sending => new(
                SubmitShopriteInvoiceStatus.InProgress,
                "Invoice submission is already in progress.",
                operation.Id),
            SubmissionOperationState.Submitted => new(
                SubmitShopriteInvoiceStatus.DuplicateBlocked,
                "Invoice already has a successful submission.",
                operation.Id),
            SubmissionOperationState.Ambiguous => new(
                SubmitShopriteInvoiceStatus.ManualReviewRequired,
                "Invoice has an unresolved ambiguous submission operation.",
                operation.Id),
            SubmissionOperationState.Rejected => new(
                SubmitShopriteInvoiceStatus.Failed,
                "This submission command was already rejected by Shoprite.",
                operation.Id),
            SubmissionOperationState.Cancelled => new(
                SubmitShopriteInvoiceStatus.PolicyBlocked,
                "This submission command was cancelled by an automation policy change.",
                operation.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private async Task<SubmitShopriteInvoiceResult?> ResolveConcurrentArchiveResultAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        SubmissionOperation? current;
        try
        {
            current = await repository.GetSubmissionOperationAsync(operationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        var requestWasArchived = current?.PayloadArchives.Any(
            payload => payload.Kind == PayloadArchiveKind.ShopriteRequest) == true;
        return current is not null
            && (current.State != SubmissionOperationState.Pending || requestWasArchived)
                ? ExistingOperationResult(current)
                : null;
    }

    private async Task<string> EnsurePreparedPayloadsArchivedAsync(
        SubmissionOperation operation,
        IPayloadArchive archive,
        CancellationToken cancellationToken)
    {
        var existingRequest = operation.PayloadArchives
            .SingleOrDefault(payload => payload.Kind == PayloadArchiveKind.ShopriteRequest);
        if (existingRequest is not null)
        {
            return await archive.ReadVerifiedAsync(existingRequest, cancellationToken);
        }

        if (operation.RequestPayload is null || operation.FrozenCanonicalJson is null)
        {
            throw new InvalidOperationException("The frozen submission payload is incomplete.");
        }

        var payloads = new List<PayloadArchiveRecord>(3);
        if (operation.FrozenSourceJson is not null)
        {
            payloads.Add(await archive.WriteAsync(
                new PayloadArchiveWrite(
                    PayloadArchiveKind.AcumaticaSource,
                    SourcePath(operation, "source.json"),
                    "application/json",
                    operation.FrozenSourceJson),
                cancellationToken));
        }

        payloads.Add(await archive.WriteAsync(
            new PayloadArchiveWrite(
                PayloadArchiveKind.CanonicalInvoice,
                SourcePath(operation, "canonical.json"),
                "application/json",
                operation.FrozenCanonicalJson),
            cancellationToken));
        payloads.Add(await archive.WriteAsync(
            new PayloadArchiveWrite(
                PayloadArchiveKind.ShopriteRequest,
                OperationPath(operation, "request.xml"),
                "application/xml",
                operation.RequestPayload),
            cancellationToken));

        await repository.RecordPreparedPayloadArchivesAsync(operation.Id, payloads, cancellationToken);
        return operation.RequestPayload;
    }

    private static string SourcePath(SubmissionOperation operation, string fileName)
        => $"acumatica/invoices/{operation.CreatedAt:yyyy/MM}/{operation.InvoiceCandidateId:D}/{operation.SourceVersion}/{fileName}";

    private static string OperationPath(SubmissionOperation operation, string fileName)
        => $"shoprite/invoices/{operation.CreatedAt:yyyy/MM}/{operation.Id:D}/{fileName}";
}
