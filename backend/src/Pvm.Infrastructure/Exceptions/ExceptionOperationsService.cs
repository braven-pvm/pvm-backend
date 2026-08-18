using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Exceptions;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Infrastructure.Exceptions;

public sealed class ExceptionOperationsService(
    PvmDbContext dbContext,
    ShopriteInvoiceCandidateMatcher candidateMatcher)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const long SynchronizationLockKey = 5788934893304272;
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(15);
    private static readonly string[] ActiveStatuses =
    [
        ExceptionTaskStatuses.Open,
        ExceptionTaskStatuses.InProgress,
        ExceptionTaskStatuses.WaitingForAcumatica,
        ExceptionTaskStatuses.WaitingForShoprite
    ];

    public async Task<int> SynchronizeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({SynchronizationLockKey})",
            cancellationToken);

        var derived = new List<DerivedException>();
        derived.AddRange(await AmbiguousOperationsAsync(cancellationToken));
        derived.AddRange(await RejectedOperationsAsync(cancellationToken));
        derived.AddRange(await StuckOperationsAsync(now, cancellationToken));
        derived.AddRange(await CandidateExceptionsAsync(cancellationToken));
        derived.AddRange(await DeadLetterExceptionsAsync(cancellationToken));
        derived.AddRange(await StuckOutboxExceptionsAsync(now, cancellationToken));

        var keys = derived.Select(item => item.Key).ToArray();
        var existing = await dbContext.ExceptionTasks
            .Where(task => keys.Contains(task.DeduplicationKey))
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(task => task.DeduplicationKey, StringComparer.Ordinal);

        foreach (var item in derived)
        {
            if (byKey.TryGetValue(item.Key, out var task))
            {
                UpdateDerivedTask(task, item, now);
                continue;
            }

            dbContext.ExceptionTasks.Add(NewDerivedTask(item, now));
        }

        var stale = await dbContext.ExceptionTasks
            .Where(task =>
                task.IsDerived
                && ActiveStatuses.Contains(task.Status)
                && !keys.Contains(task.DeduplicationKey))
            .ToListAsync(cancellationToken);
        foreach (var task in stale)
        {
            task.Status = ExceptionTaskStatuses.Resolved;
            task.ResolvedAt = now;
            task.ResolvedBy = "system:synchronization";
            task.ResolutionReason = "The underlying condition is no longer present.";
            task.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return derived.Count;
    }

    public async Task<ExceptionQueueListing> ListAsync(
        string? status,
        string? category,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ExceptionTasks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? query.Where(task => ActiveStatuses.Contains(task.Status))
                : query.Where(task => task.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(task => task.Category == category);
        }

        var tasks = await query
            .OrderBy(task => task.Status == ExceptionTaskStatuses.Resolved)
            .ThenBy(task => task.Severity == ExceptionSeverities.Critical
                ? 0
                : task.Severity == ExceptionSeverities.High
                    ? 1
                    : task.Severity == ExceptionSeverities.Medium ? 2 : 3)
            .ThenByDescending(task => task.LastSeenAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        var comments = await CommentsForAsync(tasks.Select(task => task.Id).ToArray(), cancellationToken);
        var invoiceNumbers = await InvoiceNumbersForAsync(tasks, cancellationToken);
        var views = tasks
            .Select(task => ToView(task, comments, invoiceNumbers, now))
            .ToList();

        return new ExceptionQueueListing(views, await SummaryAsync(now, cancellationToken));
    }

    public async Task<ExceptionTaskView?> GetAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = await dbContext.ExceptionTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var comments = await CommentsForAsync([task.Id], cancellationToken);
        var invoiceNumbers = await InvoiceNumbersForAsync([task], cancellationToken);
        return ToView(task, comments, invoiceNumbers, now);
    }

    public async Task<ExceptionOperationResult> AssignAsync(
        Guid taskId,
        string? owner,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.ExceptionTasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null)
        {
            return new ExceptionOperationResult(false, "Exception task not found.");
        }

        task.Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
        if (task.Owner is not null && task.Status == ExceptionTaskStatuses.Open)
        {
            task.Status = ExceptionTaskStatuses.InProgress;
        }

        task.UpdatedAt = now;
        dbContext.AuditEvents.Add(NewAudit(
            "ExceptionTask",
            task.Id.ToString(),
            "exception-task-assigned",
            actor,
            new { owner = task.Owner, status = task.Status },
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Task: await GetAsync(task.Id, now, cancellationToken));
    }

    public async Task<ExceptionOperationResult> AddCommentAsync(
        Guid taskId,
        string actor,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ExceptionOperationResult(false, "A comment body is required.");
        }

        var task = await dbContext.ExceptionTasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null)
        {
            return new ExceptionOperationResult(false, "Exception task not found.");
        }

        dbContext.ExceptionTaskComments.Add(new ExceptionTaskCommentEntity
        {
            Id = Guid.NewGuid(),
            ExceptionTaskId = task.Id,
            Actor = actor,
            Body = body.Trim(),
            CreatedAt = now
        });
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Task: await GetAsync(task.Id, now, cancellationToken));
    }

    public async Task<ExceptionOperationResult> SetStatusAsync(
        Guid taskId,
        string status,
        string actor,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!ExceptionTaskStatuses.All.Contains(status, StringComparer.Ordinal))
        {
            return new ExceptionOperationResult(false, "That exception status is not supported.");
        }

        var closing = status is ExceptionTaskStatuses.Resolved or ExceptionTaskStatuses.Suppressed;
        if (closing && string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to resolve or suppress an exception.");
        }

        var task = await dbContext.ExceptionTasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null)
        {
            return new ExceptionOperationResult(false, "Exception task not found.");
        }

        if (status == ExceptionTaskStatuses.Resolved
            && task.Category == ExceptionCategories.AmbiguousSubmission
            && task.IsDerived)
        {
            return new ExceptionOperationResult(
                false,
                "Record the ambiguous outcome with evidence before this exception can be resolved.");
        }

        task.Status = status;
        task.UpdatedAt = now;
        if (closing)
        {
            task.ResolvedAt = now;
            task.ResolvedBy = actor;
            task.ResolutionReason = reason.Trim();
        }
        else
        {
            task.ResolvedAt = null;
            task.ResolvedBy = null;
            task.ResolutionReason = null;
        }

        dbContext.AuditEvents.Add(NewAudit(
            "ExceptionTask",
            task.Id.ToString(),
            "exception-task-status-changed",
            actor,
            new { status, reason },
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Task: await GetAsync(task.Id, now, cancellationToken));
    }

    public async Task<ExceptionOperationResult> ResolveAmbiguousAsync(
        Guid submissionOperationId,
        AmbiguousResolutionOutcome outcome,
        string evidence,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return new ExceptionOperationResult(false, "Record how Shoprite was checked before you resolve this.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A resolution reason is required.");
        }

        var operation = await dbContext.SubmissionOperations
            .SingleOrDefaultAsync(item => item.Id == submissionOperationId, cancellationToken);
        if (operation is null)
        {
            return new ExceptionOperationResult(false, "Submission operation not found.");
        }

        if (operation.Status != "Ambiguous")
        {
            return new ExceptionOperationResult(
                false,
                "Only an ambiguous submission can be resolved this way.",
                Conflict: true,
                Detail: operation.Status);
        }

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(item => item.Id == operation.InvoiceCandidateId, cancellationToken);

        string newOperationState;
        string candidateStatus;
        string transitionReason;
        switch (outcome)
        {
            case AmbiguousResolutionOutcome.ConfirmedAccepted:
                newOperationState = "Submitted";
                candidateStatus = "Submitted";
                transitionReason = "Admin confirmed that Shoprite accepted the invoice.";
                break;
            case AmbiguousResolutionOutcome.ConfirmedNotAccepted:
                newOperationState = "Cancelled";
                candidateStatus = "NeedsReview";
                transitionReason = "Admin confirmed that Shoprite did not accept the invoice.";
                break;
            default:
                await RecordStillUnknownAsync(operation, evidence, reason, actor, now, cancellationToken);
                return new ExceptionOperationResult(
                    true,
                    Detail: "The submission remains ambiguous. No state was changed.");
        }

        var previousState = operation.Status;
        operation.Status = newOperationState;
        operation.CompletedAt = now;
        dbContext.SubmissionOperationTransitions.Add(new SubmissionOperationTransitionEntity
        {
            Id = Guid.NewGuid(),
            SubmissionOperationId = operation.Id,
            InvoiceCandidateId = operation.InvoiceCandidateId,
            CorrelationId = operation.CorrelationId,
            Actor = actor,
            Mode = "manual",
            PreviousState = previousState,
            NewState = newOperationState,
            Reason = transitionReason,
            SourceVersion = operation.SourceVersion,
            PayloadHash = operation.RequestPayloadHash,
            CreatedAt = now
        });

        if (candidate is not null)
        {
            candidate.Status = candidateStatus;
            candidate.UpdatedAt = now;
        }

        dbContext.AuditEvents.Add(NewAudit(
            "SubmissionOperation",
            operation.Id.ToString(),
            "ambiguous-submission-resolved",
            actor,
            new
            {
                outcome = outcome.ToString(),
                evidence,
                reason,
                previousState,
                newState = newOperationState,
                candidateStatus
            },
            now));

        await CloseDerivedTaskAsync(
            AmbiguousKey(operation.Id),
            actor,
            $"{outcome}: {reason}",
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: newOperationState);
    }

    public async Task<ExceptionOperationResult> HoldInvoiceAsync(
        Guid invoiceCandidateId,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to hold an invoice.");
        }

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(item => item.Id == invoiceCandidateId, cancellationToken);
        if (candidate is null)
        {
            return new ExceptionOperationResult(false, "Invoice candidate not found.");
        }

        if (candidate.Status is "Submitted" or "Ambiguous")
        {
            return new ExceptionOperationResult(
                false,
                "A submitted or ambiguous invoice cannot be held.",
                Conflict: true,
                Detail: candidate.Status);
        }

        if (candidate.Status == "Suspended")
        {
            return new ExceptionOperationResult(true, Detail: "Suspended");
        }

        var previousStatus = candidate.Status;
        candidate.Status = "Suspended";
        candidate.UpdatedAt = now;
        dbContext.AuditEvents.Add(NewAudit(
            "InvoiceCandidate",
            candidate.Id.ToString(),
            "invoice-hold-applied",
            actor,
            new { previousStatus, reason },
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: previousStatus);
    }

    public async Task<ExceptionOperationResult> ReleaseInvoiceAsync(
        Guid invoiceCandidateId,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to release a hold.");
        }

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(item => item.Id == invoiceCandidateId, cancellationToken);
        if (candidate is null)
        {
            return new ExceptionOperationResult(false, "Invoice candidate not found.");
        }

        if (candidate.Status != "Suspended")
        {
            return new ExceptionOperationResult(
                false,
                "That invoice is not on hold.",
                Conflict: true,
                Detail: candidate.Status);
        }

        candidate.Status = "NeedsReview";
        candidate.UpdatedAt = now;
        var revalidatedStatus = await RevalidateAsync(candidate, now, cancellationToken);
        dbContext.AuditEvents.Add(NewAudit(
            "InvoiceCandidate",
            candidate.Id.ToString(),
            "invoice-hold-released",
            actor,
            new { reason, status = revalidatedStatus },
            now));
        await CloseDerivedTaskAsync(HeldKey(candidate.Id), actor, reason, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: revalidatedStatus);
    }

    public async Task<ExceptionOperationResult> RetryRejectedAsync(
        Guid invoiceCandidateId,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to retry a rejected submission.");
        }

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(item => item.Id == invoiceCandidateId, cancellationToken);
        if (candidate is null)
        {
            return new ExceptionOperationResult(false, "Invoice candidate not found.");
        }

        var latest = await dbContext.SubmissionOperations
            .Where(operation => operation.InvoiceCandidateId == invoiceCandidateId)
            .OrderByDescending(operation => operation.Generation)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return new ExceptionOperationResult(false, "That invoice has no submission operation to retry.");
        }

        if (latest.Status is "Ambiguous" or "Sending" or "Pending")
        {
            return new ExceptionOperationResult(
                false,
                "Resolve the in-flight or ambiguous submission before you retry.",
                Conflict: true,
                Detail: latest.Status);
        }

        if (latest.Status == "Submitted")
        {
            return new ExceptionOperationResult(
                false,
                "Shoprite already accepted this invoice. It must not be sent again.",
                Conflict: true,
                Detail: latest.Status);
        }

        if (latest.Status != "Rejected")
        {
            return new ExceptionOperationResult(
                false,
                "Only a rejected submission can be retried.",
                Conflict: true,
                Detail: latest.Status);
        }

        candidate.Status = "NeedsReview";
        candidate.UpdatedAt = now;
        var revalidatedStatus = await RevalidateAsync(candidate, now, cancellationToken);
        if (revalidatedStatus != "Ready")
        {
            dbContext.AuditEvents.Add(NewAudit(
                "InvoiceCandidate",
                candidate.Id.ToString(),
                "submission-retry-refused",
                actor,
                new { reason, status = revalidatedStatus },
                now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ExceptionOperationResult(
                false,
                "The invoice still fails validation. Correct the source or the mappings first.",
                Detail: revalidatedStatus);
        }

        latest.Status = "Cancelled";
        latest.CompletedAt = now;
        dbContext.SubmissionOperationTransitions.Add(new SubmissionOperationTransitionEntity
        {
            Id = Guid.NewGuid(),
            SubmissionOperationId = latest.Id,
            InvoiceCandidateId = latest.InvoiceCandidateId,
            CorrelationId = latest.CorrelationId,
            Actor = actor,
            Mode = "manual",
            PreviousState = "Rejected",
            NewState = "Cancelled",
            Reason = "Admin retired the rejected operation after successful revalidation.",
            SourceVersion = latest.SourceVersion,
            PayloadHash = latest.RequestPayloadHash,
            CreatedAt = now
        });
        dbContext.AuditEvents.Add(NewAudit(
            "InvoiceCandidate",
            candidate.Id.ToString(),
            "submission-retry-authorized",
            actor,
            new { reason, retiredOperationId = latest.Id, generation = latest.Generation },
            now));
        await CloseDerivedTaskAsync(RejectedKey(latest.Id), actor, reason, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: revalidatedStatus);
    }

    public async Task<DeadLetterReplaySafety> EvaluateReplaySafetyAsync(
        IntegrationMessageDeliveryEntity delivery,
        CancellationToken cancellationToken)
    {
        if (delivery.Status != "DeadLettered")
        {
            return new DeadLetterReplaySafety(false, "Only a dead-lettered message can be replayed.", delivery.Status);
        }

        if (!Guid.TryParse(delivery.MessageId, out var messageId))
        {
            return new DeadLetterReplaySafety(false, "The original message identity cannot be read.");
        }

        var source = await dbContext.IntegrationOutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
        if (source is null)
        {
            return new DeadLetterReplaySafety(false, "The original message payload is no longer available.");
        }

        if (delivery.ErrorCode is "invalid-message-envelope" or "invalid-message-data" or "message-contract-mismatch")
        {
            return new DeadLetterReplaySafety(
                false,
                "The message itself is malformed. Replay would fail again.",
                delivery.ErrorCode);
        }

        if (delivery.MessageType != IntegrationMessageTypes.ShopriteInvoiceSubmitV1)
        {
            return new DeadLetterReplaySafety(true, "This command is idempotent and carries no external side effect.");
        }

        var candidateId = ReadSubmitCandidateId(source.PayloadJson);
        if (candidateId is null)
        {
            return new DeadLetterReplaySafety(false, "The submission command does not name an invoice candidate.");
        }

        var states = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Where(operation => operation.InvoiceCandidateId == candidateId)
            .Select(operation => operation.Status)
            .ToListAsync(cancellationToken);
        var blocking = states.FirstOrDefault(state => state is "Sending" or "Submitted" or "Ambiguous");
        return blocking is null
            ? new DeadLetterReplaySafety(true, "No submission has reached Shoprite for this invoice.")
            : new DeadLetterReplaySafety(
                false,
                "This invoice already has a submission that reached, or may have reached, Shoprite.",
                blocking);
    }

    public async Task<ExceptionOperationResult> ReplayDeadLetterAsync(
        Guid deliveryId,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to replay a dead-lettered message.");
        }

        var delivery = await dbContext.IntegrationMessageDeliveries
            .SingleOrDefaultAsync(item => item.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return new ExceptionOperationResult(false, "Dead-lettered message not found.");
        }

        var safety = await EvaluateReplaySafetyAsync(delivery, cancellationToken);
        if (!safety.Safe)
        {
            return new ExceptionOperationResult(false, safety.Reason, Conflict: true, Detail: safety.BlockingState);
        }

        var source = await dbContext.IntegrationOutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.Id == Guid.Parse(delivery.MessageId), cancellationToken);
        var replayId = Guid.NewGuid();
        var payload = RewriteEnvelope(source.PayloadJson, replayId, source.Id.ToString("D"), now);
        dbContext.IntegrationOutboxMessages.Add(new IntegrationOutboxMessageEntity
        {
            Id = replayId,
            QueueName = source.QueueName,
            MessageType = source.MessageType,
            PayloadJson = payload,
            CorrelationId = source.CorrelationId,
            CausationId = source.Id.ToString("D"),
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        delivery.Status = "Replayed";
        delivery.UpdatedAt = now;
        dbContext.AuditEvents.Add(NewAudit(
            "IntegrationMessageDelivery",
            delivery.Id.ToString(),
            "dead-letter-replayed",
            actor,
            new
            {
                reason,
                originalMessageId = delivery.MessageId,
                replayMessageId = replayId,
                queue = source.QueueName,
                messageType = source.MessageType,
                safety = safety.Reason
            },
            now));
        await CloseDerivedTaskAsync(DeadLetterKey(delivery.Id), actor, reason, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: replayId.ToString("D"));
    }

    public async Task<ExceptionOperationResult> ResolveDeadLettersAsync(
        string? queueName,
        int olderThanDays,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new ExceptionOperationResult(false, "A reason is required to resolve dead letters.");
        }

        if (olderThanDays < 0)
        {
            return new ExceptionOperationResult(false, "The age in days cannot be negative.");
        }

        var cutoff = now.AddDays(-olderThanDays);
        var deliveries = await dbContext.IntegrationMessageDeliveries
            .Where(delivery =>
                delivery.Status == "DeadLettered"
                && delivery.LastReceivedAt < cutoff
                && (queueName == null || delivery.QueueName == queueName))
            .ToListAsync(cancellationToken);
        if (deliveries.Count == 0)
        {
            return new ExceptionOperationResult(
                false,
                "No dead letter matches that queue and age.");
        }

        var keys = deliveries.Select(delivery => DeadLetterKey(delivery.Id)).ToArray();
        var tasks = await dbContext.ExceptionTasks
            .Where(task => keys.Contains(task.DeduplicationKey))
            .ToListAsync(cancellationToken);
        foreach (var delivery in deliveries)
        {
            delivery.Status = "DeadLetterResolved";
            delivery.UpdatedAt = now;
        }

        foreach (var task in tasks.Where(task => task.Status != ExceptionTaskStatuses.Resolved))
        {
            task.Status = ExceptionTaskStatuses.Resolved;
            task.ResolvedAt = now;
            task.ResolvedBy = actor;
            task.ResolutionReason = reason.Trim();
            task.UpdatedAt = now;
        }

        dbContext.AuditEvents.Add(NewAudit(
            "IntegrationMessageDelivery",
            queueName ?? "all-queues",
            "dead-letters-resolved",
            actor,
            new
            {
                reason,
                queueName,
                olderThanDays,
                deliveries = deliveries.Count,
                tasks = tasks.Count,
                oldest = deliveries.Min(delivery => delivery.LastReceivedAt),
                newest = deliveries.Max(delivery => delivery.LastReceivedAt)
            },
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExceptionOperationResult(true, Detail: deliveries.Count.ToString());
    }

    private async Task RecordStillUnknownAsync(
        SubmissionOperationEntity operation,
        string evidence,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(NewAudit(
            "SubmissionOperation",
            operation.Id.ToString(),
            "ambiguous-submission-evidence-recorded",
            actor,
            new { outcome = AmbiguousResolutionOutcome.StillUnknown.ToString(), evidence, reason },
            now));

        var task = await dbContext.ExceptionTasks
            .SingleOrDefaultAsync(item => item.DeduplicationKey == AmbiguousKey(operation.Id), cancellationToken);
        if (task is not null)
        {
            task.Status = ExceptionTaskStatuses.WaitingForShoprite;
            task.LatestEvidence = evidence;
            task.UpdatedAt = now;
            dbContext.ExceptionTaskComments.Add(new ExceptionTaskCommentEntity
            {
                Id = Guid.NewGuid(),
                ExceptionTaskId = task.Id,
                Actor = actor,
                Body = $"Outcome still unknown. {reason} Evidence: {evidence}",
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> RevalidateAsync(
        InvoiceCandidateEntity candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var canonical = Deserialize<Pvm.Domain.Invoices.CanonicalInvoice>(candidate.CanonicalJson);
        if (canonical is null)
        {
            return candidate.Status;
        }

        var match = await candidateMatcher.MatchAndValidateAsync(canonical, cancellationToken);
        candidate.CanonicalJson = JsonSerializer.Serialize(match.Invoice, SerializerOptions);
        candidate.ValidationJson = JsonSerializer.Serialize(match.Validation, SerializerOptions);
        candidate.MatchedShopritePurchaseOrderId = match.MatchedPurchaseOrderId;
        candidate.SupplierGln = match.Invoice.SupplierGln;
        candidate.StoreDcGln = match.Invoice.StoreDcGln;
        candidate.Status = match.Validation.CanSubmit ? "Ready" : "NeedsReview";
        candidate.UpdatedAt = now;
        return candidate.Status;
    }

    private async Task CloseDerivedTaskAsync(
        string deduplicationKey,
        string actor,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.ExceptionTasks
            .SingleOrDefaultAsync(item => item.DeduplicationKey == deduplicationKey, cancellationToken);
        if (task is null || task.Status == ExceptionTaskStatuses.Resolved)
        {
            return;
        }

        task.Status = ExceptionTaskStatuses.Resolved;
        task.ResolvedAt = now;
        task.ResolvedBy = actor;
        task.ResolutionReason = reason;
        task.UpdatedAt = now;
    }

    private async Task<Dictionary<Guid, List<ExceptionTaskCommentEntity>>> CommentsForAsync(
        Guid[] taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Length == 0)
        {
            return [];
        }

        var comments = await dbContext.ExceptionTaskComments
            .AsNoTracking()
            .Where(comment => taskIds.Contains(comment.ExceptionTaskId))
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync(cancellationToken);
        return comments
            .GroupBy(comment => comment.ExceptionTaskId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task<Dictionary<Guid, string>> InvoiceNumbersForAsync(
        IReadOnlyCollection<ExceptionTaskEntity> tasks,
        CancellationToken cancellationToken)
    {
        var invoiceIds = tasks
            .Where(task => task.InvoiceCandidateId is not null)
            .Select(task => task.InvoiceCandidateId!.Value)
            .Distinct()
            .ToArray();
        if (invoiceIds.Length == 0)
        {
            return [];
        }

        return await dbContext.InvoiceCandidates
            .AsNoTracking()
            .Where(candidate => invoiceIds.Contains(candidate.Id))
            .Select(candidate => new { candidate.Id, candidate.InvoiceNumber })
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.InvoiceNumber, cancellationToken);
    }

    private async Task<ExceptionQueueSummary> SummaryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var counts = await dbContext.ExceptionTasks
            .AsNoTracking()
            .GroupBy(task => new { task.Category, Active = ActiveStatuses.Contains(task.Status) })
            .Select(group => new { group.Key.Category, group.Key.Active, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var overdue = await dbContext.ExceptionTasks
            .AsNoTracking()
            .CountAsync(
                task => ActiveStatuses.Contains(task.Status) && task.DueAt != null && task.DueAt < now,
                cancellationToken);
        var resolved = await dbContext.ExceptionTasks
            .AsNoTracking()
            .CountAsync(task => task.Status == ExceptionTaskStatuses.Resolved, cancellationToken);

        int Active(string category) => counts
            .Where(item => item.Active && item.Category == category)
            .Sum(item => item.Count);

        return new ExceptionQueueSummary(
            Active(ExceptionCategories.AmbiguousSubmission),
            Active(ExceptionCategories.RejectedSubmission),
            Active(ExceptionCategories.NeedsReview),
            Active(ExceptionCategories.DeadLetter),
            Active(ExceptionCategories.StuckWork),
            Active(ExceptionCategories.HeldInvoice),
            overdue,
            resolved);
    }

    private async Task<IReadOnlyList<DerivedException>> AmbiguousOperationsAsync(CancellationToken cancellationToken)
    {
        var operations = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Where(operation => operation.Status == "Ambiguous")
            .Select(operation => new
            {
                operation.Id,
                operation.InvoiceCandidateId,
                operation.Generation,
                operation.SendingStartedAt,
                operation.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return operations
            .Select(operation => new DerivedException(
                AmbiguousKey(operation.Id),
                ExceptionCategories.AmbiguousSubmission,
                ExceptionSeverities.Critical,
                "SubmissionOperation",
                operation.Id.ToString(),
                operation.InvoiceCandidateId,
                "submission-ambiguous",
                "A Shoprite submission ended with an uncertain outcome. Confirm with Shoprite before any further action.",
                "Exceptions: ambiguous resolution",
                ExceptionRetryClassifications.ManualResolutionRequired,
                $"Generation {operation.Generation} started at {operation.SendingStartedAt ?? operation.CreatedAt:u}.",
                TimeSpan.FromHours(4)))
            .ToList();
    }

    private async Task<IReadOnlyList<DerivedException>> RejectedOperationsAsync(CancellationToken cancellationToken)
    {
        var operations = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Where(operation => operation.Status == "Rejected")
            .Select(operation => new
            {
                operation.Id,
                operation.InvoiceCandidateId,
                operation.Generation,
                operation.ResponseStatusCode,
                operation.ErrorMessage,
                operation.FailureClassification
            })
            .ToListAsync(cancellationToken);

        return operations
            .Select(operation => new DerivedException(
                RejectedKey(operation.Id),
                ExceptionCategories.RejectedSubmission,
                ExceptionSeverities.High,
                "SubmissionOperation",
                operation.Id.ToString(),
                operation.InvoiceCandidateId,
                operation.FailureClassification ?? "submission-rejected",
                "Shoprite rejected this invoice. Correct the source or the mappings, then authorize a retry.",
                "Invoice detail, then inventory mappings",
                ExceptionRetryClassifications.RetryAfterFix,
                $"HTTP {operation.ResponseStatusCode?.ToString() ?? "none"}: {Trim(operation.ErrorMessage, 400)}",
                TimeSpan.FromHours(24)))
            .ToList();
    }

    private async Task<IReadOnlyList<DerivedException>> StuckOperationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var threshold = now - StuckThreshold;
        var operations = await dbContext.SubmissionOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Status == "Sending"
                && operation.SendingStartedAt != null
                && operation.SendingStartedAt < threshold)
            .Select(operation => new { operation.Id, operation.InvoiceCandidateId, operation.SendingStartedAt })
            .ToListAsync(cancellationToken);

        return operations
            .Select(operation => new DerivedException(
                $"stuck-submission:{operation.Id}",
                ExceptionCategories.StuckWork,
                ExceptionSeverities.Critical,
                "SubmissionOperation",
                operation.Id.ToString(),
                operation.InvoiceCandidateId,
                "submission-stuck-sending",
                "A submission has stayed in the sending state past the recovery threshold.",
                "Exceptions: ambiguous resolution",
                ExceptionRetryClassifications.ManualResolutionRequired,
                $"Sending since {operation.SendingStartedAt:u}.",
                TimeSpan.FromHours(1)))
            .ToList();
    }

    private async Task<IReadOnlyList<DerivedException>> CandidateExceptionsAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .Where(candidate => candidate.Status == "NeedsReview" || candidate.Status == "Suspended")
            .Select(candidate => new
            {
                candidate.Id,
                candidate.InvoiceNumber,
                candidate.Status,
                candidate.ShopritePurchaseOrderNumber,
                candidate.MatchedShopritePurchaseOrderId,
                candidate.ValidationJson,
                candidate.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var derived = new List<DerivedException>();
        foreach (var candidate in candidates)
        {
            if (candidate.Status == "Suspended")
            {
                derived.Add(new DerivedException(
                    HeldKey(candidate.Id),
                    ExceptionCategories.HeldInvoice,
                    ExceptionSeverities.Medium,
                    "InvoiceCandidate",
                    candidate.Id.ToString(),
                    candidate.Id,
                    "invoice-held",
                    $"Invoice {candidate.InvoiceNumber} is on hold. No manual or automatic submission can run.",
                    "Exceptions: release the hold",
                    ExceptionRetryClassifications.NoRetry,
                    $"Held since {candidate.UpdatedAt:u}.",
                    TimeSpan.FromDays(3)));
                continue;
            }

            var issues = ValidationIssues(candidate.ValidationJson);
            derived.Add(new DerivedException(
                NeedsReviewKey(candidate.Id),
                ExceptionCategories.NeedsReview,
                ExceptionSeverities.Medium,
                "InvoiceCandidate",
                candidate.Id.ToString(),
                candidate.Id,
                candidate.MatchedShopritePurchaseOrderId is null
                    ? "missing-local-shoprite-po"
                    : "validation-blocked",
                $"Invoice {candidate.InvoiceNumber} cannot be submitted yet.",
                candidate.MatchedShopritePurchaseOrderId is null
                    ? "Shoprite PO inbox"
                    : "Inventory mappings or the Acumatica invoice",
                ExceptionRetryClassifications.RetryAfterFix,
                issues,
                TimeSpan.FromDays(2)));
        }

        return derived;
    }

    private async Task<IReadOnlyList<DerivedException>> DeadLetterExceptionsAsync(CancellationToken cancellationToken)
    {
        var deliveries = await dbContext.IntegrationMessageDeliveries
            .Where(delivery => delivery.Status == "DeadLettered")
            .ToListAsync(cancellationToken);

        var derived = new List<DerivedException>();
        foreach (var delivery in deliveries)
        {
            var safety = await EvaluateReplaySafetyAsync(delivery, cancellationToken);
            derived.Add(new DerivedException(
                DeadLetterKey(delivery.Id),
                ExceptionCategories.DeadLetter,
                delivery.MessageType == IntegrationMessageTypes.ShopriteInvoiceSubmitV1
                    ? ExceptionSeverities.Critical
                    : ExceptionSeverities.High,
                "IntegrationMessageDelivery",
                delivery.Id.ToString(),
                null,
                delivery.ErrorCode ?? "dead-lettered",
                $"A {delivery.MessageType} message on {delivery.QueueName} was dead-lettered after {delivery.DeliveryCount} deliveries.",
                "Exceptions: dead letters",
                safety.Safe
                    ? ExceptionRetryClassifications.SafeToReplay
                    : ExceptionRetryClassifications.ManualResolutionRequired,
                $"{Trim(delivery.ErrorSummary, 400)} Replay assessment: {safety.Reason}",
                TimeSpan.FromHours(12)));
        }

        return derived;
    }

    private async Task<IReadOnlyList<DerivedException>> StuckOutboxExceptionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var threshold = now - StuckThreshold;
        var messages = await dbContext.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.Status != "Published"
                && message.CreatedAt < threshold
                && message.PublishAttempts > 0)
            .Select(message => new
            {
                message.Id,
                message.QueueName,
                message.MessageType,
                message.PublishAttempts,
                message.LastErrorSummary,
                message.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return messages
            .Select(message => new DerivedException(
                $"stuck-outbox:{message.Id}",
                ExceptionCategories.StuckWork,
                ExceptionSeverities.High,
                "IntegrationOutboxMessage",
                message.Id.ToString(),
                null,
                "outbox-not-published",
                $"A {message.MessageType} message has not reached {message.QueueName} after {message.PublishAttempts} attempts.",
                "Connections: Service Bus health",
                ExceptionRetryClassifications.RetryAfterFix,
                $"Created {message.CreatedAt:u}. {Trim(message.LastErrorSummary, 400)}",
                TimeSpan.FromHours(6)))
            .ToList();
    }

    private static ExceptionTaskEntity NewDerivedTask(DerivedException item, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            DeduplicationKey = item.Key,
            Category = item.Category,
            Severity = item.Severity,
            Status = ExceptionTaskStatuses.Open,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            InvoiceCandidateId = item.InvoiceCandidateId,
            ErrorCode = item.ErrorCode,
            Summary = item.Summary,
            FixLocation = item.FixLocation,
            RetryClassification = item.RetryClassification,
            OccurrenceCount = 1,
            LatestEvidence = Trim(item.Evidence, 2000),
            IsDerived = true,
            FirstSeenAt = now,
            LastSeenAt = now,
            DueAt = now + item.DueIn,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static void UpdateDerivedTask(ExceptionTaskEntity task, DerivedException item, DateTimeOffset now)
    {
        var evidence = Trim(item.Evidence, 2000);
        var evidenceChanged = !string.Equals(task.LatestEvidence, evidence, StringComparison.Ordinal);
        task.Category = item.Category;
        task.Severity = item.Severity;
        task.ErrorCode = item.ErrorCode;
        task.Summary = item.Summary;
        task.FixLocation = item.FixLocation;
        task.RetryClassification = item.RetryClassification;
        task.LatestEvidence = evidence;
        task.LastSeenAt = now;
        task.UpdatedAt = now;
        if (evidenceChanged)
        {
            task.OccurrenceCount++;
        }

        if (task.Status == ExceptionTaskStatuses.Resolved)
        {
            task.Status = ExceptionTaskStatuses.Open;
            task.ResolvedAt = null;
            task.ResolvedBy = null;
            task.ResolutionReason = null;
            task.OccurrenceCount++;
            task.DueAt = now + item.DueIn;
        }
    }

    private static ExceptionTaskView ToView(
        ExceptionTaskEntity task,
        IReadOnlyDictionary<Guid, List<ExceptionTaskCommentEntity>> comments,
        IReadOnlyDictionary<Guid, string> invoiceNumbers,
        DateTimeOffset now)
    {
        var taskComments = comments.TryGetValue(task.Id, out var list)
            ? list.Select(comment => new ExceptionTaskComment(
                comment.Id,
                comment.Actor,
                comment.Body,
                comment.CreatedAt)).ToList()
            : [];
        string? invoiceNumber = null;
        if (task.InvoiceCandidateId is not null
            && invoiceNumbers.TryGetValue(task.InvoiceCandidateId.Value, out var number))
        {
            invoiceNumber = number;
        }

        return new ExceptionTaskView(
            task.Id,
            task.DeduplicationKey,
            task.Category,
            task.Severity,
            task.Status,
            task.EntityType,
            task.EntityId,
            task.InvoiceCandidateId?.ToString(),
            invoiceNumber,
            task.ErrorCode,
            task.Summary,
            task.FixLocation,
            task.RetryClassification,
            task.Owner,
            task.OccurrenceCount,
            task.LatestEvidence,
            task.FirstSeenAt,
            task.LastSeenAt,
            task.DueAt,
            ExceptionTaskStatuses.IsActive(task.Status) && task.DueAt is not null && task.DueAt < now,
            task.ResolvedAt,
            task.ResolvedBy,
            task.ResolutionReason,
            taskComments);
    }

    private static string RewriteEnvelope(
        string payloadJson,
        Guid replayMessageId,
        string causationId,
        DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : default;
        var envelope = new IntegrationMessageEnvelope(
            replayMessageId,
            root.GetProperty("messageType").GetString() ?? string.Empty,
            root.TryGetProperty("correlationId", out var correlation)
                ? correlation.GetString() ?? replayMessageId.ToString("D")
                : replayMessageId.ToString("D"),
            causationId,
            now,
            data);
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    private static Guid? ReadSubmitCandidateId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("data", out var data))
            {
                return null;
            }

            var message = data.Deserialize<SubmitShopriteInvoiceMessage>(SerializerOptions);
            return message?.CandidateId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ValidationIssues(string? validationJson)
    {
        var validation = Deserialize<Pvm.Domain.Validation.ValidationResult>(validationJson);
        if (validation is null || validation.Issues.Count == 0)
        {
            return "No validation detail is stored for this invoice.";
        }

        return Trim(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), 1500);
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string AmbiguousKey(Guid operationId) => $"ambiguous-submission:{operationId}";

    private static string RejectedKey(Guid operationId) => $"rejected-submission:{operationId}";

    private static string NeedsReviewKey(Guid candidateId) => $"needs-review:{candidateId}";

    private static string HeldKey(Guid candidateId) => $"held-invoice:{candidateId}";

    private static string DeadLetterKey(Guid deliveryId) => $"dead-letter:{deliveryId}";

    private static AuditEventEntity NewAudit(
        string entityType,
        string entityId,
        string action,
        string actor,
        object details,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = actor,
            DetailsJson = JsonSerializer.Serialize(details, SerializerOptions),
            CreatedAt = now
        };

    private sealed record DerivedException(
        string Key,
        string Category,
        string Severity,
        string EntityType,
        string EntityId,
        Guid? InvoiceCandidateId,
        string ErrorCode,
        string Summary,
        string FixLocation,
        string RetryClassification,
        string Evidence,
        TimeSpan DueIn);
}
