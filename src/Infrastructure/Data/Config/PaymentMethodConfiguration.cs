using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasKey(pm => pm.Id);

        // Only the PayPal vault token and a safe descriptor are stored — never full card details.
        builder.Property(pm => pm.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.Alias).HasMaxLength(120);
        builder.Property(pm => pm.Brand).HasMaxLength(40);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
    }
}
