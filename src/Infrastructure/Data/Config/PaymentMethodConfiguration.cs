using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.VaultId).IsRequired().HasMaxLength(128);
        builder.Property(pm => pm.Brand).HasMaxLength(32);
        builder.Property(pm => pm.LastDigits).HasMaxLength(8);
        builder.Property(pm => pm.Expiry).HasMaxLength(16);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
