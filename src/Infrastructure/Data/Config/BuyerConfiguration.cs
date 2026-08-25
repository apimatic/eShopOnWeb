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

        builder.Property(b => b.BuyerId).IsRequired().HasMaxLength(256);
        builder.HasIndex(b => b.BuyerId).IsUnique();

        builder.Property(b => b.PayPalCustomerId).IsRequired().HasMaxLength(64);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
