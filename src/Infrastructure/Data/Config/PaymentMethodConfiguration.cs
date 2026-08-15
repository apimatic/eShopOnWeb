using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.VaultTokenId).IsRequired().HasMaxLength(128);
        builder.Property(pm => pm.Brand).IsRequired().HasMaxLength(32);
        builder.Property(pm => pm.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(128);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
