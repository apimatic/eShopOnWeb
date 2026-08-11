using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.Property(b => b.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        // Saved cards are dependent entities of the Buyer aggregate (shadow FK to the buyer).
        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.Alias).HasMaxLength(100);
        builder.Property(pm => pm.CardId).HasMaxLength(64);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Brand).HasMaxLength(40);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
    }
}
