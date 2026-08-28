using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.ToTable("Buyers");
        builder.Property(x => x.IdentityGuid).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => x.IdentityGuid).IsUnique();

        var methods = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        methods?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.PaymentMethods)
            .WithOne()
            .HasForeignKey("BuyerId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
