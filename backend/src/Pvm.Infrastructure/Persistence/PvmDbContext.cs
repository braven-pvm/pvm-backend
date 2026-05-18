using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceCandidateEntity> InvoiceCandidates => Set<InvoiceCandidateEntity>();

    public DbSet<InvoiceSubmissionAttemptEntity> InvoiceSubmissionAttempts => Set<InvoiceSubmissionAttemptEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoiceCandidateEntity>(entity =>
        {
            entity.ToTable("invoice_candidates");
            entity.HasKey(candidate => candidate.Id);
            entity.HasIndex(candidate => candidate.IdempotencyKey).IsUnique();
            entity.Property(candidate => candidate.IdempotencyKey).HasMaxLength(512);
            entity.Property(candidate => candidate.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<InvoiceSubmissionAttemptEntity>(entity =>
        {
            entity.ToTable("invoice_submission_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InvoiceCandidateId);
            entity.Property(attempt => attempt.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });
        });
    }
}
