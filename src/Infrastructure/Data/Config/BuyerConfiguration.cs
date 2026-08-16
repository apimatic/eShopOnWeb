using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.Property(b => b.IdentityGuid)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(b => b.IdentityGuid);

        var paymentMethods = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        paymentMethods?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
