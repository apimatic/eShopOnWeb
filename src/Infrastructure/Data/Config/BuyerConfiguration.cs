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

        builder.Property(b => b.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(22);

        builder.HasIndex(b => b.IdentityGuid).IsUnique();

        builder.HasMany(b => b.PaymentMethods)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(p => p.CardId)
            .HasMaxLength(255);

        builder.Property(p => p.Last4)
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .HasMaxLength(32);

        builder.Property(p => p.Expiry)
            .HasMaxLength(7);

        builder.Property(p => p.Alias)
            .HasMaxLength(64);

        builder.Property(p => p.CardholderName)
            .HasMaxLength(300);
    }
}
