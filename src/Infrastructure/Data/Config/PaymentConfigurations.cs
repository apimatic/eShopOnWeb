using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(payment => payment.OrderId).IsUnique();
        builder.Property(payment => payment.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(payment => payment.Status).IsRequired().HasMaxLength(32);
        builder.Property(payment => payment.PaymentInstrumentId).HasMaxLength(128);
        builder.Property(payment => payment.AuthorizationId).HasMaxLength(128);
        builder.Property(payment => payment.AuthorizationState).HasMaxLength(32);
        builder.Property(payment => payment.TransferId).HasMaxLength(128);
        builder.Property(payment => payment.IdempotencyId).HasMaxLength(128);
        builder.Property(payment => payment.FailureCode).HasMaxLength(128);
        builder.Property(payment => payment.FailureMessage).HasMaxLength(1024);
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.HasIndex(card => new { card.BuyerId, card.PaymentInstrumentId }).IsUnique();
        builder.Property(card => card.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(card => card.FinixIdentityId).IsRequired().HasMaxLength(128);
        builder.Property(card => card.PaymentInstrumentId).IsRequired().HasMaxLength(128);
        builder.Property(card => card.Brand).HasMaxLength(64);
        builder.Property(card => card.Last4).HasMaxLength(4);
        builder.Property(card => card.Fingerprint).HasMaxLength(128);
    }
}

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasIndex(record => new { record.OrderId, record.IdempotencyKey }).IsUnique();
        builder.Property(record => record.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(record => record.TransferId).IsRequired().HasMaxLength(128);
        builder.Property(record => record.RefundTransferId).IsRequired().HasMaxLength(128);
        builder.Property(record => record.State).IsRequired().HasMaxLength(32);
        builder.Property(record => record.IdempotencyKey).IsRequired().HasMaxLength(128);
    }
}

public class PaymentOperationClaimConfiguration : IEntityTypeConfiguration<PaymentOperationClaim>
{
    public void Configure(EntityTypeBuilder<PaymentOperationClaim> builder)
    {
        builder.HasKey(claim => new { claim.OrderId, claim.Operation });
        builder.Property(claim => claim.Operation).IsRequired().HasMaxLength(32);
    }
}

public class RefundClaimConfiguration : IEntityTypeConfiguration<RefundClaim>
{
    public void Configure(EntityTypeBuilder<RefundClaim> builder)
    {
        builder.HasKey(claim => new { claim.OrderId, claim.IdempotencyKey });
        builder.Property(claim => claim.IdempotencyKey).IsRequired().HasMaxLength(128);
    }
}

public class ShopperProfileConfiguration : IEntityTypeConfiguration<ShopperProfile>
{
    public void Configure(EntityTypeBuilder<ShopperProfile> builder)
    {
        builder.HasKey(profile => profile.BuyerId);
        builder.Property(profile => profile.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(profile => profile.FinixIdentityId).IsRequired().HasMaxLength(128);
        builder.Property(profile => profile.Email).HasMaxLength(256);
    }
}
