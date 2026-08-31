using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(32);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.CardBrand).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.RequestSeed).IsRequired().HasMaxLength(32);
        builder.Property(p => p.CreateRequestId).HasMaxLength(128);
        builder.Property(p => p.AuthorizeRequestId).HasMaxLength(128);
        builder.Property(p => p.CaptureRequestId).HasMaxLength(128);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(p => p.OrderId);

        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.ToTable("PaymentRefunds");
            r.WithOwner().HasForeignKey("PaymentId");
            r.HasKey("PaymentId", nameof(PaymentRefund.Id));
            r.Property(x => x.Id).ValueGeneratedOnAdd();
            r.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.Status).IsRequired().HasMaxLength(32);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });
    }
}
