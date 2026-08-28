using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalPaymentTokenId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(255);
        builder.Property(p => p.Brand).IsRequired().HasMaxLength(32);
        builder.Property(p => p.Last4).IsRequired().HasMaxLength(4);
        builder.Property(p => p.Expiry).IsRequired().HasMaxLength(7);
        builder.HasIndex(p => p.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(p => new { p.BuyerId, p.Id });
    }
}
