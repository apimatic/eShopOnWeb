using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.Property(b => b.IdentityGuid)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        var navigation = builder.Metadata.FindNavigation(nameof(Buyer.PaymentMethods));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.CardId)
            .HasMaxLength(256);

        builder.Property(p => p.Last4)
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .HasMaxLength(50);

        builder.Property(p => p.Expiry)
            .HasMaxLength(10);

        builder.Property(p => p.Alias)
            .HasMaxLength(100);
    }
}
