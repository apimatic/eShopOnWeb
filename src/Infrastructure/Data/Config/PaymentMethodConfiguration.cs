using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.VaultTokenId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(40);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardHolderName).HasMaxLength(128);
        builder.Property(p => p.Alias).HasMaxLength(128);

        builder.HasIndex(p => p.OwnerId);
    }
}
