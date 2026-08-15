using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.CardBrand).HasMaxLength(30);
        builder.Property(p => p.LastFourDigits).IsRequired().HasMaxLength(4);
        builder.Property(p => p.ExpiryMonth).HasMaxLength(2);
        builder.Property(p => p.ExpiryYear).HasMaxLength(4);
        builder.Property(p => p.Label).HasMaxLength(100);

        builder.HasIndex(p => p.BuyerId);
    }
}
