using Microsoft.EntityFrameworkCore;
using Pvm.Infrastructure.Persistence.Entities;

namespace Pvm.Infrastructure.Persistence;

public sealed class PvmDbContext(DbContextOptions<PvmDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceCandidateEntity> InvoiceCandidates => Set<InvoiceCandidateEntity>();

    public DbSet<InvoiceSubmissionAttemptEntity> InvoiceSubmissionAttempts => Set<InvoiceSubmissionAttemptEntity>();

    public DbSet<ShopritePurchaseOrderEntity> ShopritePurchaseOrders => Set<ShopritePurchaseOrderEntity>();

    public DbSet<ShopritePurchaseOrderLineEntity> ShopritePurchaseOrderLines => Set<ShopritePurchaseOrderLineEntity>();

    public DbSet<ShopriteItemMappingEntity> ShopriteItemMappings => Set<ShopriteItemMappingEntity>();

    public DbSet<ShopriteUomMappingEntity> ShopriteUomMappings => Set<ShopriteUomMappingEntity>();

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
    }
}
