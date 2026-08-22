using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.PayPalOrderStatus).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(64);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PaypalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("OrderPaymentRefunds");
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(108).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(r => r.IdempotencyKey);
    }
}
