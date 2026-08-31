using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.AuthorizeRequestId).HasMaxLength(108);
        builder.Property(p => p.CaptureRequestId).HasMaxLength(108);

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.BuyerId);

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("PaymentId");
            r.Property<int>("Id");
            r.HasKey("Id");

            r.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.Status).IsRequired().HasMaxLength(32);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(108);
            r.Property(x => x.NoteToPayer).HasMaxLength(256);

            r.HasIndex(x => x.IdempotencyKey);
        });
    }
}
