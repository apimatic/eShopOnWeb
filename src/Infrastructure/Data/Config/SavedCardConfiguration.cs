using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.PayPalCustomerId)
            .HasMaxLength(128);

        builder.Property(c => c.Brand)
            .HasMaxLength(30);

        builder.Property(c => c.Last4)
            .HasMaxLength(4);

        builder.Property(c => c.ExpiryYearMonth)
            .HasMaxLength(7);

        builder.Property(c => c.CardholderName)
            .HasMaxLength(300);

        builder.HasIndex(c => c.BuyerId);
    }
}
