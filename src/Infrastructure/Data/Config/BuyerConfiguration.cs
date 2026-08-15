using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        // Saved cards are part of the buyer aggregate; access the backing collection field.
        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.Property(b => b.PayPalCustomerId).HasMaxLength(64);

        builder.HasIndex(b => b.IdentityGuid);
    }
}
