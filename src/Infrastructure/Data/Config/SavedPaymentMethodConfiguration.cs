using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalPaymentTokenId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastFourDigits).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(300);
        builder.HasIndex(p => p.BuyerId);
    }
}

public class PayPalCustomerRefConfiguration : IEntityTypeConfiguration<PayPalCustomerRef>
{
    public void Configure(EntityTypeBuilder<PayPalCustomerRef> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PayPalCustomerId).IsRequired().HasMaxLength(22);
        builder.HasIndex(c => c.BuyerId).IsUnique();
    }
}
