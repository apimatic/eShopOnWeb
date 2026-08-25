using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.VaultToken)
            .HasMaxLength(500);

        builder.Property(pm => pm.Last4)
            .HasMaxLength(4);

        builder.Property(pm => pm.CardBrand)
            .HasMaxLength(50);

        builder.Property(pm => pm.ExpiryMonth)
            .HasMaxLength(2);

        builder.Property(pm => pm.ExpiryYear)
            .HasMaxLength(4);

        builder.Property(pm => pm.Alias)
            .HasMaxLength(100);
    }
}
