using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.InvoiceId).IsRequired().HasMaxLength(127);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.InstrumentDescription).HasMaxLength(128);

        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(30);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(30);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedGrossAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        var refunds = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("PaymentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
