using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        // A saved card is always looked up by the shopper who saved it, and never by anyone else.
        builder.HasIndex(card => new { card.BuyerId, card.Id });

        builder.Property(card => card.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        // The processor's vault token, not card data.
        builder.Property(card => card.CardId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(card => card.PayPalCustomerId).HasMaxLength(64);
        builder.Property(card => card.Alias).HasMaxLength(64);
        builder.Property(card => card.Last4).HasMaxLength(4);
        builder.Property(card => card.Brand).HasMaxLength(32);
        builder.Property(card => card.Expiry).HasMaxLength(7);
        builder.Property(card => card.CardHolderName).HasMaxLength(128);
        builder.Property(card => card.BillingCountry).HasMaxLength(2);
    }
}
