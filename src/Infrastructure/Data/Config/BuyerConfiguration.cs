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

        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        // Saved cards are part of the Buyer aggregate — modelled as an owned collection.
        builder.OwnsMany(b => b.PaymentMethods, pm =>
        {
            pm.WithOwner().HasForeignKey("BuyerId");
            pm.HasKey(nameof(PaymentMethod.Id));
            pm.Property(p => p.Id).ValueGeneratedOnAdd();

            pm.Property(p => p.Alias).HasMaxLength(200);
            pm.Property(p => p.CardId).HasMaxLength(200).IsRequired();
            pm.Property(p => p.Last4).HasMaxLength(4).IsRequired();
            pm.Property(p => p.Brand).HasMaxLength(60);
            pm.Property(p => p.ExpiryMonthYear).HasMaxLength(7);
        });
    }
}
