using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.PayPalCustomerId)
            .HasMaxLength(64);

        builder.Property(c => c.LastDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(c => c.Brand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(c => c.CardholderName)
            .HasMaxLength(300);

        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.PayPalPaymentTokenId).IsUnique();
    }
}
