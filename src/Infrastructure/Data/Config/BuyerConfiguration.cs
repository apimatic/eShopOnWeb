using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        builder.Property(b => b.PayPalCustomerId)
            .HasMaxLength(64);

        // Map the private backing field and cascade-delete a buyer's cards with the buyer / when removed.
        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
