using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalTokenId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.MerchantCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.LastDigits).HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.CardholderName).HasMaxLength(128);
        builder.HasIndex(x => x.PayPalTokenId).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.IsActive });
    }
}
