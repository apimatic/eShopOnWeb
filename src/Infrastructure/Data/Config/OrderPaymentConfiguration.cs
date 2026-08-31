using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderPayment>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ExternalReference).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => x.ExternalReference).IsUnique();
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Ignore(x => x.RefundedAmount);
        builder.Property(x => x.PayPalOrderId).HasMaxLength(64);
        builder.Property(x => x.AuthorizationId).HasMaxLength(64);
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.CaptureId).HasMaxLength(64);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
