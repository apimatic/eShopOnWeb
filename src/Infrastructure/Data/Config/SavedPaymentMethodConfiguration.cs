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

        builder.Property(pm => pm.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.PayPalCustomerId)
            .HasMaxLength(256);

        builder.Property(pm => pm.CardBrand)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pm => pm.CardLast4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.CardExpiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(pm => pm.CardholderName)
            .HasMaxLength(256);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
