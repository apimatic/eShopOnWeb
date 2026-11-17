using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.AuthorizationIdempotencyId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.FinixFeeCents).HasPrecision(18, 4);
        builder.Property(p => p.NetProceedsCents).HasPrecision(18, 4);
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.FinixTransferId);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PublicId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(r => r.PublicId).IsUnique();
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixTransferId);
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.PublicId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.FinixIdentityId).IsRequired().HasMaxLength(64);
        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.HasIndex(c => c.BuyerId);
    }
}

public class FinixBuyerIdentityConfiguration : IEntityTypeConfiguration<FinixBuyerIdentity>
{
    public void Configure(EntityTypeBuilder<FinixBuyerIdentity> builder)
    {
        builder.Property(i => i.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(i => i.FinixIdentityId).IsRequired().HasMaxLength(64);
        builder.HasIndex(i => i.BuyerId).IsUnique();
    }
}
