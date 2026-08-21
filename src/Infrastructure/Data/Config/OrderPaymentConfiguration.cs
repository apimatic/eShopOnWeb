using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(64);
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
