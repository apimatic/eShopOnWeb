using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.InvoiceId).HasMaxLength(127).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PayPalOrderId).HasMaxLength(36);
        builder.Property(x => x.AuthorizationId).HasMaxLength(64);
        builder.Property(x => x.CaptureId).HasMaxLength(64);
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.PayPalOrderId).IsUnique();
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.HasIndex(x => x.AuthorizationId).IsUnique();
        builder.HasIndex(x => x.CaptureId).IsUnique();

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.OrderPaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}
