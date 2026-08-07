using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.Alias).HasMaxLength(100);
        builder.Property(pm => pm.PayPalTokenId).IsRequired().HasMaxLength(128);
        builder.Property(pm => pm.CardBrand).HasMaxLength(40);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
    }
}
