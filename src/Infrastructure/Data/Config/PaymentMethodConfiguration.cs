using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(pm => pm.CardBrand).HasMaxLength(50);
        builder.Property(pm => pm.LastFourDigits).HasMaxLength(4);
        builder.Property(pm => pm.CardholderName).HasMaxLength(300);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
