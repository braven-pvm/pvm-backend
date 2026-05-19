using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceCandidateEntity> InvoiceCandidates => Set<InvoiceCandidateEntity>();

    public DbSet<InvoiceSubmissionAttemptEntity> InvoiceSubmissionAttempts => Set<InvoiceSubmissionAttemptEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<AppUserRoleEntity> AppUserRoles => Set<AppUserRoleEntity>();

    public DbSet<AppUserAuditEventEntity> AppUserAuditEvents => Set<AppUserAuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoiceCandidateEntity>(entity =>
        {
            entity.ToTable("invoice_candidates");
            entity.HasKey(candidate => candidate.Id);
            entity.HasIndex(candidate => candidate.IdempotencyKey).IsUnique();
            entity.Property(candidate => candidate.IdempotencyKey).HasMaxLength(512);
            entity.Property(candidate => candidate.Status).HasMaxLength(64);
            entity.Property(candidate => candidate.SourceJson).HasColumnType("jsonb");
            entity.Property(candidate => candidate.CanonicalJson).HasColumnType("jsonb");
            entity.Property(candidate => candidate.ValidationJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<InvoiceSubmissionAttemptEntity>(entity =>
        {
            entity.ToTable("invoice_submission_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InvoiceCandidateId);
            entity
                .HasOne<InvoiceCandidateEntity>()
                .WithMany()
                .HasForeignKey(attempt => attempt.InvoiceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(attempt => attempt.Status).HasMaxLength(64);
            entity.Property(attempt => attempt.RequestPayload).HasColumnType("text");
            entity.Property(attempt => attempt.ResponsePayload).HasColumnType("text");
            entity.Property(attempt => attempt.FailureClassification).HasMaxLength(128);
            entity.Property(attempt => attempt.RecommendedFixLocation).HasMaxLength(128);
            entity.Property(attempt => attempt.ResponsibleRole).HasMaxLength(128);
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });
        });

        modelBuilder.Entity<AppUserEntity>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasIndex(user => user.EntraObjectId).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.EntraObjectId).HasMaxLength(128);
            entity.Property(user => user.DisplayName).HasMaxLength(256);
            entity.Property(user => user.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<AppUserRoleEntity>(entity =>
        {
            entity.ToTable("app_user_roles");
            entity.HasKey(role => role.Id);
            entity.HasIndex(role => new { role.AppUserId, role.Role }).IsUnique();
            entity.Property(role => role.Role).HasMaxLength(64);
            entity
                .HasOne<AppUserEntity>()
                .WithMany()
                .HasForeignKey(role => role.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne<AppUserEntity>()
                .WithMany()
                .HasForeignKey(role => role.GrantedByAppUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUserAuditEventEntity>(entity =>
        {
            entity.ToTable("app_user_audit_events");
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => audit.TargetAppUserId);
            entity.Property(audit => audit.Action).HasMaxLength(128);
            entity.Property(audit => audit.BeforeJson).HasColumnType("jsonb");
            entity.Property(audit => audit.AfterJson).HasColumnType("jsonb");
            entity
                .HasOne<AppUserEntity>()
                .WithMany()
                .HasForeignKey(audit => audit.ActorAppUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity
                .HasOne<AppUserEntity>()
                .WithMany()
                .HasForeignKey(audit => audit.TargetAppUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
