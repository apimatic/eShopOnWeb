using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class StaxCustomerConfiguration : IEntityTypeConfiguration<StaxCustomer>
{
    public void Configure(EntityTypeBuilder<StaxCustomer> builder)
    {
        builder.HasKey(c => c.BuyerId);
        builder.Property(c => c.BuyerId).HasMaxLength(256);
        builder.Property(c => c.StaxCustomerId).IsRequired().HasMaxLength(64);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasKey(p => p.StaxPaymentMethodId);
        builder.Property(p => p.StaxPaymentMethodId).HasMaxLength(64);
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.LastFour).HasMaxLength(8);
        builder.Property(p => p.Nickname).HasMaxLength(256);
        builder.HasIndex(p => p.BuyerId);
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(16);
        builder.Property(p => p.StaxTransactionId).HasMaxLength(64);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(p => p.StaxRefundTransactionId).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);
        builder.Property(p => p.RefundMessage).HasMaxLength(1000);
        builder.Property(p => p.OrderTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ChargedTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(p => p.BuyerId);
    }
}
