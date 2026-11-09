using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperAccountConfiguration : IEntityTypeConfiguration<ShopperAccount>
{
    public void Configure(EntityTypeBuilder<ShopperAccount> builder)
    {
        builder.Property(x => x.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.StaxCustomerId).IsRequired().HasMaxLength(64);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.ShopperEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.StaxPaymentMethodId).IsUnique();
        builder.HasIndex(x => x.ShopperEmail);
        builder.Property(x => x.Kind).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Last4).IsRequired().HasMaxLength(8);
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.BuyerEmail);
        builder.Property(x => x.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(16);
        builder.Property(x => x.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(x => x.StaxPaymentMethodId).HasMaxLength(64);
        builder.Property(x => x.StaxTransactionId).HasMaxLength(64);
        builder.Property(x => x.Method).HasMaxLength(16);
        builder.Property(x => x.LastError).HasMaxLength(500);
    }
}
