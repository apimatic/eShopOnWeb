using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalTokenId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalCustomerId).HasMaxLength(256);
        builder.Property(pm => pm.CardLastFour).HasMaxLength(4);
        builder.Property(pm => pm.CardBrand).HasMaxLength(64);
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);
    }
}
