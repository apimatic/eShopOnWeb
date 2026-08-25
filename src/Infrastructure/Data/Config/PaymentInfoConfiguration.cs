using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentInfoConfiguration : IEntityTypeConfiguration<PaymentInfo>
{
    public void Configure(EntityTypeBuilder<PaymentInfo> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(PaymentInfo.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.TotalRefunded)
            .HasColumnType("decimal(18,2)");

        builder.HasOne<Order>()
            .WithOne(o => o.Payment)
            .HasForeignKey<PaymentInfo>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
