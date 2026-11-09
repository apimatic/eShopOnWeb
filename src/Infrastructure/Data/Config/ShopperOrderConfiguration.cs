using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperOrderConfiguration : IEntityTypeConfiguration<ShopperOrder>
{
    public void Configure(EntityTypeBuilder<ShopperOrder> builder)
    {
        builder.ToTable("ShopperOrders");
        builder.Property(o => o.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.HasIndex(o => o.BuyerEmail);
        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Fee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PaymentMethodId).HasMaxLength(64);
        builder.Property(o => o.PaymentMethodType).HasMaxLength(16);
        builder.Property(o => o.StaxTransactionId).HasMaxLength(64);
        builder.Property(o => o.RefundTransactionId).HasMaxLength(64);
        builder.Property(o => o.PaymentKey).IsRequired().HasMaxLength(32);
        builder.Property(o => o.PaymentError).HasMaxLength(1000);
        builder.Property(o => o.RefundError).HasMaxLength(1000);
        builder.Property(o => o.PaymentAttempts).IsConcurrencyToken();
        builder.Property(o => o.PaymentState).IsConcurrencyToken();
        builder.Property(o => o.RefundState).IsConcurrencyToken();
        builder.HasMany(o => o.Lines).WithOne().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).AutoInclude();
    }
}

public class ShopperOrderLineConfiguration : IEntityTypeConfiguration<ShopperOrderLine>
{
    public void Configure(EntityTypeBuilder<ShopperOrderLine> builder)
    {
        builder.ToTable("ShopperOrderLines");
        builder.Property(l => l.ProductName).IsRequired().HasMaxLength(50);
        builder.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class StaxCustomerLinkConfiguration : IEntityTypeConfiguration<StaxCustomerLink>
{
    public void Configure(EntityTypeBuilder<StaxCustomerLink> builder)
    {
        builder.ToTable("StaxCustomerLinks");
        builder.Property(c => c.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(c => c.Email).IsUnique();
        builder.Property(c => c.StaxCustomerId).IsRequired().HasMaxLength(64);
    }
}
