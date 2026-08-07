using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        // Encapsulated collection: EF accesses the backing field, not the read-only property.
        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        // One buyer per identity — the identity is the shopper's username/email.
        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
