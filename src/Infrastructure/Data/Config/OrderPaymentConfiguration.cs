using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.ReferenceKey)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PaypalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(64);

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
        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
