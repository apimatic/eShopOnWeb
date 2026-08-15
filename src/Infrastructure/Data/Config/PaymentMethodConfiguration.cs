using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.Brand).IsRequired().HasMaxLength(40);
        builder.Property(pm => pm.Last4).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(300);
        builder.Property(pm => pm.Alias).HasMaxLength(100);
    }
}
