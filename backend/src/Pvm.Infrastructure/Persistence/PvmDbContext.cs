using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceCandidateEntity> InvoiceCandidates => Set<InvoiceCandidateEntity>();

    public DbSet<InvoiceSubmissionAttemptEntity> InvoiceSubmissionAttempts => Set<InvoiceSubmissionAttemptEntity>();

    public DbSet<SubmissionOperationEntity> SubmissionOperations => Set<SubmissionOperationEntity>();

    public DbSet<PayloadArchiveEntity> PayloadArchives => Set<PayloadArchiveEntity>();

    public DbSet<SubmissionOperationTransitionEntity> SubmissionOperationTransitions
        => Set<SubmissionOperationTransitionEntity>();

    public DbSet<ShopritePurchaseOrderEntity> ShopritePurchaseOrders => Set<ShopritePurchaseOrderEntity>();

    public DbSet<ShopritePurchaseOrderLineEntity> ShopritePurchaseOrderLines => Set<ShopritePurchaseOrderLineEntity>();

    public DbSet<ShopriteItemMappingEntity> ShopriteItemMappings => Set<ShopriteItemMappingEntity>();

    public DbSet<ShopriteUomMappingEntity> ShopriteUomMappings => Set<ShopriteUomMappingEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<AppUserRoleEntity> AppUserRoles => Set<AppUserRoleEntity>();

    public DbSet<AppUserAuditEventEntity> AppUserAuditEvents => Set<AppUserAuditEventEntity>();

    public DbSet<IntegrationOutboxMessageEntity> IntegrationOutboxMessages => Set<IntegrationOutboxMessageEntity>();

    public DbSet<IntegrationMessageDeliveryEntity> IntegrationMessageDeliveries => Set<IntegrationMessageDeliveryEntity>();

    public DbSet<IntegrationRunEntity> IntegrationRuns => Set<IntegrationRunEntity>();

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
            entity
                .HasOne<ShopritePurchaseOrderEntity>()
                .WithMany()
                .HasForeignKey(candidate => candidate.MatchedShopritePurchaseOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InvoiceSubmissionAttemptEntity>(entity =>
        {
            entity.ToTable("invoice_submission_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InvoiceCandidateId);
            entity.HasIndex(attempt => attempt.SubmissionOperationId)
                .IsUnique()
                .HasFilter("\"SubmissionOperationId\" IS NOT NULL");
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

        modelBuilder.Entity<SubmissionOperationEntity>(entity =>
        {
            entity.ToTable("submission_operations");
            entity.HasKey(operation => operation.Id);
            entity.HasIndex(operation => operation.CommandId).IsUnique();
            entity.HasIndex(operation => new { operation.InvoiceCandidateId, operation.Generation }).IsUnique();
            entity.HasIndex(operation => new { operation.Status, operation.SendingStartedAt });
            entity.HasIndex(operation => operation.InvoiceCandidateId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Pending', 'Sending', 'Submitted', 'Ambiguous')");
            entity
                .HasOne<InvoiceCandidateEntity>()
                .WithMany()
                .HasForeignKey(operation => operation.InvoiceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(operation => operation.IdempotencyKey).HasMaxLength(512);
            entity.Property(operation => operation.Status).HasMaxLength(64);
            entity.Property(operation => operation.InitiatedBy).HasMaxLength(320);
            entity.Property(operation => operation.InitiationMode).HasMaxLength(64);
            entity.Property(operation => operation.SourceVersion).HasMaxLength(128);
            entity.Property(operation => operation.FrozenSourceJson).HasColumnType("jsonb");
            entity.Property(operation => operation.FrozenCanonicalJson).HasColumnType("jsonb");
            entity.Property(operation => operation.RequestPayload).HasColumnType("text");
            entity.Property(operation => operation.RequestPayloadHash).HasMaxLength(128);
            entity.Property(operation => operation.ResponsePayload).HasColumnType("text");
            entity.Property(operation => operation.ResponsePayloadHash).HasMaxLength(128);
            entity.Property(operation => operation.FailureClassification).HasMaxLength(128);
            entity
                .HasMany(operation => operation.PayloadArchives)
                .WithOne()
                .HasForeignKey(payload => payload.SubmissionOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayloadArchiveEntity>(entity =>
        {
            entity.ToTable("payload_archives");
            entity.HasKey(payload => payload.Id);
            entity.HasIndex(payload => new { payload.SubmissionOperationId, payload.Kind }).IsUnique();
            entity.HasIndex(payload => payload.InvoiceCandidateId);
            entity.Property(payload => payload.Kind).HasMaxLength(64);
            entity.Property(payload => payload.Location).HasMaxLength(1024);
            entity.Property(payload => payload.Sha256Hash).HasMaxLength(64);
            entity.Property(payload => payload.ContentType).HasMaxLength(128);
            entity
                .HasOne<InvoiceCandidateEntity>()
                .WithMany()
                .HasForeignKey(payload => payload.InvoiceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubmissionOperationTransitionEntity>(entity =>
        {
            entity.ToTable("submission_operation_transitions");
            entity.HasKey(transition => transition.Id);
            entity.HasIndex(transition => new
            {
                transition.SubmissionOperationId,
                transition.CreatedAt
            });
            entity.HasIndex(transition => new
            {
                transition.InvoiceCandidateId,
                transition.CreatedAt
            });
            entity.Property(transition => transition.Actor).HasMaxLength(320);
            entity.Property(transition => transition.Mode).HasMaxLength(64);
            entity.Property(transition => transition.PreviousState).HasMaxLength(64);
            entity.Property(transition => transition.NewState).HasMaxLength(64);
            entity.Property(transition => transition.Reason).HasMaxLength(256);
            entity.Property(transition => transition.SourceVersion).HasMaxLength(128);
            entity.Property(transition => transition.PayloadHash).HasMaxLength(64);
            entity
                .HasOne<SubmissionOperationEntity>()
                .WithMany()
                .HasForeignKey(transition => transition.SubmissionOperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity
                .HasOne<InvoiceCandidateEntity>()
                .WithMany()
                .HasForeignKey(transition => transition.InvoiceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShopritePurchaseOrderEntity>(entity =>
        {
            entity.ToTable("shoprite_purchase_orders");
            entity.HasKey(order => order.Id);
            entity.HasIndex(order => order.PurchaseOrderNumber).IsUnique();
            entity.HasIndex(order => order.DeliveryGln);
            entity.Property(order => order.PurchaseOrderNumber).HasMaxLength(128);
            entity.Property(order => order.OrderHeaderId).HasMaxLength(128);
            entity.Property(order => order.OrderTypeCode).HasMaxLength(32);
            entity.Property(order => order.OrderTypeLabel).HasMaxLength(128);
            entity.Property(order => order.SupplierGln).HasMaxLength(32);
            entity.Property(order => order.BuyerGln).HasMaxLength(32);
            entity.Property(order => order.DeliveryGln).HasMaxLength(32);
            entity.Property(order => order.DeliveryLocationCode).HasMaxLength(128);
            entity.Property(order => order.DeliveryLocationName).HasMaxLength(512);
            entity.Property(order => order.DeliveryLocationSource).HasMaxLength(64);
            entity.Property(order => order.CurrencyCode).HasMaxLength(8);
            entity.Property(order => order.SourceEnvironment).HasMaxLength(32);
            entity.Property(order => order.SourceEndpoint).HasMaxLength(128);
            entity.Property(order => order.PayloadHash).HasMaxLength(128);
            entity.Property(order => order.RawOrderJson).HasColumnType("jsonb");
            entity
                .HasMany(order => order.Lines)
                .WithOne()
                .HasForeignKey(line => line.ShopritePurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShopritePurchaseOrderLineEntity>(entity =>
        {
            entity.ToTable("shoprite_purchase_order_lines");
            entity.HasKey(line => line.Id);
            entity.HasIndex(line => new { line.ShopritePurchaseOrderId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => line.Gtin);
            entity.Property(line => line.Gtin).HasMaxLength(32);
            entity.Property(line => line.BuyerItemId).HasMaxLength(128);
            entity.Property(line => line.BuyerItemDescription).HasMaxLength(512);
            entity.Property(line => line.SupplierItemId).HasMaxLength(128);
            entity.Property(line => line.Description).HasMaxLength(512);
            entity.Property(line => line.MeasurementUnitCode).HasMaxLength(32);
        });

        modelBuilder.Entity<ShopriteItemMappingEntity>(entity =>
        {
            entity.ToTable("shoprite_item_mappings");
            entity.HasKey(mapping => mapping.Id);
            entity.HasIndex(mapping => new
            {
                mapping.AcumaticaInventoryId,
                mapping.ShopriteBuyerItemId
            }).IsUnique();
            entity.Property(mapping => mapping.AcumaticaInventoryId).HasMaxLength(128);
            entity.Property(mapping => mapping.ShopriteBuyerItemId).HasMaxLength(128);
            entity.Property(mapping => mapping.Gtin).HasMaxLength(32);
            entity.Property(mapping => mapping.UpdatedBy).HasMaxLength(320);
        });

        modelBuilder.Entity<ShopriteUomMappingEntity>(entity =>
        {
            entity.ToTable("shoprite_uom_mappings");
            entity.HasKey(mapping => mapping.Id);
            entity.HasIndex(mapping => new
            {
                mapping.AcumaticaInventoryId,
                mapping.AcumaticaUom
            }).IsUnique();
            entity.Property(mapping => mapping.AcumaticaInventoryId).HasMaxLength(128);
            entity.Property(mapping => mapping.AcumaticaUom).HasMaxLength(32);
            entity.Property(mapping => mapping.ShopriteUom)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(mapping => mapping.UpdatedBy).HasMaxLength(320);
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

        modelBuilder.Entity<IntegrationOutboxMessageEntity>(entity =>
        {
            entity.ToTable("integration_outbox_messages");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.Status, message.AvailableAt });
            entity.HasIndex(message => message.CorrelationId);
            entity.Property(message => message.QueueName).HasMaxLength(128);
            entity.Property(message => message.MessageType).HasMaxLength(128);
            entity.Property(message => message.PayloadJson).HasColumnType("jsonb");
            entity.Property(message => message.CorrelationId).HasMaxLength(128);
            entity.Property(message => message.CausationId).HasMaxLength(128);
            entity.Property(message => message.Status).HasMaxLength(32);
            entity.Property(message => message.LastErrorCode).HasMaxLength(128);
            entity.Property(message => message.LastErrorSummary).HasMaxLength(1024);
        });

        modelBuilder.Entity<IntegrationMessageDeliveryEntity>(entity =>
        {
            entity.ToTable("integration_message_deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new { delivery.QueueName, delivery.MessageId }).IsUnique();
            entity.HasIndex(delivery => new { delivery.Status, delivery.UpdatedAt });
            entity.HasIndex(delivery => delivery.CorrelationId);
            entity.Property(delivery => delivery.QueueName).HasMaxLength(128);
            entity.Property(delivery => delivery.MessageId).HasMaxLength(128);
            entity.Property(delivery => delivery.MessageType).HasMaxLength(128);
            entity.Property(delivery => delivery.CorrelationId).HasMaxLength(128);
            entity.Property(delivery => delivery.CausationId).HasMaxLength(128);
            entity.Property(delivery => delivery.Status).HasMaxLength(32);
            entity.Property(delivery => delivery.ErrorCode).HasMaxLength(128);
            entity.Property(delivery => delivery.ErrorSummary).HasMaxLength(1024);
            entity.Property(delivery => delivery.DeadLetterReason).HasMaxLength(128);
        });

        modelBuilder.Entity<IntegrationRunEntity>(entity =>
        {
            entity.ToTable("integration_runs");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => run.ScheduleKey)
                .IsUnique()
                .HasFilter("\"ScheduleKey\" IS NOT NULL");
            entity.HasIndex(run => new { run.RunType, run.Status, run.UpdatedAt });
            entity.HasIndex(run => run.CorrelationId);
            entity.Property(run => run.RunType).HasMaxLength(128);
            entity.Property(run => run.Trigger).HasMaxLength(64);
            entity.Property(run => run.InitiatedBy).HasMaxLength(320);
            entity.Property(run => run.EnvironmentName).HasMaxLength(32);
            entity.Property(run => run.CorrelationId).HasMaxLength(128);
            entity.Property(run => run.ScheduleKey).HasMaxLength(128);
            entity.Property(run => run.Status).HasMaxLength(32);
            entity.Property(run => run.ErrorCode).HasMaxLength(128);
            entity.Property(run => run.ErrorSummary).HasMaxLength(1024);
        });
    }
}
