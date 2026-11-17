using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(payment => payment.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(payment => payment.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(payment => payment.AuthorizationIdempotencyKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(payment => payment.OrderId)
            .IsUnique();

        builder.HasIndex(payment => payment.FinixAuthorizationId);
        builder.HasIndex(payment => payment.FinixCaptureTransferId);

        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
