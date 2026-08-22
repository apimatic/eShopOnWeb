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

        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(b => b.IdentityGuid)
            .IsUnique();

        builder.Property(b => b.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(36);

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.CardId)
            .HasMaxLength(255);

        builder.Property(p => p.Last4)
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .HasMaxLength(50);

        builder.Property(p => p.Expiry)
            .HasMaxLength(7);

        builder.Property(p => p.Alias)
            .HasMaxLength(100);
    }
}
