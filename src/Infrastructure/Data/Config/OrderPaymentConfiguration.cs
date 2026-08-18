using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.PayPalOrderId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizationId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizationStatus)
            .HasMaxLength(32);

        builder.Property(p => p.CaptureId)
            .HasMaxLength(64);

        builder.Property(p => p.CaptureStatus)
            .HasMaxLength(32);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PaymentMethodDescription)
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizedAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CapturedGrossAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetAmount)
            .HasColumnType("decimal(18,2)");

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
