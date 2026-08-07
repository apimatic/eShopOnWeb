using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.CardBrand)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pm => pm.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(pm => pm.Alias)
            .HasMaxLength(100);
    }
}
