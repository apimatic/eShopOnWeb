using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(pm => pm.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        // The PayPal vault token id — never card data.
        builder.Property(pm => pm.VaultToken)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(pm => pm.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Brand)
            .HasMaxLength(50);

        builder.Property(pm => pm.ExpiryMonthYear)
            .HasMaxLength(7);

        builder.Property(pm => pm.CardholderName)
            .HasMaxLength(300);

        builder.Property(pm => pm.Alias)
            .HasMaxLength(100);

        builder.HasIndex(pm => pm.OwnerId);
    }
}
