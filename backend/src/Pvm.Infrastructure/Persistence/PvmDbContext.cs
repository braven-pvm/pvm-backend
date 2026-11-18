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

    public DbSet<ShopriteDeliveryLocationEntity> ShopriteDeliveryLocations => Set<ShopriteDeliveryLocationEntity>();

    public DbSet<ShopriteTradeItemEntity> ShopriteTradeItems => Set<ShopriteTradeItemEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<AppUserRoleEntity> AppUserRoles => Set<AppUserRoleEntity>();

    public DbSet<AppUserAuditEventEntity> AppUserAuditEvents => Set<AppUserAuditEventEntity>();

    public DbSet<IntegrationOutboxMessageEntity> IntegrationOutboxMessages => Set<IntegrationOutboxMessageEntity>();

    public DbSet<IntegrationMessageDeliveryEntity> IntegrationMessageDeliveries => Set<IntegrationMessageDeliveryEntity>();

    public DbSet<IntegrationRunEntity> IntegrationRuns => Set<IntegrationRunEntity>();

    public DbSet<IntegrationEventInboxEntity> IntegrationEventInbox => Set<IntegrationEventInboxEntity>();

    public DbSet<AutomationPolicyVersionEntity> AutomationPolicyVersions => Set<AutomationPolicyVersionEntity>();

    public DbSet<AutomationDecisionEntity> AutomationDecisions => Set<AutomationDecisionEntity>();

    public DbSet<ExceptionTaskEntity> ExceptionTasks => Set<ExceptionTaskEntity>();

    public DbSet<ExceptionTaskCommentEntity> ExceptionTaskComments => Set<ExceptionTaskCommentEntity>();

    public DbSet<ReportingCustomerEntity> ReportingCustomers => Set<ReportingCustomerEntity>();

    public DbSet<ReportingProductEntity> ReportingProducts => Set<ReportingProductEntity>();

    public DbSet<ReportingSalesRepEntity> ReportingSalesReps => Set<ReportingSalesRepEntity>();

    public DbSet<ReportingWarehouseLocationEntity> ReportingWarehouseLocations
        => Set<ReportingWarehouseLocationEntity>();

    public DbSet<ReportingCustomerClassChannelEntity> ReportingCustomerClassChannels
        => Set<ReportingCustomerClassChannelEntity>();

    public DbSet<SellInLineEntity> SellInLines => Set<SellInLineEntity>();

    public DbSet<SellInReportLine> SellInReportLines => Set<SellInReportLine>();

    public DbSet<SellInExcludedLine> SellInExcludedLines => Set<SellInExcludedLine>();

    public DbSet<SellOutImportEntity> SellOutImports => Set<SellOutImportEntity>();

    public DbSet<SellOutImportIssueEntity> SellOutImportIssues => Set<SellOutImportIssueEntity>();

    public DbSet<RetailStoreEntity> RetailStores => Set<RetailStoreEntity>();

    public DbSet<SellOutMonthEntity> SellOutMonths => Set<SellOutMonthEntity>();

    public DbSet<StoreStockSnapshotEntity> StoreStockSnapshots => Set<StoreStockSnapshotEntity>();

    public DbSet<SellOutProductMapEntity> SellOutProductMaps => Set<SellOutProductMapEntity>();

    public DbSet<ProductGtinEntity> ProductGtins => Set<ProductGtinEntity>();

    public DbSet<MerchandiserCompanyEntity> MerchandiserCompanies => Set<MerchandiserCompanyEntity>();

    public DbSet<MerchandiserAreaEntity> MerchandiserAreas => Set<MerchandiserAreaEntity>();

    public DbSet<MerchandiserLineEntity> MerchandiserLines => Set<MerchandiserLineEntity>();

    public DbSet<MerchandiserMakroStoreEntity> MerchandiserMakroStores => Set<MerchandiserMakroStoreEntity>();

    public DbSet<StockSyncRunEntity> StockSyncRuns => Set<StockSyncRunEntity>();

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

        modelBuilder.Entity<AutomationPolicyVersionEntity>(entity =>
        {
            entity.ToTable("automation_policy_versions");
            entity.HasKey(policy => policy.Id);
            entity.HasIndex(policy => policy.Version).IsUnique();
            entity.Property(policy => policy.Mode).HasMaxLength(32);
            entity.Property(policy => policy.TimeZoneId).HasMaxLength(128);
            entity.Property(policy => policy.CreatedBy).HasMaxLength(320);
            entity.Property(policy => policy.Reason).HasMaxLength(1000);
        });

        modelBuilder.Entity<AutomationDecisionEntity>(entity =>
        {
            entity.ToTable("automation_decisions");
            entity.HasKey(decision => decision.Id);
            entity.HasIndex(decision => new
            {
                decision.InvoiceCandidateId,
                decision.PolicyVersion,
                decision.SourceVersion,
                decision.Outcome
            }).IsUnique();
            entity.HasIndex(decision => decision.CommandId)
                .IsUnique()
                .HasFilter("\"CommandId\" IS NOT NULL");
            entity.HasIndex(decision => decision.EvaluatedAt);
            entity.HasOne<InvoiceCandidateEntity>()
                .WithMany()
                .HasForeignKey(decision => decision.InvoiceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(decision => decision.SourceVersion).HasMaxLength(64);
            entity.Property(decision => decision.Outcome).HasMaxLength(64);
            entity.Property(decision => decision.Summary).HasMaxLength(1000);
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
            entity.Property(order => order.LastAcknowledgementError).HasMaxLength(1000);
            entity.HasIndex(order => order.AcknowledgedAt);
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

        modelBuilder.Entity<ShopriteDeliveryLocationEntity>(entity =>
        {
            entity.ToTable("shoprite_delivery_locations");
            entity.HasKey(location => location.Id);
            entity.HasIndex(location => location.BranchCode).IsUnique();
            entity.Property(location => location.BranchCode).HasMaxLength(32);
            entity.Property(location => location.Gln).HasMaxLength(32);
            entity.Property(location => location.Name).HasMaxLength(256);
            entity.Property(location => location.LocationType)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(location => location.UpdatedBy).HasMaxLength(320);
        });

        modelBuilder.Entity<ShopriteTradeItemEntity>(entity =>
        {
            entity.ToTable("shoprite_trade_items");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new
            {
                item.AcumaticaInventoryId,
                item.AcumaticaUom,
                item.DeliversTo
            }).IsUnique();
            entity.Property(item => item.AcumaticaInventoryId).HasMaxLength(128);
            entity.Property(item => item.AcumaticaUom).HasMaxLength(32);
            entity.Property(item => item.DeliversTo)
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(item => item.Gtin).HasMaxLength(32);
            entity.Property(item => item.ShopritePackSize).HasPrecision(18, 4);
            entity.Property(item => item.ShopriteUom)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.Property(item => item.UpdatedBy).HasMaxLength(320);
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

        modelBuilder.Entity<StockSyncRunEntity>(entity =>
        {
            entity.ToTable("stock_sync_runs");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => run.CompletedAt);
            entity.Property(run => run.Mode).HasMaxLength(16);
            entity.Property(run => run.Status).HasMaxLength(16);
            entity.Property(run => run.Detail).HasMaxLength(2000);
            entity.Property(run => run.LinesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<IntegrationRunEntity>(entity =>
        {
            entity.ToTable("integration_runs");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => run.ScheduleKey)
                .IsUnique()
                .HasFilter("\"ScheduleKey\" IS NOT NULL");
            entity.HasIndex(run => new { run.RunType, run.Status, run.UpdatedAt });
            entity.HasIndex(run => new { run.RunType, run.CursorAfter });
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

        modelBuilder.Entity<IntegrationEventInboxEntity>(entity =>
        {
            entity.ToTable("integration_event_inbox");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new
            {
                item.SourceEnvironment,
                item.CompanyId,
                item.QueryName,
                item.TransactionId
            }).IsUnique();
            entity.HasIndex(item => item.ReceivedAt);
            entity.Property(item => item.SourceEnvironment).HasMaxLength(32);
            entity.Property(item => item.CompanyId).HasMaxLength(128);
            entity.Property(item => item.QueryName).HasMaxLength(256);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.PayloadHash).HasMaxLength(64);
        });

        modelBuilder.Entity<ExceptionTaskEntity>(entity =>
        {
            entity.ToTable("exception_tasks");
            entity.HasKey(task => task.Id);
            entity.HasIndex(task => task.DeduplicationKey).IsUnique();
            entity.HasIndex(task => new { task.Status, task.Severity });
            entity.HasIndex(task => task.InvoiceCandidateId);
            entity.Property(task => task.DeduplicationKey).HasMaxLength(512);
            entity.Property(task => task.Category).HasMaxLength(64);
            entity.Property(task => task.Severity).HasMaxLength(16);
            entity.Property(task => task.Status).HasMaxLength(32);
            entity.Property(task => task.EntityType).HasMaxLength(64);
            entity.Property(task => task.EntityId).HasMaxLength(128);
            entity.Property(task => task.ErrorCode).HasMaxLength(128);
            entity.Property(task => task.Summary).HasMaxLength(1024);
            entity.Property(task => task.FixLocation).HasMaxLength(256);
            entity.Property(task => task.RetryClassification).HasMaxLength(64);
            entity.Property(task => task.Owner).HasMaxLength(320);
            entity.Property(task => task.LatestEvidence).HasMaxLength(2048);
            entity.Property(task => task.DetailsJson).HasColumnType("jsonb");
            entity.Property(task => task.ResolvedBy).HasMaxLength(320);
            entity.Property(task => task.ResolutionReason).HasMaxLength(1024);
        });

        modelBuilder.Entity<ExceptionTaskCommentEntity>(entity =>
        {
            entity.ToTable("exception_task_comments");
            entity.HasKey(comment => comment.Id);
            entity.HasIndex(comment => comment.ExceptionTaskId);
            entity.Property(comment => comment.Actor).HasMaxLength(320);
            entity.Property(comment => comment.Body).HasMaxLength(4096);
            entity
                .HasOne<ExceptionTaskEntity>()
                .WithMany()
                .HasForeignKey(comment => comment.ExceptionTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureReporting(modelBuilder);
        ConfigureSellOut(modelBuilder);
        ConfigureMerchandisers(modelBuilder);
    }

    private static void ConfigureReporting(ModelBuilder modelBuilder)
    {
        const string schema = "reporting";

        modelBuilder.Entity<ReportingCustomerEntity>(entity =>
        {
            entity.ToTable("customers", schema);
            entity.HasKey(customer => customer.CustomerId);
            entity.Property(customer => customer.CustomerId).HasMaxLength(64);
            entity.Property(customer => customer.Name).HasMaxLength(256);
            entity.Property(customer => customer.CustomerClassId).HasMaxLength(64);
            entity.Property(customer => customer.CustomerClassName).HasMaxLength(256);
            entity.Property(customer => customer.Province).HasMaxLength(64);
            entity.Property(customer => customer.CurrentSalesRepId).HasMaxLength(64);
            entity.HasIndex(customer => customer.CustomerClassId);
        });

        modelBuilder.Entity<ReportingProductEntity>(entity =>
        {
            entity.ToTable("products", schema);
            entity.HasKey(product => product.InventoryId);
            entity.Property(product => product.InventoryId).HasMaxLength(64);
            entity.Property(product => product.Description).HasMaxLength(256);
            entity.Property(product => product.ItemClassId).HasMaxLength(64);
            entity.Property(product => product.ItemClassName).HasMaxLength(256);
        });

        modelBuilder.Entity<ReportingSalesRepEntity>(entity =>
        {
            entity.ToTable("sales_reps", schema);
            entity.HasKey(rep => rep.SalesRepId);
            entity.Property(rep => rep.SalesRepId).HasMaxLength(64);
            entity.Property(rep => rep.Name).HasMaxLength(256);
        });

        modelBuilder.Entity<ReportingWarehouseLocationEntity>(entity =>
        {
            entity.ToTable("warehouse_locations", schema);
            entity.HasKey(location => new { location.WarehouseId, location.LocationId });
            entity.Property(location => location.WarehouseId).HasMaxLength(64);
            entity.Property(location => location.LocationId).HasMaxLength(64);
            entity.Property(location => location.WarehouseName).HasMaxLength(256);
            entity.Property(location => location.LocationName).HasMaxLength(256);
        });

        modelBuilder.Entity<ReportingCustomerClassChannelEntity>(entity =>
        {
            entity.ToTable("customer_class_channels", schema);
            entity.HasKey(channel => channel.CustomerClassId);
            entity.Property(channel => channel.CustomerClassId).HasMaxLength(64);
            entity.Property(channel => channel.Channel).HasMaxLength(64);
            entity.Property(channel => channel.UpdatedBy).HasMaxLength(320);
        });

        modelBuilder.Entity<SellInLineEntity>(entity =>
        {
            entity.ToTable("sell_in_lines", schema);
            entity.HasKey(line => line.Id);
            entity.HasIndex(line => new { line.DocumentType, line.ReferenceNumber, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.FinancialYear, line.FinancialMonth });
            entity.HasIndex(line => line.CustomerId);
            entity.HasIndex(line => line.InventoryId);
            entity.HasIndex(line => line.SalesRepId);
            entity.Property(line => line.DocumentType).HasConversion<string>().HasMaxLength(16);
            entity.Property(line => line.ReferenceNumber).HasMaxLength(32);
            entity.Property(line => line.PostPeriod).HasMaxLength(8);
            entity.Property(line => line.CustomerId).HasMaxLength(64);
            entity.Property(line => line.SalesRepId).HasMaxLength(64);
            entity.Property(line => line.InventoryId).HasMaxLength(64);
            entity.Property(line => line.WarehouseId).HasMaxLength(64);
            entity.Property(line => line.LocationId).HasMaxLength(64);
            entity.Property(line => line.Uom).HasMaxLength(16);
            entity.Property(line => line.Quantity).HasPrecision(18, 4);
            entity.Property(line => line.BaseQuantity).HasPrecision(18, 4);
            entity.Property(line => line.Amount).HasPrecision(18, 2);
        });

        // Views, made in the migration AddSellInWorkbookRules. Their SQL is in SellInReportViews.
        modelBuilder.Entity<SellInReportLine>(entity =>
        {
            entity.ToView(SellInReportViews.ReportLinesView, schema);
            entity.HasNoKey();
            entity.Property(line => line.DocumentType).HasConversion<string>();
        });

        modelBuilder.Entity<SellInExcludedLine>(entity =>
        {
            entity.ToView(SellInReportViews.ExcludedLinesView, schema);
            entity.HasNoKey();
            entity.Property(line => line.DocumentType).HasConversion<string>();
        });
    }

    private static void ConfigureMerchandisers(ModelBuilder modelBuilder)
    {
        const string schema = "reporting";

        modelBuilder.Entity<MerchandiserCompanyEntity>(entity =>
        {
            entity.ToTable("merchandiser_companies", schema);
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).HasMaxLength(128);
            entity.Property(company => company.StatementTitle).HasMaxLength(128);
            entity.Property(company => company.PaymentKind).HasMaxLength(16);
            entity.Property(company => company.PaymentRatePercent).HasPrecision(7, 4);
            entity.Property(company => company.RetainerAmount).HasPrecision(18, 2);
            entity.Property(company => company.UpdatedBy).HasMaxLength(320);
            entity.HasIndex(company => new { company.Name, company.ValidFrom }).IsUnique();
        });

        modelBuilder.Entity<MerchandiserAreaEntity>(entity =>
        {
            entity.ToTable("merchandiser_areas", schema);
            entity.HasKey(area => area.Id);
            entity.Property(area => area.Name).HasMaxLength(128);
            entity.Property(area => area.CommissionRatePercent).HasPrecision(7, 4);
            entity.HasIndex(area => new { area.CompanyId, area.Order });
            entity.HasOne<MerchandiserCompanyEntity>().WithMany().HasForeignKey(area => area.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchandiserLineEntity>(entity =>
        {
            entity.ToTable("merchandiser_lines", schema);
            entity.HasKey(line => line.Id);
            entity.Property(line => line.Label).HasMaxLength(128);
            entity.Property(line => line.CustomerClassId).HasMaxLength(64);
            entity.HasIndex(line => new { line.AreaId, line.Order });
            entity.HasOne<MerchandiserAreaEntity>().WithMany().HasForeignKey(line => line.AreaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchandiserMakroStoreEntity>(entity =>
        {
            entity.ToTable("merchandiser_makro_stores", schema);
            entity.HasKey(link => new { link.AreaId, link.StoreId });
            entity.HasOne<MerchandiserAreaEntity>().WithMany().HasForeignKey(link => link.AreaId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailStoreEntity>().WithMany().HasForeignKey(link => link.StoreId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSellOut(ModelBuilder modelBuilder)
    {
        const string schema = "reporting";

        modelBuilder.Entity<SellOutImportEntity>(entity =>
        {
            entity.ToTable("sell_out_imports", schema);
            entity.HasKey(import => import.Id);
            entity.Property(import => import.Source).HasMaxLength(64);
            entity.Property(import => import.FileName).HasMaxLength(260);
            entity.Property(import => import.FileSha256).HasMaxLength(64);
            entity.Property(import => import.ReceivedBy).HasMaxLength(320);
            entity.Property(import => import.Status).HasMaxLength(16);
            entity.Property(import => import.StatusReason).HasMaxLength(1000);
            entity.Property(import => import.ArchivePath).HasMaxLength(512);
            // A file loads once. A preview or a refusal of the same file can repeat.
            entity.HasIndex(import => import.FileSha256)
                .IsUnique()
                .HasFilter($"\"Status\" = '{SellOutImportStatuses.Loaded}'");
            entity.HasIndex(import => new { import.Source, import.CreatedAt });
        });

        modelBuilder.Entity<SellOutImportIssueEntity>(entity =>
        {
            entity.ToTable("sell_out_import_issues", schema);
            entity.HasKey(issue => issue.Id);
            entity.Property(issue => issue.Reason).HasMaxLength(200);
            entity.Property(issue => issue.RawKey).HasMaxLength(500);
            entity.HasIndex(issue => issue.ImportId);
            entity.HasOne<SellOutImportEntity>().WithMany().HasForeignKey(issue => issue.ImportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RetailStoreEntity>(entity =>
        {
            entity.ToTable("retail_stores", schema);
            entity.HasKey(store => store.Id);
            entity.Property(store => store.Retailer).HasMaxLength(64);
            entity.Property(store => store.StoreCode).HasMaxLength(64);
            entity.Property(store => store.Name).HasMaxLength(256);
            entity.Property(store => store.Brand).HasMaxLength(64);
            entity.Property(store => store.Region).HasMaxLength(128);
            entity.Property(store => store.Province).HasMaxLength(64);
            entity.Property(store => store.Gln).HasMaxLength(13);
            entity.Property(store => store.AcumaticaCustomerId).HasMaxLength(64);
            entity.HasIndex(store => new { store.Retailer, store.StoreCode }).IsUnique();
        });

        modelBuilder.Entity<SellOutMonthEntity>(entity =>
        {
            entity.ToTable("sell_out_months", schema);
            entity.HasKey(month => month.Id);
            entity.Property(month => month.Source).HasMaxLength(64);
            entity.Property(month => month.InventoryId).HasMaxLength(64);
            entity.Property(month => month.SourceProductCode).HasMaxLength(64);
            entity.Property(month => month.Gtin).HasMaxLength(14);
            entity.Property(month => month.Units).HasPrecision(18, 4);
            entity.Property(month => month.UnitsEach).HasPrecision(18, 4);
            entity.Property(month => month.ValueExclVat).HasPrecision(18, 2);
            entity.HasIndex(month => new { month.Source, month.StoreId, month.SourceProductCode, month.Year, month.Month }).IsUnique();
            entity.HasIndex(month => new { month.Source, month.Year, month.Month });
            entity.HasIndex(month => new { month.FinancialYear, month.FinancialMonth });
            entity.HasIndex(month => month.InventoryId);
            entity.HasIndex(month => month.ImportId);
            entity.HasOne<RetailStoreEntity>().WithMany().HasForeignKey(month => month.StoreId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreStockSnapshotEntity>(entity =>
        {
            entity.ToTable("store_stock_snapshots", schema);
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Source).HasMaxLength(64);
            entity.Property(snapshot => snapshot.InventoryId).HasMaxLength(64);
            entity.Property(snapshot => snapshot.SourceProductCode).HasMaxLength(64);
            entity.Property(snapshot => snapshot.Gtin).HasMaxLength(14);
            entity.Property(snapshot => snapshot.StockOnHand).HasPrecision(18, 4);
            entity.Property(snapshot => snapshot.StockOnOrder).HasPrecision(18, 4);
            entity.Property(snapshot => snapshot.StockInTransit).HasPrecision(18, 4);
            entity.Property(snapshot => snapshot.StockValue).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.WeeksOfStock).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.DcStock).HasPrecision(18, 4);
            entity.HasIndex(snapshot => new { snapshot.Source, snapshot.StoreId, snapshot.SourceProductCode, snapshot.SnapshotDate }).IsUnique();
            entity.HasIndex(snapshot => snapshot.ImportId);
            entity.HasOne<RetailStoreEntity>().WithMany().HasForeignKey(snapshot => snapshot.StoreId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SellOutProductMapEntity>(entity =>
        {
            entity.ToTable("sell_out_product_maps", schema);
            entity.HasKey(map => new { map.Source, map.SourceProductCode });
            entity.Property(map => map.Source).HasMaxLength(64);
            entity.Property(map => map.SourceProductCode).HasMaxLength(64);
            entity.Property(map => map.InventoryId).HasMaxLength(64);
            entity.Property(map => map.UnitsPerEach).HasPrecision(18, 4);
            entity.Property(map => map.Method).HasMaxLength(32);
            entity.Property(map => map.MappedBy).HasMaxLength(320);
        });

        modelBuilder.Entity<ProductGtinEntity>(entity =>
        {
            entity.ToTable("product_gtins", schema);
            entity.HasKey(gtin => gtin.Gtin);
            entity.Property(gtin => gtin.Gtin).HasMaxLength(14);
            entity.Property(gtin => gtin.InventoryId).HasMaxLength(64);
            entity.Property(gtin => gtin.Uom).HasMaxLength(16);
            entity.HasIndex(gtin => gtin.InventoryId);
        });
    }

}
