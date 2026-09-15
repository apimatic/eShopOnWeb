using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.PayPalCustomerId)
            .HasMaxLength(36);

        builder.Property(p => p.Last4)
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .HasMaxLength(32);

        builder.Property(p => p.Expiry)
            .HasMaxLength(7);

        builder.Property(p => p.CardholderName)
            .HasMaxLength(300);

        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.PayPalVaultId).IsUnique();
    }
}
