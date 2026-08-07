using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        // Saved cards are owned by the buyer aggregate; access the collection through its backing field
        // so it can only be mutated via Buyer's methods.
        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        builder.Property(b => b.PayPalCustomerId)
            .HasMaxLength(64);

        // Required FK + cascade so that removing a card from the buyer aggregate deletes its row
        // (orphan removal) rather than leaving a dangling, unowned payment method behind.
        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
