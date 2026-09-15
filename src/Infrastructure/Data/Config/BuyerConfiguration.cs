using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.IdentityGuid).HasMaxLength(256).IsRequired();
        builder.HasIndex(b => b.IdentityGuid);
        builder.Property(b => b.PayPalCustomerId).HasMaxLength(64);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .OnDelete(DeleteBehavior.Cascade);

        var nav = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        nav?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
