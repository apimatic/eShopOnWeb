using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.CardBrand)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(pm => pm.LastFourDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(pm => pm.CardholderName)
            .HasMaxLength(300);

        // Queries are always scoped by owner; index the owner for those lookups.
        builder.HasIndex(pm => pm.BuyerId);
    }
}
