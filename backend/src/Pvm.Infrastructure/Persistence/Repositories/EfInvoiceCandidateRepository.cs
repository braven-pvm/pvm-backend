using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence.Repositories;

public sealed class EfInvoiceCandidateRepository(PvmDbContext dbContext) : IInvoiceCandidateRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveOperationStates =
    [
        "Pending",
        "Sending",
        "Submitted",
        "Ambiguous"
    ];

    public async Task<InvoiceSubmissionSnapshot?> GetSubmissionSnapshotAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var candidate = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .Where(candidate => candidate.Id == invoiceCandidateId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.IdempotencyKey,
                candidate.SourceJson,
                candidate.CanonicalJson,
                candidate.ValidationJson,
                candidate.MatchedShopritePurchaseOrderId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null || string.IsNullOrWhiteSpace(candidate.CanonicalJson))
        {
            return null;
        }

        var invoice = JsonSerializer.Deserialize<CanonicalInvoice>(candidate.CanonicalJson, SerializerOptions);
        if (invoice is null)
        {
            return null;
        }

        var validation = string.IsNullOrWhiteSpace(candidate.ValidationJson)
            ? new ValidationResult([])
            : JsonSerializer.Deserialize<ValidationResult>(candidate.ValidationJson, SerializerOptions)
                ?? new ValidationResult([]);
        var sourceVersion = Sha256(string.Join(
            "\n",
            candidate.SourceJson ?? string.Empty,
            candidate.CanonicalJson,
            candidate.ValidationJson ?? string.Empty,
            candidate.MatchedShopritePurchaseOrderId?.ToString() ?? string.Empty));

        return new InvoiceSubmissionSnapshot(
            candidate.Id,
            invoice,
            validation,
            candidate.MatchedShopritePurchaseOrderId is not null,
            candidate.IdempotencyKey,
            candidate.SourceJson,
            candidate.CanonicalJson,
            sourceVersion);
    }

    public async Task<SubmissionOperation> GetOrCreateSubmissionOperationAsync(
        PrepareSubmissionOperation request,
        CancellationToken cancellationToken)
    {
        var existingCommand = await FindByCommandIdAsync(request.CommandId, cancellationToken);
        if (existingCommand is not null)
        {
            return existingCommand;
        }

        var active = await FindActiveAsync(request.InvoiceCandidateId, cancellationToken);
        if (active is not null)
        {
            return active;
        }

        var rejected = await FindLatestRejectedAsync(request.InvoiceCandidateId, cancellationToken);
        if (rejected is not null)
        {
            return rejected;
        }

        var generation = (await dbContext.SubmissionOperations
            .Where(operation => operation.InvoiceCandidateId == request.InvoiceCandidateId)
            .MaxAsync(operation => (int?)operation.Generation, cancellationToken) ?? 0) + 1;
        var createdAt = DateTimeOffset.UtcNow;
        var entity = new SubmissionOperationEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = request.InvoiceCandidateId,
            CommandId = request.CommandId,
            CorrelationId = Guid.NewGuid(),
            Generation = generation,
            IdempotencyKey = request.IdempotencyKey,
            Status = "Pending",
            InitiatedBy = request.InitiatedBy,
            InitiationMode = request.InitiationMode,
            SourceVersion = request.SourceVersion,
            FrozenSourceJson = request.FrozenSourceJson,
            FrozenCanonicalJson = request.FrozenCanonicalJson,
            RequestPayload = request.RequestPayload,
            RequestPayloadHash = Sha256(request.RequestPayload),
            CreatedAt = createdAt
        };

        dbContext.SubmissionOperations.Add(entity);
        dbContext.SubmissionOperationTransitions.Add(NewTransition(
            entity,
            previousState: null,
            newState: "Pending",
            reason: "Submission operation prepared.",
            createdAt));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToOperation(entity);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindByCommandIdAsync(request.CommandId, cancellationToken)
                ?? await FindActiveAsync(request.InvoiceCandidateId, cancellationToken)
                ?? await FindLatestRejectedAsync(request.InvoiceCandidateId, cancellationToken);
            return winner ?? throw new InvalidOperationException(
                "A submission-operation uniqueness conflict occurred without a persisted winner.",
                exception);
        }
    }

    public async Task<bool> TryStartSubmissionOperationAsync(
        Guid submissionOperationId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operation = await dbContext.SubmissionOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken);
        if (operation is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var requestArchived = await dbContext.PayloadArchives
            .AnyAsync(
                payload => payload.SubmissionOperationId == submissionOperationId
                    && payload.Kind == PayloadArchiveKind.ShopriteRequest.ToString(),
                cancellationToken);
        if (!requestArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var updated = await dbContext.SubmissionOperations
            .Where(operation => operation.Id == submissionOperationId && operation.Status == "Pending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, "Sending")
                    .SetProperty(operation => operation.SendingStartedAt, startedAt),
                cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.SubmissionOperationTransitions.Add(NewTransition(
            operation,
            previousState: "Pending",
            newState: "Sending",
            reason: "Submission operation claimed for external send.",
            startedAt));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<SubmissionOperation?> GetSubmissionOperationAsync(
        Guid submissionOperationId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(operation => operation.PayloadArchives)
            .SingleOrDefaultAsync(operation => operation.Id == submissionOperationId, cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    public async Task RecordPreparedPayloadArchivesAsync(
        Guid submissionOperationId,
        IReadOnlyCollection<PayloadArchiveRecord> payloads,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(item => item.PayloadArchives)
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Submission operation '{submissionOperationId}' was not found.");

        if (operation.Status != "Pending")
        {
            throw new InvalidOperationException(
                "Prepared payload metadata can only be recorded for a pending operation.");
        }

        var records = payloads.ToDictionary(payload => payload.Kind);
        if (records.Keys.Any(kind => kind is not (
                PayloadArchiveKind.AcumaticaSource
                or PayloadArchiveKind.CanonicalInvoice
                or PayloadArchiveKind.ShopriteRequest)))
        {
            throw new InvalidOperationException(
                "Only source, canonical invoice, and request payloads can be recorded before sending.");
        }

        if (!records.TryGetValue(PayloadArchiveKind.CanonicalInvoice, out _)
            || !records.TryGetValue(PayloadArchiveKind.ShopriteRequest, out var requestPayload))
        {
            throw new InvalidOperationException(
                "Canonical invoice and Shoprite request archive records are required.");
        }

        var existingRecords = operation.PayloadArchives.ToDictionary(
            payload => Enum.Parse<PayloadArchiveKind>(payload.Kind));
        foreach (var existing in existingRecords)
        {
            if (records.TryGetValue(existing.Key, out var incoming)
                && (!string.Equals(existing.Value.Location, incoming.Location, StringComparison.Ordinal)
                    || !string.Equals(
                        existing.Value.Sha256Hash,
                        incoming.Sha256Hash,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        existing.Value.ContentType,
                        incoming.ContentType,
                        StringComparison.OrdinalIgnoreCase)
                    || existing.Value.ByteCount != incoming.ByteCount))
            {
                throw new InvalidOperationException(
                    "Persisted payload archive metadata conflicts with the immutable archive.");
            }
        }

        if (operation.FrozenSourceJson is not null
            && !records.ContainsKey(PayloadArchiveKind.AcumaticaSource))
        {
            throw new InvalidOperationException(
                "The frozen Acumatica source archive record is required.");
        }

        if (operation.RequestPayload is not null)
        {
            EnsureArchiveMatches(
                requestPayload,
                operation.RequestPayload,
                "Shoprite request");
        }
        else if (!string.Equals(
                     requestPayload.Sha256Hash,
                     operation.RequestPayloadHash,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The archived request hash does not match the frozen operation payload.");
        }

        if (operation.FrozenCanonicalJson is not null)
        {
            EnsureArchiveMatches(
                records[PayloadArchiveKind.CanonicalInvoice],
                operation.FrozenCanonicalJson,
                "canonical invoice");
        }
        else if (!existingRecords.ContainsKey(PayloadArchiveKind.CanonicalInvoice))
        {
            throw new InvalidOperationException(
                "Frozen canonical invoice content and archive metadata are missing.");
        }

        if (operation.FrozenSourceJson is not null)
        {
            EnsureArchiveMatches(
                records[PayloadArchiveKind.AcumaticaSource],
                operation.FrozenSourceJson,
                "Acumatica source");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var payload in records.Values)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into payload_archives
                    ("Id", "SubmissionOperationId", "InvoiceCandidateId", "Kind",
                     "Location", "Sha256Hash", "ContentType", "ByteCount", "CreatedAt")
                values
                    ({Guid.NewGuid()}, {operation.Id}, {operation.InvoiceCandidateId}, {payload.Kind.ToString()},
                     {payload.Location}, {payload.Sha256Hash}, {payload.ContentType},
                     {payload.ByteCount}, {payload.CreatedAt})
                on conflict ("SubmissionOperationId", "Kind") do nothing;
                """,
                cancellationToken);
        }

        await dbContext.SubmissionOperations
            .Where(item => item.Id == submissionOperationId && item.Status == "Pending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.FrozenSourceJson, (string?)null)
                    .SetProperty(item => item.FrozenCanonicalJson, (string?)null)
                    .SetProperty(item => item.RequestPayload, (string?)null),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteSubmissionOperationAsync(
        Guid submissionOperationId,
        ShopriteInvoiceResponse response,
        PayloadArchiveRecord responsePayload,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(item => item.PayloadArchives)
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken);
        if (operation is null)
        {
            throw new InvalidOperationException($"Submission operation '{submissionOperationId}' was not found.");
        }

        if (responsePayload.Kind != PayloadArchiveKind.ShopriteResponse
            || !string.Equals(
                responsePayload.Sha256Hash,
                Sha256(response.Body),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The archived Shoprite response does not match the completion response.");
        }

        var requestPayload = operation.PayloadArchives
            .SingleOrDefault(payload => payload.Kind == PayloadArchiveKind.ShopriteRequest.ToString())
            ?? throw new InvalidOperationException(
                "The Shoprite request archive metadata is missing.");
        var status = response.IsAmbiguous
            ? "Ambiguous"
            : response.Success ? "Submitted" : "Rejected";
        var completedAt = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.SubmissionOperations
            .Where(item => item.Id == submissionOperationId && item.Status == "Sending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, status)
                    .SetProperty(item => item.ResponseStatusCode, response.StatusCode)
                    .SetProperty(item => item.ResponsePayload, (string?)null)
                    .SetProperty(item => item.ResponsePayloadHash, responsePayload.Sha256Hash)
                    .SetProperty(
                        item => item.ErrorMessage,
                        response.Success
                            ? null
                            : response.IsAmbiguous
                                ? "Submission outcome is ambiguous."
                                : "Shoprite rejected the submission.")
                    .SetProperty(
                        item => item.FailureClassification,
                        response.IsAmbiguous ? "ambiguous-outcome" : response.Success ? null : "shoprite-rejected")
                    .SetProperty(item => item.CompletedAt, completedAt),
                cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        dbContext.PayloadArchives.Add(ToEntity(
            responsePayload,
            operation.Id,
            operation.InvoiceCandidateId));
        dbContext.InvoiceSubmissionAttempts.Add(new InvoiceSubmissionAttemptEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = operation.InvoiceCandidateId,
            SubmissionOperationId = operation.Id,
            InitiatedBy = operation.InitiatedBy,
            InitiationMode = operation.InitiationMode,
            Status = status,
            RequestPayload = null,
            RequestPayloadLocation = requestPayload.Location,
            RequestPayloadHash = requestPayload.Sha256Hash,
            ResponseStatusCode = response.StatusCode,
            ResponsePayload = null,
            ResponsePayloadLocation = responsePayload.Location,
            ResponsePayloadHash = responsePayload.Sha256Hash,
            ErrorMessage = response.Success
                ? null
                : response.IsAmbiguous
                    ? "Submission outcome is ambiguous."
                    : "Shoprite rejected the submission.",
            FailureClassification = response.IsAmbiguous ? "ambiguous-outcome" : response.Success ? null : "shoprite-rejected",
            RecommendedFixLocation = response.Success ? null : "Shoprite",
            IsRetryEligible = !response.Success && !response.IsAmbiguous,
            ResponsibleRole = response.Success ? null : "admin",
            CreatedAt = completedAt
        });
        dbContext.SubmissionOperationTransitions.Add(NewTransition(
            operation,
            previousState: "Sending",
            newState: status,
            reason: response.IsAmbiguous
                ? "External submission outcome is ambiguous."
                : response.Success
                    ? "Shoprite accepted the invoice."
                    : "Shoprite rejected the invoice.",
            completedAt));

        await dbContext.InvoiceCandidates
            .Where(candidate => candidate.Id == operation.InvoiceCandidateId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, status)
                    .SetProperty(candidate => candidate.UpdatedAt, completedAt),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkSubmissionOperationArchiveFailureAmbiguousAsync(
        Guid submissionOperationId,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(item => item.PayloadArchives)
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Submission operation '{submissionOperationId}' was not found.");
        var requestPayload = operation.PayloadArchives
            .SingleOrDefault(payload => payload.Kind == PayloadArchiveKind.ShopriteRequest.ToString());

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.SubmissionOperations
            .Where(item => item.Id == submissionOperationId && item.Status == "Sending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, "Ambiguous")
                    .SetProperty(item => item.CompletedAt, detectedAt)
                    .SetProperty(item => item.ErrorMessage, "Shoprite response evidence could not be archived.")
                    .SetProperty(item => item.FailureClassification, "response-archive-failed"),
                cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        dbContext.InvoiceSubmissionAttempts.Add(new InvoiceSubmissionAttemptEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = operation.InvoiceCandidateId,
            SubmissionOperationId = operation.Id,
            InitiatedBy = operation.InitiatedBy,
            InitiationMode = operation.InitiationMode,
            Status = "Ambiguous",
            RequestPayloadLocation = requestPayload?.Location,
            RequestPayloadHash = operation.RequestPayloadHash,
            ErrorMessage = "Shoprite response evidence could not be archived.",
            FailureClassification = "response-archive-failed",
            RecommendedFixLocation = "PayloadArchive",
            IsRetryEligible = false,
            ResponsibleRole = "admin",
            CreatedAt = detectedAt
        });
        dbContext.SubmissionOperationTransitions.Add(NewTransition(
            operation,
            previousState: "Sending",
            newState: "Ambiguous",
            reason: "Shoprite response evidence could not be archived.",
            detectedAt));
        await dbContext.InvoiceCandidates
            .Where(candidate => candidate.Id == operation.InvoiceCandidateId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, "Ambiguous")
                    .SetProperty(candidate => candidate.UpdatedAt, detectedAt),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> MarkStaleSendingOperationsAmbiguousAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken)
        => await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            with stale_operations as (
                update submission_operations
                set "Status" = 'Ambiguous',
                    "CompletedAt" = {detectedAt},
                    "ErrorMessage" = 'Sending operation exceeded its recovery threshold.',
                    "FailureClassification" = 'stale-sending'
                where "Status" = 'Sending'
                    and "SendingStartedAt" is not null
                    and "SendingStartedAt" < {staleBefore}
                returning "Id", "InvoiceCandidateId", "CorrelationId", "InitiatedBy",
                    "InitiationMode", "SourceVersion", "RequestPayloadHash"
            ),
            transition_events as (
                insert into submission_operation_transitions
                    ("Id", "SubmissionOperationId", "InvoiceCandidateId", "CorrelationId",
                     "Actor", "Mode", "PreviousState", "NewState", "Reason",
                     "SourceVersion", "PayloadHash", "CreatedAt")
                select gen_random_uuid(), "Id", "InvoiceCandidateId", "CorrelationId",
                    "InitiatedBy", "InitiationMode", 'Sending', 'Ambiguous',
                    'Sending operation exceeded its recovery threshold.',
                    "SourceVersion", "RequestPayloadHash", {detectedAt}
                from stale_operations
                returning "InvoiceCandidateId"
            )
            update invoice_candidates
            set "Status" = 'Ambiguous',
                "UpdatedAt" = {detectedAt}
            where "Id" in (
                select "InvoiceCandidateId" from transition_events
            );
            """,
            cancellationToken);

    private async Task<SubmissionOperation?> FindByCommandIdAsync(
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(operation => operation.PayloadArchives)
            .SingleOrDefaultAsync(operation => operation.CommandId == commandId, cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    private async Task<SubmissionOperation?> FindActiveAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(operation => operation.PayloadArchives)
            .SingleOrDefaultAsync(
                operation => operation.InvoiceCandidateId == invoiceCandidateId
                    && ActiveOperationStates.Contains(operation.Status),
                cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    private async Task<SubmissionOperation?> FindLatestRejectedAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Include(operation => operation.PayloadArchives)
            .Where(operation => operation.InvoiceCandidateId == invoiceCandidateId
                && operation.Status == "Rejected")
            .OrderByDescending(operation => operation.Generation)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    private static SubmissionOperation ToOperation(SubmissionOperationEntity entity)
        => new(
            entity.Id,
            entity.InvoiceCandidateId,
            entity.CommandId,
            entity.Generation,
            Enum.Parse<SubmissionOperationState>(entity.Status),
            entity.SourceVersion,
            entity.FrozenSourceJson,
            entity.FrozenCanonicalJson,
            entity.RequestPayload,
            entity.RequestPayloadHash,
            entity.CreatedAt,
            entity.PayloadArchives
                .OrderBy(payload => payload.Kind)
                .Select(ToRecord)
                .ToArray());

    private static PayloadArchiveRecord ToRecord(PayloadArchiveEntity entity)
        => new(
            Enum.Parse<PayloadArchiveKind>(entity.Kind),
            entity.Location,
            entity.Sha256Hash,
            entity.ContentType,
            entity.ByteCount,
            entity.CreatedAt);

    private static PayloadArchiveEntity ToEntity(
        PayloadArchiveRecord payload,
        Guid submissionOperationId,
        Guid invoiceCandidateId)
        => new()
        {
            Id = Guid.NewGuid(),
            SubmissionOperationId = submissionOperationId,
            InvoiceCandidateId = invoiceCandidateId,
            Kind = payload.Kind.ToString(),
            Location = payload.Location,
            Sha256Hash = payload.Sha256Hash,
            ContentType = payload.ContentType,
            ByteCount = payload.ByteCount,
            CreatedAt = payload.CreatedAt
        };

    private static SubmissionOperationTransitionEntity NewTransition(
        SubmissionOperationEntity operation,
        string? previousState,
        string newState,
        string reason,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            SubmissionOperationId = operation.Id,
            InvoiceCandidateId = operation.InvoiceCandidateId,
            CorrelationId = operation.CorrelationId,
            Actor = operation.InitiatedBy,
            Mode = operation.InitiationMode,
            PreviousState = previousState,
            NewState = newState,
            Reason = reason,
            SourceVersion = operation.SourceVersion,
            PayloadHash = operation.RequestPayloadHash,
            CreatedAt = createdAt
        };

    private static void EnsureArchiveMatches(
        PayloadArchiveRecord payload,
        string expectedContent,
        string description)
    {
        var expectedBytes = Encoding.UTF8.GetByteCount(expectedContent);
        if (!string.Equals(
                payload.Sha256Hash,
                Sha256(expectedContent),
                StringComparison.OrdinalIgnoreCase)
            || payload.ByteCount != expectedBytes)
        {
            throw new InvalidOperationException(
                $"The archived {description} does not match the frozen operation content.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
