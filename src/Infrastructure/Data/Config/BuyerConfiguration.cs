using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.IdentityGuid).IsRequired().HasMaxLength(256);
        builder.HasIndex(b => b.IdentityGuid);

        var methods = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        methods?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
