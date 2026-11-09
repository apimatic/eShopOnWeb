using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class StaxCustomerLinkConfiguration : IEntityTypeConfiguration<StaxCustomerLink>
{
    public void Configure(EntityTypeBuilder<StaxCustomerLink> builder)
    {
        builder.Property(x => x.Email).IsRequired().HasMaxLength(256);
        builder.Property(x => x.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.Email).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Method).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Last4).HasMaxLength(8);
        builder.Property(x => x.Description).HasMaxLength(256);
        builder.HasIndex(x => x.StaxPaymentMethodId).IsUnique();
        builder.HasIndex(x => x.BuyerEmail);
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(x => x.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Method).IsRequired().HasMaxLength(16);
        builder.Property(x => x.StaxTransactionId).HasMaxLength(64);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(16);
        builder.Property(x => x.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(x => x.LastError).HasMaxLength(1024);
        builder.Property(x => x.RefundMessage).HasMaxLength(1024);
        builder.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => x.OrderId).IsUnique();
    }
}
