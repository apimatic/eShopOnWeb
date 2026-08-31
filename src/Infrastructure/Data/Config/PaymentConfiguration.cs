using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.CorrelationId)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.HasIndex(p => p.OrderId).IsUnique();

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Refunds, refund =>
        {
            refund.WithOwner().HasForeignKey("PaymentId");
            refund.Property<int>("Id");
            refund.HasKey("Id");

            refund.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
            refund.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
            refund.Property(r => r.Status).IsRequired().HasMaxLength(32);
            refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
            refund.Property(r => r.NoteToPayer).HasMaxLength(500);

            refund.HasIndex(r => r.IdempotencyKey);
        });
    }
}
