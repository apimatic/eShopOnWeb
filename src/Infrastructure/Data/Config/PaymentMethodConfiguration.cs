using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProviderTokenId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ProviderCustomerId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.LastDigits).HasMaxLength(8);
        builder.Property(x => x.Expiry).HasMaxLength(16);
        builder.Property(x => x.CardType).HasMaxLength(32);
        builder.HasIndex(x => x.ProviderTokenId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.IsActive });
    }
}
