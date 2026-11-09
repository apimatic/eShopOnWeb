using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.PaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(16);
        builder.Property(p => p.TransactionId).HasMaxLength(64);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Message).HasMaxLength(1000);
        builder.Property(p => p.RefundMessage).HasMaxLength(1000);
        builder.Property(p => p.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
    }
}
