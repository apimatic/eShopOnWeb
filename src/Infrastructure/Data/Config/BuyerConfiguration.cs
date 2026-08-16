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

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .IsRequired();
        builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods))
            ?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
