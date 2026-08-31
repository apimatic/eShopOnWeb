using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(p => p.PayPalTokenId).IsUnique();
        builder.HasIndex(p => new { p.BuyerId, p.IsDeleted });
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalTokenId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastDigits).HasMaxLength(8);
        builder.Property(p => p.Expiry).HasMaxLength(16);
        builder.Property(p => p.CardType).HasMaxLength(32);
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}
