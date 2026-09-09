using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.VaultId).IsRequired().HasMaxLength(64);
        builder.Property(pm => pm.CustomerId).HasMaxLength(64);
        builder.Property(pm => pm.Brand).IsRequired().HasMaxLength(32);
        builder.Property(pm => pm.LastFourDigits).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(120);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
