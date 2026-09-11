using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(pm => pm.BuyerId);

        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.Brand).HasMaxLength(32);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);

        // The application database intentionally has no column for a card number.
        builder.Ignore(pm => pm.Descriptor);
    }
}
