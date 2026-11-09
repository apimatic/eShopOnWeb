using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PaymentStatus).IsRequired().HasMaxLength(20).IsConcurrencyToken();
        builder.Property(p => p.RefundStatus).IsRequired().HasMaxLength(20);
        builder.Property(p => p.PaymentMethodId).HasMaxLength(64);
        builder.Property(p => p.Method).HasMaxLength(10);
        builder.Property(p => p.StaxTransactionId).HasMaxLength(64);
        builder.Property(p => p.FailureReason).HasMaxLength(1000);
        builder.Property(p => p.RefundNote).HasMaxLength(1000);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
    }
}
