using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.Alias)
            .IsRequired()
            .HasMaxLength(128);

        // PayPal Vault payment-token id — a reference, never the card number.
        builder.Property(pm => pm.CardId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Brand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(pm => pm.Expiry)
            .IsRequired()
            .HasMaxLength(7); // "YYYY-MM"
    }
}
