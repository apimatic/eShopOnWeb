using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalOrderId).HasMaxLength(50);
        builder.Property(p => p.AuthorizationId).HasMaxLength(50);
        builder.Property(p => p.AuthIdempotencyKey).HasMaxLength(50).IsRequired();
        builder.Property(p => p.CaptureIdempotencyKey).HasMaxLength(50).IsRequired();
        builder.Property(p => p.CaptureId).HasMaxLength(50);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
