using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.HasIndex(x => x.IdentityGuid).IsUnique();
        builder.Property(x => x.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);

        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
