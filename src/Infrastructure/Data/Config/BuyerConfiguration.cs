using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.Property(x => x.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.HasIndex(x => x.IdentityGuid).IsUnique();
        var paymentMethods = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        paymentMethods?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.PaymentMethods)
            .WithOne(x => x.Buyer)
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
