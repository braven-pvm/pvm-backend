using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Api.Features.Invoices.Models;
using Pvm.Application.Acumatica;
using Pvm.Application.Shoprite;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices");

        group.MapGet("/candidates", ListCandidatesAsync);
        group.MapGet("/candidates/{id:guid}", GetCandidateAsync);
        group.MapPost("/refresh", RefreshCandidatesAsync);
        group.MapPost("/{id:guid}/revalidate", RevalidateCandidateAsync);

        return app;
    }

    private static async Task<IResult> ListCandidatesAsync(
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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

        var source = Deserialize<object>(candidate.SourceJson);
        var canonical = Deserialize<CanonicalInvoice>(candidate.CanonicalJson);
        var validation = Deserialize<ValidationResult>(candidate.ValidationJson) ?? new ValidationResult([]);
        var generatedXml = canonical is null ? null : ShopriteInvoiceXmlGenerator.Generate(canonical);

        return Results.Ok(new InvoiceCandidateDetailResponse(
            candidate.Id,
            candidate.Status,
            CanSubmit(validation, attempts),
            source,
            canonical,
            validation,
            generatedXml,
            attempts.Select(ToAttemptResponse).ToArray()));
    }

    private static async Task<IResult> RefreshCandidatesAsync(
        IWebHostEnvironment environment,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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
        var validation = ShopriteInvoiceValidator.Validate(canonical, ShopriteValidationEnvironment.Qa);
        var idempotencyKey = $"shoprite-vendorinvoice:{canonical.SupplierGln}:{canonical.StoreDcGln}:{canonical.InvoiceNumber}";
        var now = DateTimeOffset.UtcNow;

        var candidate = await dbContext.InvoiceCandidates
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == idempotencyKey, cancellationToken);

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
        candidate.SupplierGln = canonical.SupplierGln;
        candidate.StoreDcGln = canonical.StoreDcGln;
        candidate.Status = CandidateStatus(validation, candidate.Status);
        candidate.SourceJson = JsonSerializer.Serialize(fixture.Invoice, SerializerOptions);
        candidate.CanonicalJson = JsonSerializer.Serialize(canonical, SerializerOptions);
        candidate.ValidationJson = JsonSerializer.Serialize(validation, SerializerOptions);
        candidate.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToSummaryResponse(candidate));
    }

    private static async Task<IResult> RevalidateCandidateAsync(
        Guid id,
        PvmDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

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

        var validation = ShopriteInvoiceValidator.Validate(canonical, ShopriteValidationEnvironment.Qa);
        candidate.ValidationJson = JsonSerializer.Serialize(validation, SerializerOptions);
        candidate.Status = CandidateStatus(validation, candidate.Status);
        candidate.UpdatedAt = DateTimeOffset.UtcNow;

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
            candidate.StoreDcGln,
            candidate.Status,
            validation.CanSubmit && candidate.Status is not "Submitted" and not "Ambiguous",
            candidate.UpdatedAt);
    }

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

    private static bool CanSubmit(
        ValidationResult validation,
        IReadOnlyCollection<InvoiceSubmissionAttemptEntity> attempts)
        => validation.CanSubmit
            && !attempts.Any(attempt => attempt.Status is "Submitted" or "Ambiguous");

    private static string CandidateStatus(ValidationResult validation, string? currentStatus)
    {
        if (currentStatus is "Submitted" or "Ambiguous")
        {
            return currentStatus;
        }

        return validation.CanSubmit ? "Ready" : "NeedsReview";
    }

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);
}
