using Pvm.Application.Shoprite;

namespace Pvm.Application.Submissions;

public sealed class SubmitShopriteInvoiceHandler(
    IInvoiceCandidateRepository repository,
    IShopriteInvoiceClient shopriteClient,
    IPayloadArchive payloadArchive)
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
            return new SubmitShopriteInvoiceResult(
                SubmitShopriteInvoiceStatus.Failed,
                "Submission evidence could not be archived. Nothing was sent to Shoprite.",
                operation.Id);
        }

        var started = await repository.TryStartSubmissionOperationAsync(
            operation.Id,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!started)
        {
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
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

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
