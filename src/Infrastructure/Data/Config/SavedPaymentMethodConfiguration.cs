using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalPaymentTokenId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(22);
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(32);
        builder.Property(x => x.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).IsRequired().HasMaxLength(7);
        builder.Ignore(x => x.IsDeleted);
        builder.HasIndex(x => x.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.DeletedAt });
    }
}
