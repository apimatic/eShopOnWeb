using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PaymentTokenId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(256);
        builder.Property(m => m.Last4).HasMaxLength(10);
        builder.Property(m => m.Brand).HasMaxLength(50);
        builder.Property(m => m.Expiry).HasMaxLength(10);
    }
}
