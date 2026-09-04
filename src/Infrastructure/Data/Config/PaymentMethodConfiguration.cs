using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(pm => pm.PayPalVaultId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(pm => pm.Brand)
            .HasMaxLength(30);

        builder.Property(pm => pm.LastDigits)
            .HasMaxLength(4);

        builder.Property(pm => pm.Expiry)
            .HasMaxLength(10);

        builder.HasIndex(pm => pm.BuyerId);
        builder.HasIndex(pm => pm.PayPalVaultId).IsUnique();
    }
}
