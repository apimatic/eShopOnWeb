using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(x => x.PayPalVaultId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.DeletedAt });
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(32);
        builder.Property(x => x.LastFour).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).IsRequired().HasMaxLength(7);
    }
}
