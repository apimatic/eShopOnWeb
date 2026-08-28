using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.PayPalCaptureId).IsUnique().HasFilter("[PayPalCaptureId] IS NOT NULL");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CaptureRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.VoidRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalCaptureId).HasMaxLength(64);
        builder.Property(x => x.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasPrecision(18, 2);
        builder.Property(x => x.PayPalFee).HasPrecision(18, 2);
        builder.Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Property(x => x.RefundedAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.CurrentAuthorization);
        builder.Ignore(x => x.RefundableAmount);

        var authorizations = builder.Metadata.FindNavigation(nameof(OrderPayment.Authorizations));
        authorizations?.SetPropertyAccessMode(PropertyAccessMode.Field);
        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Authorizations)
            .WithOne()
            .HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
