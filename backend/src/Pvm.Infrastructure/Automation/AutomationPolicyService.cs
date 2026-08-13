using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pvm.Application.Automation;
using Pvm.Application.Messaging;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Automation;

public sealed record AutomationPolicyChange(
    int ExpectedVersion,
    AutomationMode Mode,
    IReadOnlyCollection<string> AccountAllowlist,
    IReadOnlyCollection<string> LocationAllowlist,
    IReadOnlyCollection<string> SupportedOrderTypes,
    int StabilizationDelayMinutes,
    int PurchaseOrderFreshnessMinutes,
    int AcumaticaFreshnessMinutes,
    int DailyAutomaticSubmissionCap,
    TimeOnly AutomaticWindowStart,
    TimeOnly AutomaticWindowEnd,
    string TimeZoneId,
    string Actor,
    string Reason);

public sealed record AutomationPolicyChangeResult(
    bool Changed,
    bool Conflict,
    string? Error,
    AutomationPolicy Policy);

public sealed record AutomationDecisionView(
    Guid Id,
    Guid InvoiceCandidateId,
    string InvoiceNumber,
    int PolicyVersion,
    string Outcome,
    IReadOnlyList<string> ReasonCodes,
    string Summary,
    DateTimeOffset? NotBefore,
    Guid? CommandId,
    Guid? MessageId,
    DateTimeOffset EvaluatedAt);

public sealed record AutomationDecisionSummary(
    int Evaluated,
    int WouldSubmit,
    int Queued,
    int Excluded,
    int Disabled,
    int EmergencyStopped);

public sealed record AutomationPolicyView(
    AutomationPolicy Policy,
    AutomationDecisionSummary DecisionSummary,
    IReadOnlyList<AutomationDecisionView> RecentDecisions,
    IReadOnlyList<AutomationPolicy> RecentVersions);

public sealed record AutomationEvaluationBatch(int Evaluated, int WouldSubmit, int Queued, int Excluded);

public sealed class AutomationPolicyService(
    PvmDbContext dbContext,
    IIntegrationCommandQueue commandQueue) : IAutomationSubmissionGate
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string Automatic = "automatic";

    public async Task<AutomationPolicy> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var current = await dbContext.AutomationPolicyVersions
            .AsNoTracking()
            .OrderByDescending(policy => policy.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is not null)
        {
            return ToPolicy(current);
        }

        var initial = InitialPolicy();
        dbContext.AutomationPolicyVersions.Add(initial);
        dbContext.AuditEvents.Add(NewAudit(
            initial.Version.ToString(),
            "automation-policy-initialized",
            initial.CreatedBy,
            new { initial.Mode, initial.EmergencyStop, initial.Reason },
            initial.CreatedAt));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToPolicy(initial);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            current = await dbContext.AutomationPolicyVersions
                .AsNoTracking()
                .OrderByDescending(policy => policy.Version)
                .FirstAsync(cancellationToken);
            return ToPolicy(current);
        }
    }

    public async Task<AutomationPolicyView> GetViewAsync(CancellationToken cancellationToken)
    {
        var policy = await GetCurrentAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var counts = await dbContext.AutomationDecisions
            .AsNoTracking()
            .Where(decision => decision.PolicyVersion == policy.Version && decision.EvaluatedAt >= since)
            .GroupBy(decision => decision.Outcome)
            .Select(group => new { Outcome = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Outcome, item => item.Count, cancellationToken);
        var recent = await (
            from decision in dbContext.AutomationDecisions.AsNoTracking()
            join candidate in dbContext.InvoiceCandidates.AsNoTracking()
                on decision.InvoiceCandidateId equals candidate.Id
            orderby decision.EvaluatedAt descending
            select new AutomationDecisionView(
                decision.Id,
                decision.InvoiceCandidateId,
                candidate.InvoiceNumber,
                decision.PolicyVersion,
                decision.Outcome,
                decision.ReasonCodes,
                decision.Summary,
                decision.NotBefore,
                decision.CommandId,
                decision.MessageId,
                decision.EvaluatedAt))
            .Take(100)
            .ToListAsync(cancellationToken);
        var versions = await dbContext.AutomationPolicyVersions
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new AutomationPolicyView(
            policy,
            new AutomationDecisionSummary(
                counts.Values.Sum(),
                Count("WouldSubmit"),
                Count("Queued"),
                counts.Where(item => item.Key is not ("WouldSubmit" or "Queued" or "Disabled" or "EmergencyStopped"))
                    .Sum(item => item.Value),
                Count("Disabled"),
                Count("EmergencyStopped")),
            recent,
            versions.Select(ToPolicy).ToArray());

        int Count(string outcome) => counts.GetValueOrDefault(outcome);
    }

    public async Task<AutomationPolicyChangeResult> ChangeAsync(
        AutomationPolicyChange change,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(cancellationToken);
        var error = Validate(change);
        if (error is not null)
        {
            return new AutomationPolicyChangeResult(false, false, error, current);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AutomationPolicyLock.AcquireAsync(dbContext, cancellationToken);
        current = ToPolicy(await dbContext.AutomationPolicyVersions
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .FirstAsync(cancellationToken));
        if (change.ExpectedVersion != current.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AutomationPolicyChangeResult(false, true, "The automation policy changed. Reload before saving.", current);
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new AutomationPolicyVersionEntity
        {
            Id = Guid.NewGuid(),
            Version = current.Version + 1,
            Mode = change.Mode.ToString(),
            EmergencyStop = current.EmergencyStop,
            AccountAllowlist = Normalize(change.AccountAllowlist),
            LocationAllowlist = Normalize(change.LocationAllowlist),
            SupportedOrderTypes = Normalize(change.SupportedOrderTypes),
            StabilizationDelayMinutes = change.StabilizationDelayMinutes,
            PurchaseOrderFreshnessMinutes = change.PurchaseOrderFreshnessMinutes,
            AcumaticaFreshnessMinutes = change.AcumaticaFreshnessMinutes,
            DailyAutomaticSubmissionCap = change.DailyAutomaticSubmissionCap,
            AutomaticWindowStart = change.AutomaticWindowStart,
            AutomaticWindowEnd = change.AutomaticWindowEnd,
            TimeZoneId = change.TimeZoneId.Trim(),
            CreatedBy = change.Actor,
            Reason = change.Reason.Trim(),
            CreatedAt = now
        };
        dbContext.AutomationPolicyVersions.Add(entity);
        dbContext.AuditEvents.Add(NewAudit(
            entity.Version.ToString(),
            "automation-policy-changed",
            change.Actor,
            new
            {
                previousVersion = current.Version,
                previousMode = current.Mode,
                entity.Mode,
                entity.AccountAllowlist,
                entity.LocationAllowlist,
                entity.SupportedOrderTypes,
                entity.StabilizationDelayMinutes,
                entity.PurchaseOrderFreshnessMinutes,
                entity.AcumaticaFreshnessMinutes,
                entity.DailyAutomaticSubmissionCap,
                entity.AutomaticWindowStart,
                entity.AutomaticWindowEnd,
                entity.TimeZoneId,
                entity.Reason
            },
            now));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AutomationPolicyChangeResult(true, false, null, ToPolicy(entity));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            current = await GetCurrentAsync(cancellationToken);
            return new AutomationPolicyChangeResult(false, true, "The automation policy changed. Reload before saving.", current);
        }
    }

    public async Task<AutomationPolicyChangeResult> SetEmergencyStopAsync(
        int expectedVersion,
        bool active,
        string actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            return new AutomationPolicyChangeResult(false, false, "A meaningful emergency-control reason is required.", current);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AutomationPolicyLock.AcquireAsync(dbContext, cancellationToken);
        current = ToPolicy(await dbContext.AutomationPolicyVersions
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .FirstAsync(cancellationToken));
        if (expectedVersion != current.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AutomationPolicyChangeResult(false, true, "The automation policy changed. Reload before saving.", current);
        }
        if (current.EmergencyStop == active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AutomationPolicyChangeResult(false, false, null, current);
        }

        var now = DateTimeOffset.UtcNow;
        var entity = FromPolicy(current with
        {
            Version = current.Version + 1,
            EmergencyStop = active,
            CreatedBy = actor,
            Reason = reason.Trim(),
            CreatedAt = now
        });
        dbContext.AutomationPolicyVersions.Add(entity);
        dbContext.AuditEvents.Add(NewAudit(
            entity.Version.ToString(),
            active ? "automation-emergency-stop-activated" : "automation-emergency-stop-cleared",
            actor,
            new { previousVersion = current.Version, active, reason = reason.Trim() },
            now));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AutomationPolicyChangeResult(true, false, null, ToPolicy(entity));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            current = await GetCurrentAsync(cancellationToken);
            return new AutomationPolicyChangeResult(false, true, "The automation policy changed. Reload before saving.", current);
        }
    }

    public async Task<AutomationSubmissionPermission> EvaluateSubmissionAsync(
        Guid invoiceCandidateId,
        string initiationMode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = await GetCurrentAsync(cancellationToken);
        if (policy.EmergencyStop)
        {
            return Denied(policy, "emergency-stop", "Invoice submission is disabled by the emergency stop.");
        }
        if (!string.Equals(initiationMode, Automatic, StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationSubmissionPermission(true, policy.Version, "manual-authorized", "Manual submission is permitted.");
        }
        if (policy.Mode is AutomationMode.Disabled or AutomationMode.Shadow)
        {
            return Denied(policy, "automation-mode-blocked", $"Automatic submission is blocked while mode is {policy.Mode}.");
        }

        var evaluation = await EvaluateCoreAsync(invoiceCandidateId, policy, now, cancellationToken);
        return evaluation.ReasonCodes.Count == 0
            ? new AutomationSubmissionPermission(true, policy.Version, "eligible", "Candidate satisfies the active automation policy.")
            : Denied(policy, evaluation.ReasonCodes[0], evaluation.Summary);
    }

    public async Task<AutomationEvaluationBatch> EvaluateCandidatesAsync(
        IReadOnlyCollection<Guid>? candidateIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AutomationPolicyLock.AcquireAsync(dbContext, cancellationToken);
        var policy = await GetCurrentAsync(cancellationToken);
        var ids = candidateIds is null
            ? await dbContext.InvoiceCandidates.AsNoTracking().Select(candidate => candidate.Id).ToArrayAsync(cancellationToken)
            : candidateIds.Distinct().ToArray();
        var wouldSubmit = 0;
        var queued = 0;
        var excluded = 0;

        foreach (var candidateId in ids)
        {
            var evaluation = await EvaluateCoreAsync(candidateId, policy, now, cancellationToken);
            var outcome = policy.EmergencyStop
                ? "EmergencyStopped"
                : policy.Mode == AutomationMode.Disabled
                    ? "Disabled"
                    : evaluation.ReasonCodes.Count > 0
                        ? OutcomeFor(evaluation.ReasonCodes)
                        : policy.Mode == AutomationMode.Shadow ? "WouldSubmit" : "Queued";

            if (outcome == "WouldSubmit") wouldSubmit++;
            else if (outcome == "Queued") queued++;
            else excluded++;

            await PersistDecisionAsync(evaluation, policy, outcome, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AutomationEvaluationBatch(ids.Length, wouldSubmit, queued, excluded);
    }

    private async Task PersistDecisionAsync(
        CandidateEvaluation evaluation,
        AutomationPolicy policy,
        string outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.AutomationDecisions.AsNoTracking().AnyAsync(
            item => item.InvoiceCandidateId == evaluation.CandidateId
                && item.PolicyVersion == policy.Version
                && item.SourceVersion == evaluation.SourceVersion
                && item.Outcome == outcome,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var decisionId = DeterministicGuid($"decision:{evaluation.CandidateId:D}:{policy.Version}:{evaluation.SourceVersion}:{outcome}");
        var commandId = outcome == "Queued"
            ? DeterministicGuid($"command:{evaluation.CandidateId:D}:{policy.Version}:{evaluation.SourceVersion}")
            : (Guid?)null;
        var messageId = outcome == "Queued"
            ? DeterministicGuid($"message:{evaluation.CandidateId:D}:{policy.Version}:{evaluation.SourceVersion}")
            : (Guid?)null;
        dbContext.AutomationDecisions.Add(new AutomationDecisionEntity
        {
            Id = decisionId,
            InvoiceCandidateId = evaluation.CandidateId,
            PolicyVersion = policy.Version,
            SourceVersion = evaluation.SourceVersion,
            Outcome = outcome,
            ReasonCodes = evaluation.ReasonCodes.ToArray(),
            Summary = outcome switch
            {
                "WouldSubmit" => "Candidate would be submitted under the active policy; Shadow mode prevented the send.",
                "Queued" => "Candidate was queued for automatic submission.",
                "Disabled" => "Automation is disabled; candidate was evaluated without queueing.",
                "EmergencyStopped" => "The emergency stop prevented automatic submission.",
                _ => evaluation.Summary
            },
            NotBefore = evaluation.NotBefore,
            CommandId = commandId,
            MessageId = messageId,
            EvaluatedAt = now
        });

        try
        {
            if (outcome == "Queued")
            {
                await commandQueue.EnqueueAsync(
                    IntegrationQueues.ShopriteInvoiceSubmit,
                    IntegrationMessageTypes.ShopriteInvoiceSubmitV1,
                    new SubmitShopriteInvoiceMessage(
                        evaluation.CandidateId,
                        commandId!.Value,
                        $"system:automation-policy-v{policy.Version}",
                        Automatic),
                    $"automation:{decisionId:D}",
                    messageId: messageId,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<CandidateEvaluation> EvaluateCoreAsync(
        Guid candidateId,
        AutomationPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidate = await dbContext.InvoiceCandidates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return new CandidateEvaluation(candidateId, "missing", ["candidate-not-found"], "Invoice candidate was not found.", null);
        }

        var sourceVersion = SourceVersion(candidate);
        var reasons = new List<string>();
        CanonicalInvoice? invoice = null;
        ValidationResult? validation = null;
        try
        {
            invoice = string.IsNullOrWhiteSpace(candidate.CanonicalJson)
                ? null
                : JsonSerializer.Deserialize<CanonicalInvoice>(candidate.CanonicalJson, SerializerOptions);
            validation = string.IsNullOrWhiteSpace(candidate.ValidationJson)
                ? null
                : JsonSerializer.Deserialize<ValidationResult>(candidate.ValidationJson, SerializerOptions);
        }
        catch (JsonException)
        {
            reasons.Add("candidate-payload-invalid");
        }

        var purchaseOrder = candidate.MatchedShopritePurchaseOrderId is null
            ? null
            : await dbContext.ShopritePurchaseOrders.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == candidate.MatchedShopritePurchaseOrderId, cancellationToken);
        if (candidate.Status != "Ready" || validation?.CanSubmit != true) reasons.Add("candidate-not-ready");
        if (purchaseOrder is null) reasons.Add("purchase-order-unmatched");
        if (invoice is null) reasons.Add("canonical-invoice-missing");
        if (invoice is not null)
        {
            if (!string.Equals(invoice.CurrencyCode, "ZAR", StringComparison.OrdinalIgnoreCase)) reasons.Add("unsupported-currency");
            if (!string.Equals(invoice.CountryCode, "ZA", StringComparison.OrdinalIgnoreCase)) reasons.Add("unsupported-country");
            if (invoice.Lines.Any(line => line.IsCatchWeight)) reasons.Add("catch-weight-excluded");
        }
        if (purchaseOrder?.OrderTypeCode is null
            || !policy.SupportedOrderTypes.Contains(purchaseOrder.OrderTypeCode, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("order-type-not-supported");
        }
        if (policy.Mode == AutomationMode.Allowlisted)
        {
            if (!policy.AccountAllowlist.Contains(candidate.CustomerAccount, StringComparer.OrdinalIgnoreCase))
            {
                reasons.Add("account-not-allowlisted");
            }
            var locationKeys = new[]
            {
                candidate.CustomerLocation,
                candidate.StoreDcGln,
                purchaseOrder?.DeliveryLocationCode,
                purchaseOrder?.DeliveryGln
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
            if (!locationKeys.Any(key => policy.LocationAllowlist.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                reasons.Add("location-not-allowlisted");
            }
        }

        var priorOperation = await dbContext.SubmissionOperations.AsNoTracking()
            .AnyAsync(operation => operation.InvoiceCandidateId == candidateId, cancellationToken);
        if (priorOperation) reasons.Add("prior-submission-operation");

        var latestPoRefresh = await LatestSuccessAsync(IntegrationRunTypes.ShopritePurchaseOrderRefresh, cancellationToken);
        if (latestPoRefresh is null || now - latestPoRefresh > TimeSpan.FromMinutes(policy.PurchaseOrderFreshnessMinutes))
        {
            reasons.Add("shoprite-po-data-stale");
        }
        var latestAcumatica = await LatestSuccessAsync(IntegrationRunTypes.AcumaticaInvoiceReconciliation, cancellationToken);
        if (latestAcumatica is null || now - latestAcumatica > TimeSpan.FromMinutes(policy.AcumaticaFreshnessMinutes))
        {
            reasons.Add("acumatica-reconciliation-stale");
        }

        var notBefore = candidate.UpdatedAt.AddMinutes(policy.StabilizationDelayMinutes);
        if (now < notBefore) reasons.Add("stabilization-delay");

        if (!IsInsideAutomaticWindow(policy, now)) reasons.Add("outside-automatic-window");
        if (policy.DailyAutomaticSubmissionCap > 0)
        {
            var (dayStart, dayEnd) = LocalDayBounds(policy, now);
            var automaticCount = await dbContext.AutomationDecisions.AsNoTracking().CountAsync(
                decision => decision.Outcome == "Queued"
                    && decision.EvaluatedAt >= dayStart
                    && decision.EvaluatedAt < dayEnd,
                cancellationToken);
            if (automaticCount >= policy.DailyAutomaticSubmissionCap) reasons.Add("daily-cap-reached");
        }

        return new CandidateEvaluation(
            candidateId,
            sourceVersion,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            reasons.Count == 0
                ? "Candidate satisfies the active automation policy."
                : $"Automatic submission blocked: {string.Join(", ", reasons.Distinct(StringComparer.Ordinal))}.",
            notBefore);
    }

    private async Task<DateTimeOffset?> LatestSuccessAsync(string runType, CancellationToken cancellationToken)
        => await dbContext.IntegrationRuns.AsNoTracking()
            .Where(run => run.RunType == runType && run.Status == IntegrationRunStatuses.Succeeded && run.CompletedAt != null)
            .OrderByDescending(run => run.CompletedAt)
            .Select(run => run.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsInsideAutomaticWindow(AutomationPolicy policy, DateTimeOffset now)
    {
        if (policy.AutomaticWindowStart == policy.AutomaticWindowEnd) return true;
        var local = TimeZoneInfo.ConvertTime(now, ResolveTimeZone(policy.TimeZoneId)).TimeOfDay;
        var start = policy.AutomaticWindowStart.ToTimeSpan();
        var end = policy.AutomaticWindowEnd.ToTimeSpan();
        return start < end ? local >= start && local < end : local >= start || local < end;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) LocalDayBounds(AutomationPolicy policy, DateTimeOffset now)
    {
        var zone = ResolveTimeZone(policy.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var localStart = local.Date;
        var start = new DateTimeOffset(localStart, zone.GetUtcOffset(localStart)).ToUniversalTime();
        return (start, start.AddDays(1));
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string? Validate(AutomationPolicyChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Trim().Length < 5) return "A meaningful policy-change reason is required.";
        if (change.StabilizationDelayMinutes is < 0 or > 1440) return "Stabilization delay must be between 0 and 1440 minutes.";
        if (change.PurchaseOrderFreshnessMinutes is < 1 or > 10080) return "PO freshness must be between 1 and 10080 minutes.";
        if (change.AcumaticaFreshnessMinutes is < 1 or > 10080) return "Acumatica freshness must be between 1 and 10080 minutes.";
        if (change.DailyAutomaticSubmissionCap is < 0 or > 100000) return "Daily cap must be between 0 and 100000.";
        if (Normalize(change.SupportedOrderTypes).Length == 0) return "At least one supported order type is required.";
        if (change.Mode == AutomationMode.Allowlisted
            && (Normalize(change.AccountAllowlist).Length == 0 || Normalize(change.LocationAllowlist).Length == 0))
        {
            return "Allowlisted mode requires at least one account and one delivery location.";
        }
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(change.TimeZoneId.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return "The automation time zone is invalid.";
        }
        return null;
    }

    private static AutomationPolicyVersionEntity InitialPolicy()
        => new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            Mode = AutomationMode.Disabled.ToString(),
            EmergencyStop = false,
            AccountAllowlist = [],
            LocationAllowlist = [],
            SupportedOrderTypes = ["220"],
            StabilizationDelayMinutes = 15,
            PurchaseOrderFreshnessMinutes = 15,
            AcumaticaFreshnessMinutes = 30,
            DailyAutomaticSubmissionCap = 10,
            AutomaticWindowStart = new TimeOnly(6, 0),
            AutomaticWindowEnd = new TimeOnly(18, 0),
            TimeZoneId = "Africa/Johannesburg",
            CreatedBy = "system:initial-policy",
            Reason = "Initial safe default with automatic submission disabled.",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static AutomationPolicy ToPolicy(AutomationPolicyVersionEntity entity)
        => new(
            entity.Version,
            Enum.Parse<AutomationMode>(entity.Mode, ignoreCase: true),
            entity.EmergencyStop,
            entity.AccountAllowlist,
            entity.LocationAllowlist,
            entity.SupportedOrderTypes,
            entity.StabilizationDelayMinutes,
            entity.PurchaseOrderFreshnessMinutes,
            entity.AcumaticaFreshnessMinutes,
            entity.DailyAutomaticSubmissionCap,
            entity.AutomaticWindowStart,
            entity.AutomaticWindowEnd,
            entity.TimeZoneId,
            entity.CreatedBy,
            entity.Reason,
            entity.CreatedAt);

    private static AutomationPolicyVersionEntity FromPolicy(AutomationPolicy policy)
        => new()
        {
            Id = Guid.NewGuid(),
            Version = policy.Version,
            Mode = policy.Mode.ToString(),
            EmergencyStop = policy.EmergencyStop,
            AccountAllowlist = policy.AccountAllowlist.ToArray(),
            LocationAllowlist = policy.LocationAllowlist.ToArray(),
            SupportedOrderTypes = policy.SupportedOrderTypes.ToArray(),
            StabilizationDelayMinutes = policy.StabilizationDelayMinutes,
            PurchaseOrderFreshnessMinutes = policy.PurchaseOrderFreshnessMinutes,
            AcumaticaFreshnessMinutes = policy.AcumaticaFreshnessMinutes,
            DailyAutomaticSubmissionCap = policy.DailyAutomaticSubmissionCap,
            AutomaticWindowStart = policy.AutomaticWindowStart,
            AutomaticWindowEnd = policy.AutomaticWindowEnd,
            TimeZoneId = policy.TimeZoneId,
            CreatedBy = policy.CreatedBy,
            Reason = policy.Reason,
            CreatedAt = policy.CreatedAt
        };

    private static string[] Normalize(IEnumerable<string> values)
        => values.Select(value => value.Trim().ToUpperInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static AutomationSubmissionPermission Denied(AutomationPolicy policy, string code, string message)
        => new(false, policy.Version, code, message);

    private static string OutcomeFor(IReadOnlyCollection<string> reasons)
    {
        if (reasons.Contains("stabilization-delay")) return "Stabilizing";
        if (reasons.Contains("outside-automatic-window")) return "OutsideWindow";
        if (reasons.Contains("daily-cap-reached")) return "Capped";
        return "Excluded";
    }

    private static string SourceVersion(InvoiceCandidateEntity candidate)
        => Sha256(string.Join(
            "\n",
            candidate.SourceJson ?? string.Empty,
            candidate.CanonicalJson ?? string.Empty,
            candidate.ValidationJson ?? string.Empty,
            candidate.MatchedShopritePurchaseOrderId?.ToString() ?? string.Empty));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static AuditEventEntity NewAudit(string entityId, string action, string actor, object details, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            EntityType = "AutomationPolicy",
            EntityId = entityId,
            Action = action,
            Actor = actor,
            DetailsJson = JsonSerializer.Serialize(details, SerializerOptions),
            CreatedAt = now
        };

    private sealed record CandidateEvaluation(
        Guid CandidateId,
        string SourceVersion,
        IReadOnlyList<string> ReasonCodes,
        string Summary,
        DateTimeOffset? NotBefore);
}
