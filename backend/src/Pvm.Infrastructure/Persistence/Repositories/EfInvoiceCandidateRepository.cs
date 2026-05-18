using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pvm.Application.Submissions;
using Pvm.Domain.Invoices;
using Pvm.Domain.Validation;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence.Repositories;

public sealed class EfInvoiceCandidateRepository(PvmDbContext dbContext) : IInvoiceCandidateRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<CanonicalInvoice?> GetCanonicalInvoiceAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var canonicalJson = await dbContext.InvoiceCandidates
            .Where(candidate => candidate.Id == invoiceCandidateId)
            .Select(candidate => candidate.CanonicalJson)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(canonicalJson)
            ? null
            : JsonSerializer.Deserialize<CanonicalInvoice>(canonicalJson, SerializerOptions);
    }

    public async Task<ValidationResult> GetValidationResultAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
    {
        var validationJson = await dbContext.InvoiceCandidates
            .Where(candidate => candidate.Id == invoiceCandidateId)
            .Select(candidate => candidate.ValidationJson)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(validationJson)
            ? new ValidationResult([])
            : JsonSerializer.Deserialize<ValidationResult>(validationJson, SerializerOptions) ?? new ValidationResult([]);
    }

    public Task<bool> HasUnresolvedAmbiguousSubmissionAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
        => dbContext.InvoiceSubmissionAttempts.AnyAsync(
            attempt => attempt.InvoiceCandidateId == invoiceCandidateId
                && attempt.Status == "Ambiguous",
            cancellationToken);

    public Task<bool> HasSuccessfulSubmissionAsync(
        Guid invoiceCandidateId,
        CancellationToken cancellationToken)
        => dbContext.InvoiceSubmissionAttempts.AnyAsync(
            attempt => attempt.InvoiceCandidateId == invoiceCandidateId
                && attempt.Status == "Submitted",
            cancellationToken);

    public async Task RecordAttemptAsync(
        Guid invoiceCandidateId,
        string initiatedBy,
        string initiationMode,
        string xml,
        ShopriteInvoiceResponse response,
        CancellationToken cancellationToken)
    {
        var status = response.IsAmbiguous
            ? "Ambiguous"
            : response.Success ? "Submitted" : "Failed";

        dbContext.InvoiceSubmissionAttempts.Add(new InvoiceSubmissionAttemptEntity
        {
            Id = Guid.NewGuid(),
            InvoiceCandidateId = invoiceCandidateId,
            InitiatedBy = initiatedBy,
            InitiationMode = initiationMode,
            Status = status,
            RequestPayload = xml,
            RequestPayloadHash = Sha256(xml),
            ResponseStatusCode = response.StatusCode,
            ResponsePayload = response.Body,
            ResponsePayloadHash = Sha256(response.Body),
            ErrorMessage = response.Success ? null : response.Body,
            FailureClassification = response.IsAmbiguous ? "ambiguous-outcome" : response.Success ? null : "shoprite-rejected",
            RecommendedFixLocation = response.Success ? null : "Shoprite",
            IsRetryEligible = !response.Success && !response.IsAmbiguous,
            ResponsibleRole = response.Success ? null : "admin",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var candidate = await dbContext.InvoiceCandidates.FindAsync([invoiceCandidateId], cancellationToken);
        if (candidate is not null)
        {
            candidate.Status = status;
            candidate.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
