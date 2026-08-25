using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.VaultCustomerId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.PayPalTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.Last4)
            .HasMaxLength(4);

        builder.Property(pm => pm.Brand)
            .HasMaxLength(32);

        builder.Property(pm => pm.Expiry)
            .HasMaxLength(10);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
