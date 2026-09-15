using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.PayPalOrderId).IsUnique();
        builder.HasIndex(x => x.AuthorizationId).IsUnique();
        builder.HasIndex(x => x.CaptureId).IsUnique().HasFilter("[CaptureId] IS NOT NULL");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PayPalOrderId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AuthorizationId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CaptureId).HasMaxLength(32);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.OrderPaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
