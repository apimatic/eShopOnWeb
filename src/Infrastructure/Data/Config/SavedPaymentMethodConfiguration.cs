using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayPalTokenId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(36);
        builder.Property(x => x.Brand).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Expiry).HasMaxLength(7).IsRequired();
        builder.HasIndex(x => x.PayPalTokenId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.IsDeleted });
    }
}
