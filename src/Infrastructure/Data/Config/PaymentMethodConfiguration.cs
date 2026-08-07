using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

/// <summary>
/// Persistence for a shopper's saved (vaulted) card. Only the PayPal vault token and safe display
/// metadata are stored — never full card details.
/// </summary>
public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.CardId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(pm => pm.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.CardBrand).HasMaxLength(32);
        builder.Property(pm => pm.Alias).HasMaxLength(256);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
