using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Reference).IsRequired().HasMaxLength(127);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(p => p.OrderId).IsUnique();

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("OrderPaymentId");
            r.HasKey(x => x.Id);
            r.Property(x => x.Id).ValueGeneratedOnAdd();
            r.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            r.Property(x => x.Status).HasMaxLength(30);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.HasIndex("OrderPaymentId", nameof(Refund.IdempotencyKey)).IsUnique();
        });
    }
}
