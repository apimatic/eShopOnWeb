using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.FulfilmentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixRefundId);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.FinixPaymentInstrumentId).IsRequired().HasMaxLength(128);
        builder.HasIndex(m => new { m.BuyerId, m.FinixPaymentInstrumentId }).IsUnique();
        builder.HasIndex(m => new { m.BuyerId, m.Fingerprint });
    }
}

public class FinixBuyerIdentityConfiguration : IEntityTypeConfiguration<FinixBuyerIdentity>
{
    public void Configure(EntityTypeBuilder<FinixBuyerIdentity> builder)
    {
        builder.HasKey(i => i.BuyerId);
        builder.Property(i => i.BuyerId).HasMaxLength(256);
        builder.Property(i => i.FinixIdentityId).IsRequired().HasMaxLength(128);
    }
}

public class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> builder)
    {
        builder.HasKey(c => new { c.Scope, c.Key });
        builder.Property(c => c.Scope).HasMaxLength(128);
        builder.Property(c => c.Key).HasMaxLength(256);
        builder.Property(c => c.Status).HasMaxLength(32);
        builder.Property(c => c.Reference).HasMaxLength(128);
        builder.Property(c => c.ResultJson).HasMaxLength(4000);
        builder.Property(c => c.FailureMessage).HasMaxLength(512);
    }
}
