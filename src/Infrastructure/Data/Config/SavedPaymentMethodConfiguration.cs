using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.VaultToken).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Last4Digits).HasMaxLength(4);
        builder.Property(x => x.CardBrand).HasMaxLength(32);
        builder.Property(x => x.Expiry).HasMaxLength(7);
    }
}
