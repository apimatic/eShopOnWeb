using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperFinixIdentityConfiguration : IEntityTypeConfiguration<ShopperFinixIdentity>
{
    public void Configure(EntityTypeBuilder<ShopperFinixIdentity> builder)
    {
        builder.HasKey(i => i.ShopperId);
        builder.Property(i => i.ShopperId).HasMaxLength(256);
        builder.Property(i => i.IdentityId).HasMaxLength(128);
    }
}

public class SavedCardClaimConfiguration : IEntityTypeConfiguration<SavedCardClaim>
{
    public void Configure(EntityTypeBuilder<SavedCardClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);
        builder.Property(c => c.ClaimKey).HasMaxLength(512);
        builder.Property(c => c.PaymentInstrumentId).HasMaxLength(128);
    }
}

public class OrderOperationClaimConfiguration : IEntityTypeConfiguration<OrderOperationClaim>
{
    public void Configure(EntityTypeBuilder<OrderOperationClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);
        builder.Property(c => c.ClaimKey).HasMaxLength(64);
        builder.Property(c => c.Operation).HasMaxLength(32);
    }
}

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasKey(r => r.RefundKey);
        builder.Property(r => r.RefundKey).HasMaxLength(512);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(r => r.RefundId).HasMaxLength(128);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.FailureCode).HasMaxLength(128);
        builder.Property(r => r.FailureMessage).HasMaxLength(1024);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.RefundId);
    }
}

public class RefundLockConfiguration : IEntityTypeConfiguration<RefundLock>
{
    public void Configure(EntityTypeBuilder<RefundLock> builder)
    {
        builder.HasKey(l => l.OrderId);
        builder.Property(l => l.OrderId).ValueGeneratedNever();
    }
}
