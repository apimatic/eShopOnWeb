using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        builder.OwnsMany(b => b.PaymentMethods, pm =>
        {
            pm.WithOwner();
            pm.Property(p => p.CardId).HasMaxLength(64).IsRequired();
            pm.Property(p => p.Brand).HasMaxLength(32).IsRequired();
            pm.Property(p => p.Last4).HasMaxLength(4).IsRequired();
            pm.Property(p => p.Expiry).HasMaxLength(7).IsRequired();
            pm.Property(p => p.Alias).HasMaxLength(64);
        });
    }
}
