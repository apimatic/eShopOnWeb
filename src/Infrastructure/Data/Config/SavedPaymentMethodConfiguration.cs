using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.Last4).HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.CardholderName).HasMaxLength(256);

        builder.HasIndex(x => x.BuyerId);
    }
}
