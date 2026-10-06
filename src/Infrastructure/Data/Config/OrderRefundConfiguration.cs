using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // The composite key serialises refunds of one order, so they can never exceed what was paid.
        builder.HasKey(r => new { r.OrderId, r.Sequence });
        builder.Property(r => r.Sequence).ValueGeneratedNever();

        builder.HasOne<Order>()
            .WithMany(o => o.Refunds)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.RefundId).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.RefundId).IsUnique();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(r => r.PaymentPspReference).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(256);
        builder.Property(r => r.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.ProviderStatus).HasMaxLength(64);
        builder.Property(r => r.ProviderErrorCode).HasMaxLength(64);
        builder.Property(r => r.ProviderMessage).HasMaxLength(1024);
        builder.Property(r => r.ProviderResponse);

        builder.Ignore(r => r.ReservesAmount);
    }
}
