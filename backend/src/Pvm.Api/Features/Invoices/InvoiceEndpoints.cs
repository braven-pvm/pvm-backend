using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pvm.Api.Features.Invoices.Models;
using Pvm.Application.Acumatica;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Shoprite;

namespace Pvm.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapGet("/candidates", ListCandidatesAsync)
            .RequireAuthorization("Invoices.Read");
        group.MapGet("/candidates/{id:guid}", GetCandidateAsync)
            .RequireAuthorization("Invoices.Read");
        group.MapPost("/refresh", RefreshCandidatesAsync)
            .RequireAuthorization("Invoices.Write");
        group.MapPost("/{id:guid}/revalidate", RevalidateCandidateAsync)
            .RequireAuthorization("Invoices.Write");
        return app;
    }

    private static async Task<IResult> ListCandidatesAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(candidates.Select(ToSummaryResponse).ToArray());
    }

    private static async Task<IResult> GetCandidateAsync(
        Guid id,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var candidate = await dbContext.InvoiceCandidates
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (candidate is null)
        {
            return Results.NotFound(new { id, message = "Invoice candidate not found." });
        }

        var attempts = await dbContext.InvoiceSubmissionAttempts
            .AsNoTracking()
            .Where(attempt => attempt.InvoiceCandidateId == id)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .ToListAsync(cancellationToken);

        var matchedPurchaseOrder = candidate.MatchedShopritePurchaseOrderId is null
            ? null
            : await dbContext.ShopritePurchaseOrders
                .AsNoTracking()
                .Include(order => order.Lines)
                .SingleOrDefaultAsync(order => order.Id == candidate.MatchedShopritePurchaseOrderId, cancellationToken);

        var source = Deserialize<object>(candidate.SourceJson);
        var canonical = Deserialize<CanonicalInvoice>(candidate.CanonicalJson);
        var validation = Deserialize<ValidationResult>(candidate.ValidationJson) ?? new ValidationResult([]);
        var generatedXml = canonical is null ? null : ShopriteInvoiceXmlGenerator.Generate(canonical);

        return Results.Ok(new InvoiceCandidateDetailResponse(
            candidate.Id,
            candidate.Status,
            InvoiceCandidateReadiness.CanSubmit(
                validation,
                candidate.MatchedShopritePurchaseOrderId,
                candidate.Status,
                attempts.Select(attempt => attempt.Status).ToArray()),
            source,
            canonical,
            matchedPurchaseOrder is null ? null : ToPurchaseOrderResponse(matchedPurchaseOrder),
            validation,
            generatedXml,
            attempts.Select(ToAttemptResponse).ToArray()));
    }

    private static async Task<IResult> RefreshCandidatesAsync(
        IWebHostEnvironment environment,
        PvmDbContext dbContext,
        IOptions<AcumaticaOptions> acumaticaOptions,
        AcumaticaInvoiceCandidateRefreshService acumaticaRefreshService,
        ShopriteInvoiceCandidateMatcher candidateMatcher,
        CancellationToken cancellationToken)
    {
        if (acumaticaOptions.Value.InvoiceSourceMode == AcumaticaInvoiceSourceMode.RealQa)
        {
            var result = await acumaticaRefreshService.RefreshAsync(cancellationToken);
            return Results.Ok(new InvoiceRefreshResponse(
                result.Received,
                result.Created,
                result.Updated));
        }

        var fixturePath = Path.Combine(
            environment.ContentRootPath,
            "Features",
            "Invoices",
            "Fixtures",
            "shoprite-invoice-basic.json");

        await using var fixtureStream = File.OpenRead(fixturePath);
        var fixture = await JsonSerializer.DeserializeAsync<AcumaticaInvoiceFixture>(
            fixtureStream,
            SerializerOptions,
            cancellationToken);

        if (fixture is null)
        {
            return Results.Problem("Fixture could not be loaded.");
        }

        var canonical = AcumaticaInvoiceNormalizer.Normalize(
            fixture.Invoice,
            fixture.SupplierGln,
            fixture.StoreDcGln);
        var enriched = await candidateMatcher.MatchAndValidateAsync(canonical, cancellationToken);
        canonical = enriched.Invoice;
        var validation = enriched.Validation;
        var idempotencyKey = BuildIdempotencyKey(canonical);
        var now = DateTimeOffset.UtcNow;

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(candidate => candidate.AcumaticaInvoiceId == canonical.AcumaticaInvoiceId, cancellationToken);
        var created = candidate is null;

        if (candidate is null)
        {
            candidate = new InvoiceCandidateEntity
            {
                Id = Guid.NewGuid(),
                AcumaticaInvoiceId = canonical.AcumaticaInvoiceId,
                InvoiceNumber = canonical.InvoiceNumber,
                CustomerAccount = canonical.CustomerAccount,
                IdempotencyKey = idempotencyKey,
                Status = CandidateStatus(validation, currentStatus: null),
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.InvoiceCandidates.Add(candidate);
        }

        candidate.AcumaticaInvoiceId = canonical.AcumaticaInvoiceId;
        candidate.InvoiceNumber = canonical.InvoiceNumber;
        candidate.CustomerAccount = canonical.CustomerAccount;
        candidate.CustomerLocation = canonical.CustomerLocation;
        candidate.ShopritePurchaseOrderNumber = canonical.ShopritePurchaseOrderNumber;
        candidate.MatchedShopritePurchaseOrderId = enriched.MatchedPurchaseOrderId;
        candidate.SupplierGln = canonical.SupplierGln;
        candidate.StoreDcGln = canonical.StoreDcGln;
        candidate.IdempotencyKey = idempotencyKey;
        candidate.Status = CandidateStatus(validation, candidate.Status);
        candidate.SourceJson = JsonSerializer.Serialize(fixture.Invoice, SerializerOptions);
        candidate.CanonicalJson = JsonSerializer.Serialize(canonical, SerializerOptions);
        candidate.ValidationJson = JsonSerializer.Serialize(validation, SerializerOptions);
        candidate.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new InvoiceRefreshResponse(
            Received: 1,
            Created: created ? 1 : 0,
            Updated: created ? 0 : 1));
    }

    private static async Task<IResult> RevalidateCandidateAsync(
        Guid id,
        PvmDbContext dbContext,
        ShopriteInvoiceCandidateMatcher candidateMatcher,
        CancellationToken cancellationToken)
    {
        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (candidate is null)
        {
            return Results.NotFound(new { id, message = "Invoice candidate not found." });
        }

        var canonical = Deserialize<CanonicalInvoice>(candidate.CanonicalJson);
        if (canonical is null)
        {
            return Results.Problem("Invoice candidate has no canonical invoice payload.");
        }

        var enriched = await candidateMatcher.MatchAndValidateAsync(canonical, cancellationToken);
        canonical = enriched.Invoice;
        var validation = enriched.Validation;
        ApplyMatch(candidate, enriched, DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToSummaryResponse(candidate));
    }

    private static InvoiceCandidateSummaryResponse ToSummaryResponse(InvoiceCandidateEntity candidate)
    {
        var validation = Deserialize<ValidationResult>(candidate.ValidationJson) ?? new ValidationResult([]);

        return new InvoiceCandidateSummaryResponse(
            candidate.Id,
            candidate.InvoiceNumber,
            candidate.CustomerAccount,
            candidate.CustomerLocation,
            candidate.ShopritePurchaseOrderNumber,
            candidate.MatchedShopritePurchaseOrderId,
            PurchaseOrderMatchStatus(candidate),
            candidate.StoreDcGln,
            candidate.Status,
            InvoiceCandidateReadiness.CanSubmit(
                validation,
                candidate.MatchedShopritePurchaseOrderId,
                candidate.Status,
                attemptStatuses: []),
            candidate.UpdatedAt);
    }

    private static InvoiceCandidatePurchaseOrderResponse ToPurchaseOrderResponse(
        ShopritePurchaseOrderEntity order)
        => new(
            order.Id,
            order.PurchaseOrderNumber,
            order.OrderTypeCode,
            order.OrderTypeLabel,
            order.DeliveryGln,
            order.DeliveryLocationCode,
            order.DeliveryLocationName,
            order.DeliveryLocationSource,
            order.Lines.Count,
            order.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new InvoiceCandidatePurchaseOrderLineResponse(
                    line.Id,
                    line.LineNumber,
                    line.Gtin,
                    line.BuyerItemId,
                    line.BuyerItemDescription,
                    line.Description,
                    line.RequestedQuantity))
                .ToArray());

    private static InvoiceSubmissionAttemptResponse ToAttemptResponse(InvoiceSubmissionAttemptEntity attempt)
        => new(
            attempt.Id,
            attempt.InitiatedBy,
            attempt.InitiationMode,
            attempt.Status,
            attempt.ResponseStatusCode,
            attempt.ErrorMessage,
            attempt.FailureClassification,
            attempt.IsRetryEligible,
            attempt.CreatedAt);

    private static string CandidateStatus(ValidationResult validation, string? currentStatus)
    {
        if (currentStatus is "Submitted" or "Rejected" or "Ambiguous" or "Suspended")
        {
            return currentStatus;
        }

        return validation.CanSubmit ? "Ready" : "NeedsReview";
    }

    private static void ApplyMatch(
        InvoiceCandidateEntity candidate,
        ShopriteInvoiceMatchResult match,
        DateTimeOffset updatedAt)
    {
        candidate.ValidationJson = JsonSerializer.Serialize(match.Validation, SerializerOptions);
        candidate.CanonicalJson = JsonSerializer.Serialize(match.Invoice, SerializerOptions);
        candidate.MatchedShopritePurchaseOrderId = match.MatchedPurchaseOrderId;
        candidate.SupplierGln = match.Invoice.SupplierGln;
        candidate.StoreDcGln = match.Invoice.StoreDcGln;
        candidate.IdempotencyKey = BuildIdempotencyKey(match.Invoice);
        candidate.Status = CandidateStatus(match.Validation, candidate.Status);
        candidate.UpdatedAt = updatedAt;
    }

    private static string PurchaseOrderMatchStatus(InvoiceCandidateEntity candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ShopritePurchaseOrderNumber))
        {
            return "MissingPoNumber";
        }

        return candidate.MatchedShopritePurchaseOrderId is null
            ? "Unmatched"
            : "Matched";
    }

    private static string BuildIdempotencyKey(CanonicalInvoice invoice)
        => $"shoprite-vendorinvoice:{invoice.SupplierGln}:{invoice.StoreDcGln}:{invoice.ShopritePurchaseOrderNumber}:{invoice.InvoiceNumber}";

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);

}
