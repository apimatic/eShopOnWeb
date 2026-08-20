using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.CardBrand).HasMaxLength(32);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(32);

        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(108);
    }
}
