using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProviderTokenId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ProviderCustomerId).HasMaxLength(128);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.Last4).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(16);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => x.ProviderTokenId).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.IsActive });
    }
}
