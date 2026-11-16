using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.Ignore(p => p.RefundableAmountInCents);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.PaymentToken).IsRequired().HasMaxLength(64);
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.AuthorizationIdempotencyKey).HasMaxLength(128);
        builder.Property(p => p.FinixTransferId).HasMaxLength(64);
        builder.Property(p => p.TransferState).HasMaxLength(32);
        builder.Property(p => p.CaptureIdempotencyKey).HasMaxLength(128);
        builder.Property(p => p.VoidState).HasMaxLength(32);
        builder.Property(p => p.LastError).HasMaxLength(2000);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.FinixAuthorizationId);
        builder.HasIndex(p => p.FinixTransferId);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(r => r.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.FinixRefundTransferId).HasMaxLength(64);
        builder.Property(r => r.FinixState).HasMaxLength(32);
        builder.Property(r => r.LastError).HasMaxLength(2000);

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixRefundTransferId);
        builder.HasIndex(r => r.BuyerId);

        builder.HasOne<OrderPayment>()
            .WithMany()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastFour).HasMaxLength(4);
        builder.Property(p => p.CardType).HasMaxLength(32);
        builder.Property(p => p.Name).HasMaxLength(100);

        builder.HasIndex(p => new { p.BuyerId, p.FinixPaymentInstrumentId }).IsUnique();
        builder.HasIndex(p => p.BuyerId);
    }
}

public class ShopperFinixProfileConfiguration : IEntityTypeConfiguration<ShopperFinixProfile>
{
    public void Configure(EntityTypeBuilder<ShopperFinixProfile> builder)
    {
        builder.ToTable("ShopperFinixProfiles");

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.FinixIdentityId).IsRequired().HasMaxLength(64);

        builder.HasIndex(p => p.BuyerId).IsUnique();
        builder.HasIndex(p => p.FinixIdentityId).IsUnique();
    }
}
