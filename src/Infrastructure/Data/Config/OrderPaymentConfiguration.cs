using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfigurationPayment : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // Persist the additive fulfilment/payment state on the existing order model.
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // One-to-one: an order optionally owns a payment (part of the aggregate).
        builder.HasOne(o => o.Payment)
            .WithOne()
            .HasForeignKey<OrderPayment>("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Payment).AutoInclude(false);
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.Property(p => p.ProviderOrderId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.AuthorizationId).HasMaxLength(255);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(30);
        builder.Property(p => p.CaptureId).HasMaxLength(255);
        builder.Property(p => p.CaptureStatus).HasMaxLength(30);

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(r => r.ProviderRefundId).IsRequired().HasMaxLength(255);
        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.Status).IsRequired().HasMaxLength(30);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(255);
    }
}
