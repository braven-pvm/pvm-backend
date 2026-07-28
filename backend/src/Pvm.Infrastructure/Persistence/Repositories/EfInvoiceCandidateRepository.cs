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
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SubmissionOperations.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToOperation(entity);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(entity).State = EntityState.Detached;
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
        var updated = await dbContext.SubmissionOperations
            .Where(operation => operation.Id == submissionOperationId && operation.Status == "Pending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, "Sending")
                    .SetProperty(operation => operation.SendingStartedAt, startedAt),
                cancellationToken);
        return updated == 1;
    }

    public async Task<SubmissionOperation?> GetSubmissionOperationAsync(
        Guid submissionOperationId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.Id == submissionOperationId, cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    public async Task CompleteSubmissionOperationAsync(
        Guid submissionOperationId,
        ShopriteInvoiceResponse response,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.SubmissionOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken);
        if (operation is null)
        {
            throw new InvalidOperationException($"Submission operation '{submissionOperationId}' was not found.");
        }

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
                    .SetProperty(item => item.ResponsePayload, response.Body)
                    .SetProperty(item => item.ResponsePayloadHash, Sha256(response.Body))
                    .SetProperty(item => item.ErrorMessage, response.Success ? null : response.Body)
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

        dbContext.InvoiceSubmissionAttempts.Add(new InvoiceSubmissionAttemptEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = operation.InvoiceCandidateId,
            SubmissionOperationId = operation.Id,
            InitiatedBy = operation.InitiatedBy,
            InitiationMode = operation.InitiationMode,
            Status = status,
            RequestPayload = operation.RequestPayload,
            RequestPayloadHash = operation.RequestPayloadHash,
            ResponseStatusCode = response.StatusCode,
            ResponsePayload = response.Body,
            ResponsePayloadHash = Sha256(response.Body),
            ErrorMessage = response.Success ? null : response.Body,
            FailureClassification = response.IsAmbiguous ? "ambiguous-outcome" : response.Success ? null : "shoprite-rejected",
            RecommendedFixLocation = response.Success ? null : "Shoprite",
            IsRetryEligible = !response.Success && !response.IsAmbiguous,
            ResponsibleRole = response.Success ? null : "admin",
            CreatedAt = completedAt
        });

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
                returning "InvoiceCandidateId"
            )
            update invoice_candidates
            set "Status" = 'Ambiguous',
                "UpdatedAt" = {detectedAt}
            where "Id" in (
                select "InvoiceCandidateId" from stale_operations
            );
            """,
            cancellationToken);

    private async Task<SubmissionOperation?> FindByCommandIdAsync(
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.CommandId == commandId, cancellationToken);
        return entity is null ? null : ToOperation(entity);
    }

    private async Task<SubmissionOperation?> FindActiveAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubmissionOperations
            .AsNoTracking()
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
            entity.RequestPayload,
            entity.RequestPayloadHash);

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
